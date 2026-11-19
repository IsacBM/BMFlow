using System;
using System.Runtime.InteropServices;

namespace DockBar
{
    internal struct BluetoothRadioStatus
    {
        public bool Available;
        public bool Enabled;
    }

    internal static class BluetoothRadioReader
    {
        private const uint InProcessServer = 1;
        private static readonly Guid BluetoothRadioManagerClass = new Guid("AFD198AC-5F30-4E89-A789-5DDF60A69366");

        internal static BluetoothRadioStatus Read()
        {
            object managerObject = null;
            IRadioInstanceCollection radios = null;
            try
            {
                int result = CoCreateInstance(
                    BluetoothRadioManagerClass,
                    IntPtr.Zero,
                    InProcessServer,
                    typeof(IMediaRadioManager).GUID,
                    out managerObject);
                var manager = managerObject as IMediaRadioManager;
                if (result != 0 || manager == null || manager.GetRadioInstances(out radios) != 0 || radios == null)
                    return new BluetoothRadioStatus();

                uint count;
                if (radios.GetCount(out count) != 0 || count == 0) return new BluetoothRadioStatus();

                var status = new BluetoothRadioStatus { Available = true };
                for (uint index = 0; index < count; index++)
                {
                    IRadioInstance radio = null;
                    try
                    {
                        DeviceRadioState state;
                        if (radios.GetAt(index, out radio) == 0 && radio != null && radio.GetRadioState(out state) == 0 &&
                            (state == DeviceRadioState.On || state == DeviceRadioState.OnUncontrollable))
                        {
                            status.Enabled = true;
                            break;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(radio);
                    }
                }
                return status;
            }
            catch
            {
                return new BluetoothRadioStatus();
            }
            finally
            {
                ReleaseComObject(radios);
                ReleaseComObject(managerObject);
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try { Marshal.ReleaseComObject(value); } catch { }
        }

        private enum DeviceRadioState
        {
            On = 0,
            SoftwareOff = 1,
            HardwareOff = 2,
            SoftwareAndHardwareOff = 3,
            OnUncontrollable = 4,
            Invalid = 5,
            HardwareOffUncontrollable = 6
        }

        [ComImport]
        [Guid("6CFDCAB5-FC47-42A5-9241-074B58830E73")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMediaRadioManager
        {
            [PreserveSig]
            int GetRadioInstances([MarshalAs(UnmanagedType.Interface)] out IRadioInstanceCollection collection);

            [PreserveSig]
            int OnSystemRadioStateChange(int state, uint timeoutSeconds);
        }

        [ComImport]
        [Guid("E5791FAE-5665-4E0C-95BE-5FDE31644185")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IRadioInstanceCollection
        {
            [PreserveSig]
            int GetCount(out uint count);

            [PreserveSig]
            int GetAt(uint index, [MarshalAs(UnmanagedType.Interface)] out IRadioInstance radio);
        }

        [ComImport]
        [Guid("70AA1C9E-F2B4-4C61-86D3-6B9FB75FD1A2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IRadioInstance
        {
            [PreserveSig]
            int GetRadioManagerSignature(out Guid signature);

            [PreserveSig]
            int GetInstanceSignature([MarshalAs(UnmanagedType.BStr)] out string identifier);

            [PreserveSig]
            int GetFriendlyName(uint locale, [MarshalAs(UnmanagedType.BStr)] out string name);

            [PreserveSig]
            int GetRadioState(out DeviceRadioState state);

            [PreserveSig]
            int SetRadioState(DeviceRadioState state, uint timeoutSeconds);

            [PreserveSig]
            [return: MarshalAs(UnmanagedType.Bool)]
            bool IsMultiComm();

            [PreserveSig]
            [return: MarshalAs(UnmanagedType.Bool)]
            bool IsAssociatingDevice();
        }

        [DllImport("ole32.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int CoCreateInstance(
            [In] Guid classId,
            IntPtr outer,
            uint context,
            [In] Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out object instance);
    }
}
