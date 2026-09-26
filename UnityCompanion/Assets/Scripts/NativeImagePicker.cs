using System;
using System.Runtime.InteropServices;
using System.Text;

namespace KurisuCompanion
{
    internal static class NativeImagePicker
    {
        // Keep OPENFILENAME entirely blittable. Marshaling StringBuilder fields inside
        // this native struct caused a stack-overflow exception on the target machine.
        [StructLayout(LayoutKind.Sequential)]
        private struct OpenFileName
        {
            public int Size;
            public IntPtr Owner;
            public IntPtr Instance;
            public IntPtr Filter;
            public IntPtr CustomFilter;
            public int MaxCustomFilter;
            public int FilterIndex;
            public IntPtr File;
            public int MaxFile;
            public IntPtr FileTitle;
            public int MaxFileTitle;
            public IntPtr InitialDirectory;
            public IntPtr Title;
            public int Flags;
            public short FileOffset;
            public short FileExtension;
            public IntPtr DefaultExtension;
            public IntPtr CustomerData;
            public IntPtr Hook;
            public IntPtr TemplateName;
            public IntPtr Reserved;
            public int ReservedInt;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetOpenFileName(ref OpenFileName dialog);
        [DllImport("comdlg32.dll")] private static extern int CommDlgExtendedError();
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();

        private static IntPtr AppWindow()
        {
            IntPtr window = GetActiveWindow();
            if (window == IntPtr.Zero) window = GetForegroundWindow();
            if (window == IntPtr.Zero) return IntPtr.Zero;
            GetWindowThreadProcessId(window, out uint processId);
            return processId == GetCurrentProcessId() ? window : IntPtr.Zero;
        }

        public static string Pick()
        {
            const int fileCapacity = 4096;
            IntPtr file = IntPtr.Zero, filter = IntPtr.Zero, title = IntPtr.Zero;
            try
            {
                file = Marshal.AllocHGlobal(fileCapacity * sizeof(char));
                Marshal.WriteInt16(file, 0);
                byte[] filterBytes = Encoding.Unicode.GetBytes(
                    "图片 (*.png;*.jpg;*.jpeg)\0*.png;*.jpg;*.jpeg\0所有文件\0*.*\0\0");
                filter = Marshal.AllocHGlobal(filterBytes.Length);
                Marshal.Copy(filterBytes, 0, filter, filterBytes.Length);
                title = Marshal.StringToHGlobalUni("选择图片或游戏截图");

                var dialog = new OpenFileName
                {
                    Size = Marshal.SizeOf<OpenFileName>(),
                    Owner = AppWindow(),
                    Filter = filter,
                    FilterIndex = 1,
                    File = file,
                    MaxFile = fileCapacity,
                    Title = title,
                    Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008
                };
                if (GetOpenFileName(ref dialog)) return Marshal.PtrToStringUni(file);
                int error = CommDlgExtendedError();
                if (error != 0)
                    throw new InvalidOperationException("Windows 文件选择器打开失败（错误 0x" + error.ToString("X") + "）。");
                return null;
            }
            finally
            {
                if (title != IntPtr.Zero) Marshal.FreeHGlobal(title);
                if (filter != IntPtr.Zero) Marshal.FreeHGlobal(filter);
                if (file != IntPtr.Zero) Marshal.FreeHGlobal(file);
            }
        }
    }
}
