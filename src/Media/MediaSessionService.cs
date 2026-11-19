using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DockBar
{
    /// <summary>
    /// Reads the Windows global media session without requiring a Windows SDK
    /// reference at build time. The WinRT projection is resolved at runtime so
    /// the portable .NET Framework build remains self-contained.
    /// </summary>
    internal sealed class MediaSessionService : IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1250);
        private static readonly TimeSpan MissingSessionGrace = TimeSpan.FromMilliseconds(1750);
        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(15);

        private readonly object sessionLock = new object();
        private System.Threading.Timer timer;
        private object manager;
        private object currentSession;
        private Type managerType;
        private Type sessionType;
        private DateTime retryAfterUtc;
        private DateTime missingSinceUtc = DateTime.MinValue;
        private MediaSessionSnapshot current = MediaSessionSnapshot.Empty;
        private int refreshRunning;
        private bool disposed;
        private string lastDiagnostic = String.Empty;

        internal event EventHandler SnapshotChanged;

        internal MediaSessionSnapshot Current
        {
            get { return current; }
        }

        internal void Start()
        {
            if (disposed || timer != null) return;
            BeginDiagnosticSession();
            timer = new System.Threading.Timer(Poll, null, 0, (int)PollInterval.TotalMilliseconds);
        }

        internal async Task TogglePlayPauseAsync()
        {
            await RunControlAsync("TryTogglePlayPauseAsync").ConfigureAwait(false);
        }

        internal async Task SkipPreviousAsync()
        {
            await RunControlAsync("TrySkipPreviousAsync").ConfigureAwait(false);
        }

        internal async Task SkipNextAsync()
        {
            await RunControlAsync("TrySkipNextAsync").ConfigureAwait(false);
        }

        private async Task RunControlAsync(string methodName)
        {
            object session;
            lock (sessionLock) session = currentSession;
            if (session == null || sessionType == null || disposed) return;
            try
            {
                MethodInfo method = sessionType.GetMethod(methodName, Type.EmptyTypes);
                object operation = method.Invoke(session, null);
                await WinRtBridge.AwaitResultAsync(operation, typeof(bool)).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
            catch (Exception error) { WriteDiagnostic("control " + methodName, error); }
        }

        private async void Poll(object ignored)
        {
            if (disposed || Interlocked.CompareExchange(ref refreshRunning, 1, 0) != 0) return;
            try { await RefreshAsync().ConfigureAwait(false); }
            catch (Exception error)
            {
                WriteDiagnostic("poll", error);
                retryAfterUtc = DateTime.UtcNow.Add(RetryAfterFailure);
            }
            finally { Interlocked.Exchange(ref refreshRunning, 0); }
        }

        private async Task RefreshAsync()
        {
            if (disposed || DateTime.UtcNow < retryAfterUtc) return;
            if (manager == null)
            {
                try { await InitializeAsync().ConfigureAwait(false); }
                catch (Exception error)
                {
                    WriteDiagnostic("initialize", error);
                    retryAfterUtc = DateTime.UtcNow.Add(RetryAfterFailure);
                    PublishMissing();
                    return;
                }
            }

            object session;
            try { session = managerType.GetMethod("GetCurrentSession", Type.EmptyTypes).Invoke(manager, null); }
            catch (Exception error)
            {
                WriteDiagnostic("current session", error);
                manager = null;
                retryAfterUtc = DateTime.UtcNow.Add(RetryAfterFailure);
                PublishMissing();
                return;
            }

            if (session == null)
            {
                lock (sessionLock) currentSession = null;
                PublishMissing();
                return;
            }

            missingSinceUtc = DateTime.MinValue;
            lock (sessionLock) currentSession = session;

            MethodInfo propertiesMethod = sessionType.GetMethod("TryGetMediaPropertiesAsync", Type.EmptyTypes);
            object propertiesOperation = propertiesMethod.Invoke(session, null);
            object properties = await WinRtBridge.AwaitResultAsync(
                propertiesOperation,
                WinRtBridge.AsyncResultType(propertiesMethod)).ConfigureAwait(false);
            if (properties == null) { PublishMissing(); return; }

            string title = ReadString(properties, "Title");
            string artist = ReadString(properties, "Artist");
            string sourceId = ReadString(session, "SourceAppUserModelId", sessionType);
            object playback = sessionType.GetMethod("GetPlaybackInfo", Type.EmptyTypes).Invoke(session, null);
            bool playing = String.Equals(ReadValue(playback, "PlaybackStatus").ToString(), "Playing", StringComparison.OrdinalIgnoreCase);
            object controls = ReadValue(playback, "Controls");

            byte[] artwork = null;
            bool sameTrack = current.HasMedia &&
                String.Equals(current.SourceAppUserModelId, sourceId, StringComparison.Ordinal) &&
                String.Equals(current.Title, title, StringComparison.Ordinal) &&
                String.Equals(current.Artist, artist, StringComparison.Ordinal);
            if (sameTrack) artwork = current.Artwork;
            else
            {
                object thumbnail = ReadValue(properties, "Thumbnail");
                if (thumbnail != null) artwork = await WinRtBridge.ReadStreamReferenceAsync(thumbnail).ConfigureAwait(false);
            }

            var snapshot = new MediaSessionSnapshot
            {
                SourceAppUserModelId = sourceId,
                Title = title,
                Artist = artist,
                Artwork = artwork,
                IsPlaying = playing,
                CanPlayPause = ReadBoolean(controls, "IsPlayPauseToggleEnabled", true),
                CanPrevious = ReadBoolean(controls, "IsPreviousEnabled", true),
                CanNext = ReadBoolean(controls, "IsNextEnabled", true)
            };
            if (!snapshot.HasMedia) { PublishMissing(); return; }
            WriteDiagnostic("media detected", null);
            Publish(snapshot);
        }

        private async Task InitializeAsync()
        {
            managerType = Type.GetType(
                "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime",
                true);
            sessionType = Type.GetType(
                "Windows.Media.Control.GlobalSystemMediaTransportControlsSession, Windows.Media.Control, ContentType=WindowsRuntime",
                true);
            MethodInfo requestMethod = managerType.GetMethod("RequestAsync", BindingFlags.Public | BindingFlags.Static);
            object operation = requestMethod.Invoke(null, null);
            manager = await WinRtBridge.AwaitResultAsync(operation, managerType).ConfigureAwait(false);
            if (manager == null) throw new InvalidOperationException("O gerenciador de mídia do Windows não está disponível.");
            retryAfterUtc = DateTime.MinValue;
            WriteDiagnostic("ready", null);
        }

        private void PublishMissing()
        {
            if (!current.HasMedia) return;
            if (missingSinceUtc == DateTime.MinValue)
            {
                missingSinceUtc = DateTime.UtcNow;
                return;
            }
            if (DateTime.UtcNow - missingSinceUtc < MissingSessionGrace) return;
            Publish(MediaSessionSnapshot.Empty);
        }

        private void Publish(MediaSessionSnapshot snapshot)
        {
            if (disposed || snapshot == null || snapshot.Signature == current.Signature) return;
            current = snapshot;
            EventHandler handler = SnapshotChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static object ReadValue(object target, string property, Type knownType = null)
        {
            if (target == null) return null;
            Type type = knownType ?? target.GetType();
            PropertyInfo info = type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            return info == null ? null : info.GetValue(target, null);
        }

        private static string ReadString(object target, string property, Type knownType = null)
        {
            object value = ReadValue(target, property, knownType);
            return value == null ? String.Empty : value.ToString();
        }

        private static bool ReadBoolean(object target, string property, bool fallback)
        {
            object value = ReadValue(target, property);
            return value is bool ? (bool)value : fallback;
        }

        public void Dispose()
        {
            disposed = true;
            System.Threading.Timer oldTimer = Interlocked.Exchange(ref timer, null);
            if (oldTimer != null) oldTimer.Dispose();
            lock (sessionLock) currentSession = null;
            manager = null;
            SnapshotChanged = null;
        }

        private void BeginDiagnosticSession()
        {
            try
            {
                string path = DiagnosticPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, DateTime.Now.ToString("s") + " media session starting" + Environment.NewLine);
            }
            catch { }
        }

        private void WriteDiagnostic(string stage, Exception error)
        {
            try
            {
                Exception actual = error;
                while (actual != null && actual is TargetInvocationException && actual.InnerException != null)
                    actual = actual.InnerException;
                string signature = stage + "|" + (actual == null ? "ok" : actual.HResult.ToString("X8") + "|" + actual.Message);
                if (String.Equals(signature, lastDiagnostic, StringComparison.Ordinal)) return;
                lastDiagnostic = signature;
                string line = DateTime.Now.ToString("s") + " " + stage;
                if (actual != null)
                    line += " 0x" + actual.HResult.ToString("X8") + " " + actual.GetType().Name + ": " + actual.Message.Replace(Environment.NewLine, " ");
                File.AppendAllText(DiagnosticPath, line + Environment.NewLine);
            }
            catch { }
        }

        private static string DiagnosticPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMFlow", "media.log"); }
        }

        private static class WinRtBridge
        {
            private static readonly Assembly runtimeAssembly = LoadRuntimeAssembly();
            private static readonly Type asyncExtensions = runtimeAssembly.GetType("System.WindowsRuntimeSystemExtensions", true);
            private static readonly Type streamExtensions = runtimeAssembly.GetType("System.IO.WindowsRuntimeStreamExtensions", true);

            internal static Type AsyncResultType(MethodInfo method)
            {
                if (method == null) throw new ArgumentNullException("method");
                Type returnType = method.ReturnType;
                if (!returnType.IsGenericType) throw new InvalidOperationException("Operação WinRT sem resultado genérico.");
                return returnType.GetGenericArguments()[0];
            }

            internal static async Task<object> AwaitResultAsync(object operation, Type resultType)
            {
                if (operation == null) return null;
                if (resultType == null) throw new ArgumentNullException("resultType");
                MethodInfo asTask = asyncExtensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(method => method.Name == "AsTask" && method.IsGenericMethodDefinition)
                    .Where(method => method.GetParameters().Length == 1)
                    .First(method => method.GetParameters()[0].ParameterType.Name.StartsWith("IAsyncOperation`1", StringComparison.Ordinal));
                Task task = (Task)asTask.MakeGenericMethod(resultType).Invoke(null, new[] { operation });
                await task.ConfigureAwait(false);
                return task.GetType().GetProperty("Result").GetValue(task, null);
            }

            internal static async Task<byte[]> ReadStreamReferenceAsync(object streamReference)
            {
                try
                {
                    MethodInfo open = streamReference.GetType().GetMethod("OpenReadAsync", Type.EmptyTypes);
                    if (open == null) return null;
                    object randomAccessStream = await AwaitResultAsync(
                        open.Invoke(streamReference, null),
                        AsyncResultType(open)).ConfigureAwait(false);
                    if (randomAccessStream == null) return null;
                    MethodInfo asStream = streamExtensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(method => method.Name == "AsStreamForRead" && method.GetParameters().Length == 1)
                        .FirstOrDefault(method => method.GetParameters()[0].ParameterType.IsInstanceOfType(randomAccessStream));
                    if (asStream == null) return null;
                    using (Stream source = (Stream)asStream.Invoke(null, new[] { randomAccessStream }))
                    using (var memory = new MemoryStream())
                    {
                        await source.CopyToAsync(memory).ConfigureAwait(false);
                        if (memory.Length <= 0 || memory.Length > 8 * 1024 * 1024) return null;
                        return memory.ToArray();
                    }
                }
                catch { return null; }
            }

            private static Assembly LoadRuntimeAssembly()
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string framework = Environment.Is64BitProcess ? "Framework64" : "Framework";
                string path = Path.Combine(windows, "Microsoft.NET", framework, "v4.0.30319", "System.Runtime.WindowsRuntime.dll");
                return Assembly.LoadFrom(path);
            }
        }
    }
}
