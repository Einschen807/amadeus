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
        PlayerSettings.productName = "Kurisu Companion";
        PlayerSettings.defaultScreenWidth = 620;
        PlayerSettings.defaultScreenHeight = 820;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

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
        var root = GameObject.Find("Kurisu Companion");
        if (root == null) root = new GameObject("Kurisu Companion");
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
        Configure();
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../KurisuCompanion_Windows/KurisuCompanion.exe"));
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
    }
}
