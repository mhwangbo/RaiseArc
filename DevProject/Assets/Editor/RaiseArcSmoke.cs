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
                ("RaiseArc.Editor.Tests.ScreenCompositionTests", "UnsavedLayoutSurvivesReloadAndRejectsSettingsSwitch"),
                ("RaiseArc.Editor.Tests.ScreenCompositionTests", "BasicSetupPreservesUnsavedScreenAcrossUndoRefreshAndProjectSwitch")
            })
            {
                var type = Type.GetType(test.Item1 + ", PrincessStudio.GraphTests")
                    ?? throw new InvalidOperationException("Test assembly is unavailable: " + test.Item1);
                try { type.GetMethod(test.Item2).Invoke(Activator.CreateInstance(type), null); }
                catch (TargetInvocationException error) { throw error.InnerException ?? error; }
                UnityEngine.Debug.Log("RAISEARC_REGRESSION_PASS " + test.Item2);
            }
        }
        public static void RunGraphFixtureRegression()
        {
            var project = RaiseArc.Editor.Graph.GraphExampleDefinition.Create();
            var report = new PrincessStudio.Core.AuthoringService(project, new UnityProjectCodec()).ValidateProject();
            if (report.HasErrors) throw new InvalidOperationException(report.Summary);
            var session = new PrincessStudio.Core.GameSession(project);
            session.PerformActivity("study");
            if (session.State.PendingEventId != "invitation") throw new InvalidOperationException("Invitation did not trigger.");
            session.AdvancePresentation("invitation.line1");
            session.AdvancePresentation("invitation.line2");
            session.Choose("accept");
            if (session.State.Flags["festival"] != 1) throw new InvalidOperationException("Invitation choice did not commit its flag.");
            var type = Type.GetType("PrincessStudio.Editor.Graph.Tests.KoreanWorkbenchTests, PrincessStudio.GraphTests")
                ?? throw new InvalidOperationException("Graph test assembly is unavailable.");
            var method = type.GetMethod("ShowcaseBranchesCompleteWithScholarship");
            if (method == null || method.ReturnType != typeof(void)) throw new InvalidOperationException("Expected a synchronous NUnit Test method.");
            try { method.Invoke(Activator.CreateInstance(type), null); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
            UnityEngine.Debug.Log("RAISEARC_DIRECT_CHECK_PASS GraphFixtureAndShowcaseBranches");
        }
    }
}
