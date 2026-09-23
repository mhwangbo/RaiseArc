using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor.Tests
{
    public sealed class BalanceWindowTests
    {
        [Test] public void ReplayBelongsToSelectedPathAndComparisonReopensWithoutLiveJobs()
        {
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(EndingTestExample.Definition());
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/BalanceReplayFixture.asset"); AssetDatabase.CreateAsset(asset, path);
            var definition = BalanceTests.New(asset); definition.policies[0].runs = 2;
            var saved = BalanceTests.Save(asset, definition, null, 0);
            var alternate = ScriptableObject.CreateInstance<GameProjectAsset>(); var alternateData = asset.Read(); alternateData.activities[0].cost += 1; alternate.Write(alternateData);
            var window = ScriptableObject.CreateInstance<SimulationWindow>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic; var jobs = new List<BalanceJob>(); var artifacts = new HashSet<string>();
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".comparison.json");
            void Set(string name, object value) => typeof(SimulationWindow).GetField(name, flags).SetValue(window, value);
            T Get<T>(string name) => (T)typeof(SimulationWindow).GetField(name, flags).GetValue(window);
            void Click(string en, string ko) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(window.rootVisualElement.Query<Button>().ToList().Single(b => b.text == en || b.text == ko).clickable, new object[] { null });
            string Labels() => string.Join("\n", window.rootVisualElement.Query<Label>().ToList().Select(l => l.text));
            void Run()
            {
                Click("Run saved test", "저장된 테스트 실행"); var job = BalanceTests.Find(Get<string>("balanceJobId")); jobs.Add(job);
                for (var i = 0; i < 1000 && job.Data.status == ExplorationStatus.Running; i++) BalanceTests.Tick(job, 128, 100);
                Assert.That(job.Data.status, Is.EqualTo(ExplorationStatus.Completed));
                typeof(SimulationWindow).GetMethod("RenderBalance", flags).Invoke(window, null);
            }
            void Inspect()
            {
                var button = window.rootVisualElement.Query<Button>().ToList().First(b => b.text.Contains("inspect run") || b.text.Contains(" 보기"));
                typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
                artifacts.Add(Get<string>("balanceResultId"));
            }
            try
            {
                Set("asset", asset); Set("savedBalance", saved); Set("balanceMode", true); window.Show(); window.CreateGUI();
                Run(); Inspect(); var r0 = Get<string>("balanceResultId");
                Get<TextField>("balanceResultField").value = Guid.NewGuid().ToString("N");
                Click("Verify replay original candidate", "원래 후보 입력 재현 확인");
                Assert.That(Get<string>("balanceReplayText"), Does.Contain("True"));
                Assert.That(Labels(), Does.Contain("result=" + r0).And.Contain("job=" + jobs[0].Data.jobId).And.Contain("content="));
                Assert.That(Get<Label>("balanceMessage").text, Does.Not.Contain("True"), "Replay verdicts never live in the unowned message bar.");
                var completedSelection = Get<int>("balanceReplaySelection");
                var previousButton = window.rootVisualElement.Query<Button>().ToList().Single(b => b.text == "Verify replay original candidate" || b.text == "원래 후보 입력 재현 확인");
                typeof(SimulationWindow).GetMethod("SelectBalanceRun", flags).Invoke(window, new object[] { jobs[0], 0, 1 });
                Assert.That(Get<int>("balanceRun"), Is.EqualTo(1));
                typeof(SimulationWindow).GetMethod("CompleteBalanceReplay", flags).Invoke(window, new object[] { completedSelection, "Late R0/run0 success" });
                typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(previousButton.clickable, new object[] { null });
                Assert.That(Get<string>("balanceReplayText"), Is.Empty, "An older run's queued callback or completion must not label this run.");
                Click("Verify replay original candidate", "원래 후보 입력 재현 확인"); Assert.That(Get<string>("balanceReplayText"), Does.Contain("True"));
                completedSelection = Get<int>("balanceReplaySelection");
                var candidateField = window.rootVisualElement.Query<ObjectField>().ToList().Single(f => f.label == "Candidate (empty = test project)" || f.label == "실행 후보 (비우면 테스트 프로젝트)");
                candidateField.value = alternate;
                typeof(SimulationWindow).GetMethod("CompleteBalanceReplay", flags).Invoke(window, new object[] { completedSelection, "Late candidate success" });
                Assert.That(Get<string>("balanceReplayText"), Is.Empty);
                candidateField.value = null;
                completedSelection = Get<int>("balanceReplaySelection");
                Run();
                typeof(SimulationWindow).GetMethod("CompleteBalanceReplay", flags).Invoke(window, new object[] { completedSelection, "Late previous-result success" });
                Assert.That(Get<string>("balanceReplayText"), Is.Empty);
                Assert.That(Get<object>("balancePath"), Is.Null);
                Inspect(); var r1 = Get<string>("balanceResultId");
                Assert.That(Labels(), Does.Contain("Not replayed for this selection.").Or.Contain("현재 선택 경로는 아직 재현하지 않았습니다."));
                Assert.That(Labels(), Does.Not.Contain("result=" + r0));
                Get<TextField>("balanceResultField").value = r0; Click("Reopen checkpoint", "체크포인트 재오픈"); jobs.Add(BalanceTests.Find(Get<string>("balanceJobId")));
                Assert.That(Get<object>("balancePath"), Is.Null); Assert.That(Get<string>("balanceReplayText"), Is.Empty);
                var comparison = BalanceTests.Compare(r0, r1); BalanceComparisonFile.Export(file, comparison);
                foreach (var job in jobs) BalanceTests.Forget(job); jobs.Clear();
                window.Close(); window = ScriptableObject.CreateInstance<SimulationWindow>(); Set("balanceMode", true); window.Show(); window.CreateGUI();
                typeof(SimulationWindow).GetMethod("OpenBalanceComparison", flags).Invoke(window, new object[] { file });
                Assert.That(Labels(), Does.Contain("R0: " + r0).And.Contain("R1: " + r1));
                Assert.That(Get<object>("balancePath"), Is.Null);
                Assert.That(window.rootVisualElement.Query<Button>().ToList().Any(b => b.text == "Export comparison…" || b.text == "비교 내보내기…"), Is.True);
            }
            finally
            {
                window.Close(); foreach (var job in jobs) { BalanceTests.Cancel(job); BalanceTests.Forget(job); }
                foreach (var id in artifacts) File.Delete(BalanceTests.ArtifactPath(id)); File.Delete(file);
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(saved)); AssetDatabase.DeleteAsset(path); UnityEngine.Object.DestroyImmediate(alternate);
            }
        }
        [Test] public void ReopenedBalanceFieldsSaveThroughSharedApiAndProjectSwitchClearsIdentity()
        {
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(EndingTestExample.Definition());
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/BalanceWindowFixture.asset"); AssetDatabase.CreateAsset(asset, path);
            var d = BalanceTests.New(asset); var saved = BalanceTests.Save(asset, d, null, 0);
            var window = ScriptableObject.CreateInstance<SimulationWindow>(); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var second = ScriptableObject.CreateInstance<GameProjectAsset>(); second.Write(EndingTestExample.Definition());
            try
            {
                typeof(SimulationWindow).GetField("asset", flags).SetValue(window, asset);
                typeof(SimulationWindow).GetField("savedBalance", flags).SetValue(window, saved);
                typeof(SimulationWindow).GetField("balanceMode", flags).SetValue(window, true); window.Show(); window.CreateGUI();
                var root = window.rootVisualElement;
                var money = root.Query<IntegerField>().ToList().Single(f => f.label == "Starting money" || f.label == "시작 돈"); money.value = 3;
                var toggle = root.Query<Toggle>().ToList().Single(t => t.label == "Override starting money (analysis only)" || t.label == "분석에서만 시작 돈 재정의"); toggle.value = true;
                root.Query<Toggle>().ToList().Single(t => t.label == "dates").value = false;
                var save = root.Query<Button>().ToList().Single(b => b.text == "Save balance test" || b.text == "밸런스 테스트 저장");
                typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(save.clickable, new object[] { null });
                Assert.That(saved.Read().money, Is.EqualTo(3)); Assert.That(saved.Read().overrideMoney, Is.True);
                Assert.That(saved.Read().metrics.Contains("dates"), Is.False);
                Assert.That(saved.Read().policies[0].runs, Is.EqualTo(d.policies[0].runs), "Report sections do not filter runs.");
                var read = JsonUtility.FromJson<BalanceResponse>(BalanceCommands.Execute(asset, "{\"operation\":\"ReadBalanceTest\",\"testId\":\"" + d.id + "\"}"));
                Assert.That(read.definition.money, Is.EqualTo(3)); Assert.That(asset.Read().startingMoney, Is.EqualTo(2));
                root.Q<ObjectField>().value = second;
                Assert.That(typeof(SimulationWindow).GetField("savedBalance", flags).GetValue(window), Is.Null);
                Assert.That(typeof(SimulationWindow).GetField("balanceDraft", flags).GetValue(window), Is.Null);
            }
            finally { window.Close(); AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(saved)); AssetDatabase.DeleteAsset(path); UnityEngine.Object.DestroyImmediate(second); }
        }
    }
}
