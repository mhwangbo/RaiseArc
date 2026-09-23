using System.IO;
using System.Linq;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public static class SampleSceneBuilder
    {
        [MenuItem("Window/RaiseArc/Create playable sample scenes")]
        public static void Create()
        {
            // Use serialized type names to keep the distribution's editor independent of optional sample assemblies.
            var gameType = System.Type.GetType("PrincessStudio.Samples.SampleGameController, PrincessStudio.Samples");
            var forestType = System.Type.GetType("PrincessStudio.Samples.ForestSampleEndpoint, PrincessStudio.Samples");
            if (gameType == null || forestType == null)
                throw new System.InvalidOperationException("Sample runtime assembly is not loaded.");
            const string root = "Assets/PrincessStudio/Samples/Scenes";
            Directory.CreateDirectory(root);
            AssetDatabase.Refresh();
            var project = AssetDatabase.LoadAssetAtPath<GameProjectAsset>(root + "/SampleGame.asset");
            if (project == null)
            {
                project = ScriptableObject.CreateInstance<GameProjectAsset>();
                project.Write(new UnityProjectCodec().FromJson(File.ReadAllText("Assets/PrincessStudio/Samples/Data/SampleProject.json")));
                AssetDatabase.CreateAsset(project, root + "/SampleGame.asset");
            }
            var sampleSource = new UnityProjectCodec().FromJson(File.ReadAllText("Assets/PrincessStudio/Samples/Data/SampleProject.json"));
            var existingContent = project.Read();
            foreach (var entry in sampleSource.translations)
                if (!existingContent.translations.Exists(t => t.key == entry.key && t.locale == entry.locale))
                    existingContent.translations.Add(entry);
            project.Write(existingContent);
            EditorUtility.SetDirty(project);
            var fontGuid = AssetDatabase.AssetPathToGUID("Assets/PrincessStudio/Samples/Fonts/NotoSansKR.ttf");
            if (!string.IsNullOrEmpty(fontGuid))
            {
                var content = project.Read();
                foreach (var code in content.locales)
                {
                    content.localizedAssets.RemoveAll(a => a.key == "ui.font" && a.locale == code);
                    content.localizedAssets.Add(new PrincessStudio.Core.LocalizedAssetEntry { key = "ui.font", locale = code, assetGuid = fontGuid });
                }
                project.Write(content);
                EditorUtility.SetDirty(project);
            }
            LocalizationTableBridge.Sync(project);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(root + "/SamplePanel.asset");
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1280, 800);
                AssetDatabase.CreateAsset(panel, root + "/SamplePanel.asset");
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(root + "/SampleTheme.tss");
            if (theme == null)
            {
                File.WriteAllText(root + "/SampleTheme.tss", "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(root + "/SampleTheme.tss");
                theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(root + "/SampleTheme.tss");
            }
            panel.themeStyleSheet = theme;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            Build(root + "/LifeSimulation.unity", gameType, root + "/SampleGame.asset", root + "/SamplePanel.asset");
            Build(root + "/ForestModule.unity", forestType, root + "/SampleGame.asset", root + "/SamplePanel.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("RaiseArc sample scenes created. Add both scenes to Build Settings before runtime scene transitions.");
        }
        private static void Build(string path, System.Type type, string projectPath, string panelPath)
        {
            var active = SceneManager.GetActiveScene();
            if (!Application.isBatchMode && string.IsNullOrEmpty(active.path))
                throw new System.InvalidOperationException("Save the current untitled scene before generating samples.");
            var scene = File.Exists(path) ? EditorSceneManager.OpenScene(path, Application.isBatchMode ? OpenSceneMode.Single : OpenSceneMode.Additive) : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                var controller = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren(type, true)).FirstOrDefault();
                if (controller == null)
                {
                    var go = new GameObject("RaiseArc");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    controller = go.AddComponent(type);
                }
                var doc = controller.GetComponent<UIDocument>();
                if (doc == null)
                    doc = controller.gameObject.AddComponent<UIDocument>();
                if (doc.panelSettings == null)
                    doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
                doc.sortingOrder = type.Name.Contains("Forest") ? 10 : 0;
                var serialized = new SerializedObject(controller);
                var reference = serialized.FindProperty("projectAsset");
                if (reference.objectReferenceValue == null)
                    reference.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameProjectAsset>(projectPath);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (reference.objectReferenceValue == null)
                    throw new System.InvalidOperationException("Sample project reference could not be assigned: " + projectPath);
                EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(doc);
                EditorSceneManager.SaveScene(scene, path);
            }
            finally { if (!Application.isBatchMode) { EditorSceneManager.CloseScene(scene, true); if (active.IsValid()) SceneManager.SetActiveScene(active); } }
        }
        public static void PrepareAndExit()
        {
            try
            {
                Create();
                Debug.Log("RAISEARC_SAMPLE_READY");
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void PrepareTestsAndExit()
        {
            try
            {
                Create();
                var scenes = EditorBuildSettings.scenes.ToList();
                foreach (var path in new[] { "Assets/PrincessStudio/Samples/Scenes/LifeSimulation.unity", "Assets/PrincessStudio/Samples/Scenes/ForestModule.unity" })
                {
                    var existing = scenes.Find(s => s.path == path);
                    if (existing == null)
                        scenes.Add(new EditorBuildSettingsScene(path, true));
                    else
                        existing.enabled = true;
                }
                EditorBuildSettings.scenes = scenes.ToArray();
                UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.BuildPlayerContent(out var result);
                if (!string.IsNullOrEmpty(result.Error))
                    throw new System.InvalidOperationException(result.Error);
                Debug.Log("RAISEARC_TEST_SCENES_READY");
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
