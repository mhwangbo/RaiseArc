using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PrincessStudio.Editor
{
    public static class PackageRelease
    {
        public const string CandidateVersion = "0.3.0-preview.2-nativeui.2";
        [MenuItem("Window/RaiseArc/Export distribution package")]
        public static void Export()
        {
            var path = EditorUtility.SaveFilePanel("Export RaiseArc", "", "RaiseArc-" + CandidateVersion + ".unitypackage", "unitypackage");
            if (path.Length > 0)
                AssetDatabase.ExportPackage("Assets/PrincessStudio", path, ExportPackageOptions.Recurse);
        }
        public static void BuildSampleAndExit()
        {
            try
            {
                Directory.CreateDirectory("Builds/Windows");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/PrincessStudio/Samples/Scenes/LifeSimulation.unity", "Assets/PrincessStudio/Samples/Scenes/ForestModule.unity" },
                    locationPathName = "Builds/Windows/RaiseArcDemo.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });
                Debug.Log("RAISEARC_BUILD " + report.summary.result + " bytes=" + report.summary.totalSize + " errors=" + report.summary.totalErrors);
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void ExportAndExit()
        {
            Directory.CreateDirectory("Builds/Distribution");
            AssetDatabase.ExportPackage("Assets/PrincessStudio", "Builds/Distribution/RaiseArc-" + CandidateVersion + ".unitypackage", ExportPackageOptions.Recurse);
            Debug.Log("RAISEARC_EXPORT_READY");
            EditorApplication.Exit(0);
        }
        public static void OpenReview()
        {
            EditorApplication.delayCall += () =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<PrincessStudio.Unity.GameProjectAsset>("Assets/PrincessStudio/Samples/Scenes/SampleGame.asset");
                var window = StudioWindow.OpenProject(asset);
                window.position = new Rect(120, 80, 1250, 840);
            };
        }
    }
}
