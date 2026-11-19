using System;

namespace DockBar
{
    internal sealed class MediaSessionSnapshot
    {
        internal static readonly MediaSessionSnapshot Empty = new MediaSessionSnapshot();

        internal string SourceAppUserModelId { get; set; }
        internal string Title { get; set; }
        internal string Artist { get; set; }
        internal byte[] Artwork { get; set; }
        internal bool IsPlaying { get; set; }
        internal bool CanPlayPause { get; set; }
        internal bool CanPrevious { get; set; }
        internal bool CanNext { get; set; }

        internal bool HasMedia
        {
            get { return !String.IsNullOrWhiteSpace(Title); }
        }

        internal string Signature
        {
            get
            {
                return String.Join("|", new[]
                {
                    SourceAppUserModelId ?? String.Empty,
                    Title ?? String.Empty,
                    Artist ?? String.Empty,
                    IsPlaying.ToString(),
                    CanPlayPause.ToString(),
                    CanPrevious.ToString(),
                    CanNext.ToString(),
                    Artwork == null ? "0" : Artwork.Length.ToString()
                });
            }
        }
    }
}
