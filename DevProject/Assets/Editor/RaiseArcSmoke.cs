using System;
using System.IO;
using System.Reflection;
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
        public static void RunFocusedRegressions()
        {
            foreach (var test in new[]
            {
                ("PrincessStudio.Editor.Graph.Tests.PlaythroughSharedFlowTests", "RandomizedPlaythroughAdvancesExpandedSharedFlow"),
                ("PrincessStudio.Editor.Graph.Tests.AuthoringPipeTargetTests", "SwitchingStudioProjectStopsTheOldMcpTarget")
            })
            {
                var type = Type.GetType(test.Item1 + ", PrincessStudio.GraphTests")
                    ?? throw new InvalidOperationException("Test assembly is unavailable: " + test.Item1);
                try { type.GetMethod(test.Item2).Invoke(Activator.CreateInstance(type), null); }
                catch (TargetInvocationException error) { throw error.InnerException ?? error; }
                UnityEngine.Debug.Log("RAISEARC_REGRESSION_PASS " + test.Item2);
            }
        }
        public static void RunMcpTargetRegression()
        {
            var type = Type.GetType("PrincessStudio.Editor.Graph.Tests.AuthoringPipeTargetTests, PrincessStudio.GraphTests")
                ?? throw new InvalidOperationException("MCP test assembly is unavailable.");
            try { type.GetMethod("SwitchingStudioProjectStopsTheOldMcpTarget").Invoke(Activator.CreateInstance(type), null); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
            UnityEngine.Debug.Log("RAISEARC_REGRESSION_PASS SwitchingStudioProjectStopsTheOldMcpTarget");
        }
        public static void RunDraftRegression()
        {
            foreach (var test in new[]
            {
                ("RaiseArc.Editor.Tests.TimePlanTests", "UnsavedTimeRulesSurviveUndoRefreshAndRejectProjectSwitch"),
                ("RaiseArc.Editor.Tests.ScreenCompositionTests", "UnsavedLayoutSurvivesReloadAndRejectsSettingsSwitch")
            })
            {
                var type = Type.GetType(test.Item1 + ", PrincessStudio.GraphTests")
                    ?? throw new InvalidOperationException("Test assembly is unavailable: " + test.Item1);
                try { type.GetMethod(test.Item2).Invoke(Activator.CreateInstance(type), null); }
                catch (TargetInvocationException error) { throw error.InnerException ?? error; }
                UnityEngine.Debug.Log("RAISEARC_REGRESSION_PASS " + test.Item2);
            }
        }
    }
}
