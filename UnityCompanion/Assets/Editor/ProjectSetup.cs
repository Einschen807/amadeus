using System;
using System.IO;
using KurisuCompanion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class ProjectSetup
{
    public static void Configure()
    {
        PlayerSettings.companyName = "AmadeusPrototype";
        PlayerSettings.productName = "Amadeus";
        PlayerSettings.defaultScreenWidth = 620;
        PlayerSettings.defaultScreenHeight = 923;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = false;
        PlayerSettings.runInBackground = true;
        // 陪伴小窗依赖 DWM 按像素 alpha 合成：需要 D3D11，并关闭 DXGI flip model 交换链。
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
        PlayerSettings.useFlipModelSwapchain = false;

        Directory.CreateDirectory("Assets/UI");
        Directory.CreateDirectory("Assets/Scenes");
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/CompanionPanel.asset");
        if (panel == null)
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, "Assets/UI/CompanionPanel.asset");
        }
        var scene = File.Exists("Assets/Scenes/Companion.unity")
            ? EditorSceneManager.OpenScene("Assets/Scenes/Companion.unity", OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = GameObject.Find("Amadeus") ?? GameObject.Find("Kurisu Companion");
        if (root == null) root = new GameObject("Amadeus");
        root.name = "Amadeus";
        var doc = root.GetComponent<UIDocument>();
        if (doc == null) doc = root.AddComponent<UIDocument>();
        doc.panelSettings = panel;
        if (root.GetComponent<KurisuApp>() == null) root.AddComponent<KurisuApp>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Companion.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Companion.unity", true) };
        AssetDatabase.SaveAssets();
        Debug.Log("KURISU_PROJECT_SETUP_OK");
    }

    public static void BuildWindows()
    {
        BuildDetachedInput();
        BuildDesktopBubble();
        Configure();
        bool preview = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--preview-build");
        var output = Path.GetFullPath(Path.Combine(Application.dataPath,
            preview ? "../../Amadeus_Preview/Amadeus.exe" : "../../Amadeus_Windows/Amadeus.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Companion.unity" },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        Debug.Log("KURISU_BUILD_RESULT=" + report.summary.result + " OUTPUT=" + output);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception("Windows build failed: " + report.summary.result);
        PackageLocalEmote(output);
    }

    private static void PackageLocalEmote(string executable)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        string source = Path.Combine(root, "EmoteTest");
        string model = Path.Combine(source, "cache", "kurisu0-crossed.psb");
        string wrapper = Path.Combine(source, "FreeMote.NET.dll");
        string driver = Path.Combine(source, "emotedriver.dll");
        string bridgeSource = Path.Combine(source, "BridgeHost.cs");
        string bridge = Path.Combine(source, "EmoteBridge.exe");
        // 有桥接源码就重新编译；没有源码但已有编译好的 EmoteBridge.exe 时直接复用。
        if (!File.Exists(model) || !File.Exists(wrapper) || !File.Exists(driver) ||
            (!File.Exists(bridgeSource) && !File.Exists(bridge)))
        {
            Debug.LogWarning("EMOTE_PACKAGE_SKIPPED: local compatible model or driver is missing; static portrait remains available.");
            return;
        }
        if (File.Exists(bridgeSource)) CompileBridge(bridgeSource, bridge, wrapper);
        else Debug.Log("EMOTE_BRIDGE_PREBUILT " + bridge);
        string target = Path.Combine(Path.GetDirectoryName(executable), "Emote");
        Directory.CreateDirectory(target);
        File.Copy(model, Path.Combine(target, "kurisu.psb"), true);
        File.Copy(wrapper, Path.Combine(target, "FreeMote.NET.dll"), true);
        File.Copy(driver, Path.Combine(target, "emotedriver.dll"), true);
        File.Copy(bridge, Path.Combine(target, "EmoteBridge.exe"), true);
        Debug.Log("EMOTE_PACKAGE_OK " + target);
    }

    private static void CompileBridge(string bridgeSource, string bridge, string wrapper)
    {
        string framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET/Framework/v4.0.30319");
        string args = "/nologo /target:winexe /platform:x64 /optimize+ " +
            "/out:\"" + bridge + "\" /r:\"" + wrapper + "\" " +
            "/r:\"" + Path.Combine(framework, "System.Xaml.dll") + "\" " +
            "/r:\"" + Path.Combine(framework, "WPF/WindowsBase.dll") + "\" " +
            "/r:\"" + Path.Combine(framework, "WPF/PresentationCore.dll") + "\" " +
            "/r:\"" + Path.Combine(framework, "WPF/PresentationFramework.dll") + "\" " +
            "\"" + bridgeSource + "\"";
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(framework, "csc.exe"), args)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = System.Diagnostics.Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception("E-mote bridge build failed: " + output);
        }
    }

    private static void BuildDetachedInput()
    {
        string folder = Path.Combine(Application.dataPath, "StreamingAssets/DesktopInput");
        string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET/Framework64/v4.0.30319/csc.exe");
        var start = new System.Diagnostics.ProcessStartInfo(compiler,
            "/nologo /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /out:\"" +
            Path.Combine(folder, "DetachedInput.exe") + "\" \"" + Path.Combine(folder, "DetachedInput.cs.txt") + "\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = System.Diagnostics.Process.Start(start)) {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception("Detached input build failed: " + output);
        }
        AssetDatabase.Refresh();
    }

    private static void BuildDesktopBubble()
    {
        string folder = Path.Combine(Application.dataPath, "StreamingAssets/DesktopBubble");
        string framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET/Framework64/v4.0.30319");
        string args = "/nologo /target:winexe /platform:x64 /optimize+ " +
            "/r:\"" + Path.Combine(framework, "System.Web.Extensions.dll") + "\" " +
            "/r:\"" + Path.Combine(framework, "System.Windows.Forms.dll") + "\" " +
            "/r:\"" + Path.Combine(framework, "System.Xaml.dll") + "\" " +
            "/r:\"" + Path.Combine(framework, "WPF/WindowsBase.dll") + "\" " +
            "/r:\"" + Path.Combine(framework, "WPF/PresentationCore.dll") + "\" " +
            "/r:\"" + Path.Combine(framework, "WPF/PresentationFramework.dll") + "\" " +
            "/out:\"" + Path.Combine(folder, "DesktopBubble.exe") + "\" \"" +
            Path.Combine(folder, "DesktopBubble.cs.txt") + "\"";
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(framework, "csc.exe"), args)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = System.Diagnostics.Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception("Desktop bubble build failed: " + output);
        }
        AssetDatabase.Refresh();
    }
}
