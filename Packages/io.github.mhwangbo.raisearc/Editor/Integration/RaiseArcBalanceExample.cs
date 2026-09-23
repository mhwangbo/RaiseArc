using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using RaiseArc.Core;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    public static class BalanceExample
    {
        public static ProjectDefinition Definition(int days = 45)
        {
            var p = RulesExamples.Definition("fitness"); p.id = "balance-academy"; p.durationDays = days; p.startingMoney = 6;
            p.stats[0].maximum = 300;
            var train = p.activities.Find(a => a.id == "train"); train.cost = 2;
            train.effects[0].value = 4; train.effects[0].randomRange = true; train.effects[0].minimumValue = 3; train.effects[0].maximumValue = 5;
            p.activities.Add(new ActivityDefinition { id = "work", nameKey = "work", income = 6 });
            p.activities.Add(new ActivityDefinition { id = "lottery", nameKey = "lottery", income = 100, successChance = new SuccessChance { enabled = true, basePercent = 10 } });
            p.activities.Add(new ActivityDefinition { id = "trial", nameKey = "trial", evaluation = new EvaluationDefinition { enabled = true, statId = "score", passingScore = 12, qualificationFlagId = "qualified" } });
            p.events.Add(new EventDefinition { id = "mentor", nameKey = "mentor", once = true,
                conditions = new List<ConditionSpec> { new ConditionSpec { kind = ValueKind.Day, value = 7 } },
                choices = new List<ChoiceDefinition> {
                    new ChoiceDefinition { id = "mentor-train", nameKey = "mentor-train", effects = new List<EffectSpec> { new EffectSpec { kind = ValueKind.Stat, target = "score", value = 3 } } },
                    new ChoiceDefinition { id = "mentor-rest", nameKey = "mentor-rest", effects = new List<EffectSpec> { new EffectSpec { kind = ValueKind.ModifierDays, target = "fatigue", operation = EffectOperation.Set, value = 0 } } }
                } });
            var goal = p.endings.Find(e => e.id == "goal"); goal.conditions = new List<ConditionSpec> {
                new ConditionSpec { kind = ValueKind.Stat, target = "score", value = days }, new ConditionSpec { kind = ValueKind.Flag, target = "qualified", value = 1 } };
            var labels = new Dictionary<string, string[]> {
                ["work"] = new[] { "Work", "일하기" }, ["lottery"] = new[] { "Lottery", "복권" }, ["trial"] = new[] { "Skill assessment", "실력 평가" },
                ["mentor"] = new[] { "Mentor advice", "멘토의 조언" }, ["mentor-train"] = new[] { "Practice together", "함께 연습" }, ["mentor-rest"] = new[] { "Recover first", "먼저 회복" }
            };
            foreach (var pair in labels)
            {
                p.translations.RemoveAll(t => t.key == pair.Key);
                for (var i = 0; i < 2; i++) p.translations.Add(new TranslationEntry { key = pair.Key, locale = i == 0 ? "en" : "ko", text = pair.Value[i] });
            }
            new ContentIndex(p, assignIdentities: true); return p;
        }
        public static BalanceTestDefinition Test(ProjectDefinition p, int runs = 100)
        {
            return new BalanceTestDefinition { id = Guid.NewGuid().ToString("N"), name = "Academy economy / 학교 경제", projectId = p.id, overrideMoney = true, money = 3,
                settings = new ExplorationSettings { mode = ExplorationMode.MonteCarlo, horizonDays = p.durationDays, maxTransitions = 2000000, maxStates = 100000, maxMemoryMiB = 512, maxCheckpointMiB = 512, maxSeconds = 600,
                    forbiddenActivities = new List<string> { "lottery" } },
                policies = new List<BalancePolicy> {
                    new BalancePolicy { id = "random", name = "Random / 무작위", runs = runs },
                    new BalancePolicy { id = "income", name = "Income first / 수입 우선", kind = "income-first-v1", incomeActivityId = "work", incomeThreshold = 2, runs = runs,
                        weights = new List<InputWeight> { new InputWeight { id = "train", weight = 6 }, new InputWeight { id = "work", weight = .5 }, new InputWeight { id = "fatigue-start", weight = .25 }, new InputWeight { id = "trial", weight = 1 }, new InputWeight { id = "rest", weight = .5 } } }
                },
                criteria = new List<BalanceCriterion> { new BalanceCriterion { id = "goal-rate", policyId = "income", targetId = "goal", rate = .5 } }
            };
        }
        [MenuItem("Window/RaiseArc/Samples/Create Balance Test Example")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder("Assets/RaiseArcBalance")) AssetDatabase.CreateFolder("Assets", "RaiseArcBalance");
            var p = Definition(); p.id += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(p);
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath("Assets/RaiseArcBalance/Academy.asset"));
            var test = BalanceTests.Save(asset, Test(p), null, 0);
            var ending = EndingTests.New(asset, "goal"); ending.settings.maxStates = 30000; ending.settings.maxTransitions = 100000;
            var goalTest = EndingTests.Save(asset, ending, null, 0);
            TestSuites.Save(asset, new TestSuiteDefinition { id = Guid.NewGuid().ToString("N"), name = "Academy tests / 학교 테스트",
                entries = new List<TestSuiteEntry> { new TestSuiteEntry { testId = test.Read().id }, new TestSuiteEntry { kind = "ending", testId = goalTest.Read().id } } }, null, 0);
            AssetDatabase.SaveAssets(); Selection.activeObject = test; PrincessStudio.Editor.StudioWindow.OpenProject(asset); BalanceTests.Open(test);
        }
        [Serializable] private sealed class BenchmarkMeasurement
        {
            public int requested, completed, days, transitions;
            public double seconds;
            public long managedBefore, managedAfter, workingSet, estimatedRetained, artifactBytes;
            public string status, stopReason;
        }
        // Explicit batch entry point; runs only when requested, never at import/editor startup.
        public static void BenchmarkAndExit()
        {
            try
            {
                Directory.CreateDirectory("Logs/BalanceBenchmark"); var p = Definition(45); var codec = new UnityProjectCodec();
                foreach (var count in new[] { 10, 1000, 10000 })
                {
                    var d = Test(p, count); d.policies.RemoveAt(0); var policy = d.policies[0]; var before = GC.GetTotalMemory(true); var watch = Stopwatch.StartNew();
                    var e = new SimulationExplorer(p, codec, new AnalysisManifest { initial = d.CreateInitial(p, codec), settings = d.PolicySettings(policy),
                        contentFingerprint = SimulationIdentity.Hash(JsonUtility.ToJson(p)), runtimeFingerprint = BalanceTests.RuntimeFingerprint });
                    while (e.Report.status == ExplorationStatus.Running) e.Tick(256, 100);
                    watch.Stop(); var artifact = JsonUtility.ToJson(e.Report); var bytes = System.Text.Encoding.UTF8.GetByteCount(artifact);
                    var measurement = new BenchmarkMeasurement { requested = count, completed = e.Report.plays.Count, days = p.durationDays, transitions = e.Report.transitions, seconds = watch.Elapsed.TotalSeconds,
                        managedBefore = before, managedAfter = GC.GetTotalMemory(false), workingSet = Process.GetCurrentProcess().WorkingSet64, estimatedRetained = e.Report.retainedBytes,
                        artifactBytes = bytes, status = e.Report.status.ToString(), stopReason = e.Report.manifest.stopReason };
                    File.WriteAllText("Logs/BalanceBenchmark/measurement-" + count + ".json", JsonUtility.ToJson(measurement, true));
                    File.WriteAllText("Logs/BalanceBenchmark/report-" + count + ".json", artifact);
                    UnityEngine.Debug.Log("BALANCE_BENCHMARK " + JsonUtility.ToJson(measurement));
                }
                EditorApplication.Exit(0);
            }
            catch (Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
