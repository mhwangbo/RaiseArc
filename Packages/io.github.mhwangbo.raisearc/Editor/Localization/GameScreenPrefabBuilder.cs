using System;
using System.IO;
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
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                throw new IOException("An asset already exists at the export path.");
            var issues = skin.ValidateBindings(project.Read());
            if (issues.Count > 0) throw new ArgumentException(string.Join("\n", issues));
            var templatePath = RaiseArc.Editor.BasicGameSetup.Folder(project) + "/GameScreen.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(templatePath) == null)
                throw new InvalidOperationException("Create a basic game screen for this project before exporting its prefab.");
            LocalizationTableBridge.Sync(project);
            var root = PrefabUtility.LoadPrefabContents(templatePath);
            try
            {
                var type = Type.GetType("PrincessStudio.Samples.SampleGameController, PrincessStudio.Samples") ?? throw new InvalidOperationException("RaiseArc screen runtime unavailable.");
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
