using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using KurisuCompanion;
using UnityEngine;

public static class PickerChecks
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static void Run()
    {
        bool opened = false;
        var closer = new Thread(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                IntPtr dialog = FindWindow(null, "选择图片或游戏截图");
                if (dialog != IntPtr.Zero)
                {
                    opened = true;
                    PostMessage(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Thread.Sleep(100);
            }
        });
        closer.IsBackground = true;
        closer.Start();
        try
        {
            Type picker = typeof(KurisuApp).Assembly.GetType("KurisuCompanion.NativeImagePicker", true);
            MethodInfo pick = picker.GetMethod("Pick", BindingFlags.Public | BindingFlags.Static);
            pick.Invoke(null, null);
            closer.Join(1000);
            if (!opened) throw new Exception("Native picker window did not open");
            Debug.Log("KURISU_PICKER_CHECK_OK");
        }
        catch (TargetInvocationException e)
        {
            throw e.InnerException ?? e;
        }
    }
}
