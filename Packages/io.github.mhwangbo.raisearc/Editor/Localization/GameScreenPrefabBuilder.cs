using System;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEngine;

namespace PrincessStudio.Editor.Localization
{
    public static class GameScreenPrefabBuilder
    {
        public static void Export(GameProjectAsset project, PresentationSkin skin, string path)
        {
            if (project == null || skin == null) throw new ArgumentException("Select a project and skin.");
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || path.Contains(".."))
                throw new ArgumentException("Choose a prefab path inside Assets.");
            var issues = skin.ValidateBindings(project.Read());
            if (issues.Count > 0) throw new ArgumentException(string.Join("\n", issues));
            LocalizationTableBridge.Sync(project);
            var root = PrefabUtility.LoadPrefabContents(PresentationSampleBuilder.Root + "/GameScreen.prefab");
            try
            {
                var type = Type.GetType("PrincessStudio.Samples.SampleGameController, PrincessStudio.Samples") ?? throw new InvalidOperationException("Sample screen runtime unavailable.");
                var component = root.GetComponent(type) ?? throw new InvalidOperationException("Screen template has no controller.");
                var serialized = new SerializedObject(component);
                serialized.FindProperty("projectAsset").objectReferenceValue = project;
                serialized.FindProperty("presentationSkin").objectReferenceValue = skin;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new InvalidOperationException("Could not save screen prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }
    }
}
