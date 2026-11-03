using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DockBar
{
    internal static class AudioController
    {
        private const int Render = 0;
        private const int Multimedia = 1;

        public static bool TryGetState(out float volume, out bool muted)
        {
            volume = 0;
            muted = false;
            IAudioEndpointVolume endpoint = GetEndpoint();
            if (endpoint == null) return false;
            try
            {
                endpoint.GetMasterVolumeLevelScalar(out volume);
                endpoint.GetMute(out muted);
                return true;
            }
            catch { return false; }
            finally { Marshal.FinalReleaseComObject(endpoint); }
        }

        public static void ToggleMute()
        {
            IAudioEndpointVolume endpoint = GetEndpoint();
            if (endpoint == null) return;
            try
            {
                bool muted;
                endpoint.GetMute(out muted);
                Guid context = Guid.Empty;
                endpoint.SetMute(!muted, ref context);
            }
            catch { }
            finally { Marshal.FinalReleaseComObject(endpoint); }
        }

        private static IAudioEndpointVolume GetEndpoint()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out device));
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object endpoint;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out endpoint));
                return (IAudioEndpointVolume)endpoint;
            }
            catch { return null; }
            finally
            {
                if (device != null) Marshal.FinalReleaseComObject(device);
                if (enumerator != null) Marshal.FinalReleaseComObject(enumerator);
            }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        private interface IAudioEndpointVolume
        {
            int NotImpl1();
            int NotImpl2();
            int GetChannelCount(out int channelCount);
            int SetMasterVolumeLevel(float levelDB, ref Guid eventContext);
            int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
            int GetMasterVolumeLevel(out float levelDB);
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint channelNumber, float levelDB, ref Guid eventContext);
            int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);
            int GetChannelVolumeLevel(uint channelNumber, out float levelDB);
            int GetChannelVolumeLevelScalar(uint channelNumber, out float level);
            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
            [PreserveSig]
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
        }
    }
}
