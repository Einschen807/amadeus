using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace KurisuCompanion
{
    internal static class KeyVault
    {
        [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Length; public IntPtr Data; }
        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
        [DllImport("Kernel32.dll", SetLastError = true)] private static extern IntPtr LocalFree(IntPtr memory);

        private static string Pathname => Path.Combine(Application.persistentDataPath, "deepseek-key.bin");

        public static string Read()
        {
            if (!File.Exists(Pathname)) return "";
            try { return System.Text.Encoding.UTF8.GetString(Transform(File.ReadAllBytes(Pathname), false)); }
            catch { return ""; }
        }

        public static void Save(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) { if (File.Exists(Pathname)) File.Delete(Pathname); return; }
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllBytes(Pathname, Transform(System.Text.Encoding.UTF8.GetBytes(key.Trim()), true));
        }

        private static byte[] Transform(byte[] data, bool protect)
        {
            var input = new DataBlob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
            DataBlob output = default;
            try
            {
                Marshal.Copy(data, 0, input.Data, data.Length);
                bool ok = protect
                    ? CryptProtectData(ref input, "Amadeus API Key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output);
                if (!ok) throw new InvalidOperationException("Windows 无法加密或解密此用户的 API Key。");
                var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }
    }
}
