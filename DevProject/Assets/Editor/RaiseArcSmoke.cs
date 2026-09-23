using System;
using System.IO;
using PrincessStudio.Unity;
using RaiseArc.Editor;
using RaiseArc.Unity;
using UnityEditor;

namespace RaiseArc.DevProject
{
    public static class RaiseArcSmoke
    {
        public static void CreateComposerScreen()
        {
            var asset = GameCreation.Create(new NewGameDefinition { title = "Composer smoke" });
            var path = BasicGameSetup.CreateConfigured(asset, null, null, new GameScreenDefinition());
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new InvalidOperationException("Composer scene was not created.");
            var folder = Path.GetDirectoryName(path);
            foreach (var name in new[] { "GameScreen.uxml", "GameScreen.uss", "ScreenSkin.asset", "Panel.asset" })
                if (!File.Exists(Path.Combine(folder, name)))
                    throw new InvalidOperationException("Missing Composer output: " + name);
            AssetDatabase.SaveAssets();
        }
    }
}
