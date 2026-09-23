using System.IO;
using System.Reflection;
using NUnit.Framework;
using PrincessStudio.Editor;
using PrincessStudio.Editor.Graph;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using RaiseArc.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor.Tests
{
    public sealed class SimulationWindowTests
    {
        [Test] public void SelectingAnotherProjectClearsResultsFromThePreviousProject()
        {
            var first = ScriptableObject.CreateInstance<GameProjectAsset>(); first.Write(EndingTestExample.Definition());
            var second = ScriptableObject.CreateInstance<GameProjectAsset>(); second.Write(EndingTestExample.Definition());
            var window = ScriptableObject.CreateInstance<SimulationWindow>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(SimulationWindow).GetField("asset", flags).SetValue(window, first);
                window.Show(); window.CreateGUI();
                foreach (var field in new[] { "jobId", "resultId", "endingReplayJobId" })
                    typeof(SimulationWindow).GetField(field, flags).SetValue(window, "previous-result");
                window.rootVisualElement.Q<UnityEditor.UIElements.ObjectField>().value = second;
                Assert.That(typeof(SimulationWindow).GetField("asset", flags).GetValue(window), Is.SameAs(second));
                Assert.That(typeof(SimulationWindow).GetField("jobId", flags).GetValue(window), Is.EqualTo(""));
                Assert.That(typeof(SimulationWindow).GetField("resultId", flags).GetValue(window), Is.EqualTo(""));
                Assert.That(typeof(SimulationWindow).GetField("endingReplayJobId", flags).GetValue(window), Is.Null);
            }
            finally { window.Close(); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }
        [Test] public void EnglishAndKoreanUseTheSameResultAndNavigateToItsChoice()
        {
            var previousLanguage = StudioText.Language;
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>();
            asset.Write(new UnityProjectCodec().FromJson(File.ReadAllText("Assets/PrincessStudio/Samples/Data/SimulationExample.json")));
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/SimulationWindowTest.asset"); AssetDatabase.CreateAsset(asset, path);
            var window = ScriptableObject.CreateInstance<SimulationWindow>();
            window.Show();
            var job = SimulationJobs.Start(asset, new ExplorationSettings { mode = ExplorationMode.TargetSearch, targetId = "accept" });
            try
            {
                for (var i = 0; i < 1000 && job.Explorer.Report.status == ExplorationStatus.Running; i++) job.Explorer.Tick(16, 100);
                typeof(SimulationWindow).GetField("asset", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, asset);
                typeof(SimulationWindow).GetField("jobId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, job.Id);
                StudioText.Language = "en"; window.CreateGUI();
                Assert.That(window.rootVisualElement.Query<Label>().ToList().Exists(l => l.text.Contains("Simulation Explorer")), Is.True);
                var result = job.Explorer.Report.targetRecord;
                StudioText.Language = "ko"; window.CreateGUI();
                Assert.That(window.rootVisualElement.Query<Label>().ToList().Exists(l => l.text.Contains("시뮬레이션 탐색기")), Is.True);
                Assert.That(job.Explorer.Report.targetRecord, Is.EqualTo(result));
                SimulationJobs.Navigate(job, "accept", job.Explorer.Records[result]);
                var graph = EditorWindow.GetWindow<GraphWorkbenchWindow>();
                Assert.That(typeof(GraphWorkbenchWindow).GetField("selectedId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(graph), Is.EqualTo("accept"));
                graph.Close();
            }
            finally { job.Explorer.Cancel(); SimulationJobs.Forget(job.Id); window.Close(); AssetDatabase.DeleteAsset(path); StudioText.Language = previousLanguage; }
        }
    }
}
