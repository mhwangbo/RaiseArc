using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEngine;
using RaiseArc.Analysis;

namespace RaiseArc.Editor
{
    [Serializable] public sealed class TestSuiteEntry { public string kind = "balance", testId = ""; }
    [Serializable] public sealed class TestSuiteDefinition
    {
        public int version = 1;
        public string id = "", name = "Saved test suite";
        public List<TestSuiteEntry> entries = new List<TestSuiteEntry>();
    }
    public sealed class RaiseArcTestSuiteAsset : ScriptableObject
    {
        [SerializeField] private GameProjectAsset project;
        [SerializeField] private int revision;
        [SerializeField, TextArea] private string definitionJson;
        public GameProjectAsset Project => project;
        public int Revision => revision;
        public TestSuiteDefinition Read() => JsonUtility.FromJson<TestSuiteDefinition>(definitionJson);
        internal void Write(GameProjectAsset p, TestSuiteDefinition d) { project = p; definitionJson = JsonUtility.ToJson(d); revision++; }
    }
    [Serializable] public sealed class TestSuiteEntryResult
    {
        public string kind, testId, jobId, resultId, execution = "NotStarted", verdict = "Pending", error = "";
        public int testRevision;
    }
    [Serializable] public sealed class TestSuiteRun
    {
        public string id;
        public TestSuiteDefinition definition;
        public int revision;
        public string execution = "Running";
        public List<TestSuiteEntryResult> entries = new List<TestSuiteEntryResult>();
        [NonSerialized] public GameProjectAsset Project;
    }
    [InitializeOnLoad] public static class TestSuites
    {
        private static readonly Dictionary<string, TestSuiteRun> jobs = new Dictionary<string, TestSuiteRun>();
        static TestSuites() { EditorApplication.update += Update; }
        public static IEnumerable<RaiseArcTestSuiteAsset> List(GameProjectAsset p) => AssetDatabase.FindAssets("t:RaiseArcTestSuiteAsset").Select(g => AssetDatabase.LoadAssetAtPath<RaiseArcTestSuiteAsset>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t.Project == p);
        public static RaiseArcTestSuiteAsset Save(GameProjectAsset p, TestSuiteDefinition d, RaiseArcTestSuiteAsset asset, int revision)
        {
            if ((asset == null ? 0 : asset.Revision) != revision) throw new InvalidOperationException("Suite revision conflict.");
            if (p == null || d == null || d.version != 1 || string.IsNullOrWhiteSpace(d.id) || string.IsNullOrWhiteSpace(d.name) || d.entries.Count < 1 || d.entries.Count > 32) throw new ArgumentException("Invalid saved test suite.");
            if (asset != null && (asset.Project != p || asset.Read().id != d.id)) throw new ArgumentException("Suite identity/project cannot change.");
            foreach (var e in d.entries)
                if (e.kind == "balance" ? !BalanceTests.List(p).Any(t => t.Read().id == e.testId) : e.kind != "ending" || !EndingTests.List(p).Any(t => t.Read().id == e.testId)) throw new ArgumentException("Missing suite test: " + e.testId);
            if (asset == null)
            {
                if (List(p).Any(t => t.Read().id == d.id)) throw new ArgumentException("Duplicate suite ID.");
                if (!AssetDatabase.IsValidFolder("Assets/RaiseArcTests")) AssetDatabase.CreateFolder("Assets", "RaiseArcTests");
                asset = ScriptableObject.CreateInstance<RaiseArcTestSuiteAsset>(); asset.Write(p, d);
                AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath("Assets/RaiseArcTests/TestSuite.asset")); Undo.RegisterCreatedObjectUndo(asset, "Create test suite");
            }
            else { Undo.RecordObject(asset, "Edit test suite"); asset.Write(p, d); EditorUtility.SetDirty(asset); }
            AssetDatabase.SaveAssets(); return asset;
        }
        public static TestSuiteRun Start(RaiseArcTestSuiteAsset asset)
        {
            if (jobs.Values.Any(j => j.execution == "Running")) throw new InvalidOperationException("A suite is already running.");
            var d = asset.Read(); var run = new TestSuiteRun { id = Guid.NewGuid().ToString("N"), definition = d, revision = asset.Revision, Project = asset.Project,
                entries = d.entries.Select(e => new TestSuiteEntryResult { kind = e.kind, testId = e.testId,
                    testRevision = e.kind == "balance" ? BalanceTests.List(asset.Project).Single(t => t.Read().id == e.testId).Revision : EndingTests.List(asset.Project).Single(t => t.Read().id == e.testId).Revision }).ToList() };
            jobs.Add(run.id, run); return run;
        }
        public static TestSuiteRun Find(string id, GameProjectAsset p) => jobs.TryGetValue(id ?? "", out var j) && j.Project == p ? j : throw new ArgumentException("Suite job not found in this project.");
        public static void Cancel(TestSuiteRun j)
        {
            foreach (var e in j.entries.Where(e => e.execution == "Running"))
            {
                if (e.kind == "balance") BalanceTests.Cancel(BalanceTests.Find(e.jobId)); else SimulationJobs.Find(e.jobId).Explorer.Cancel();
                e.execution = "Cancelled"; e.verdict = "Inconclusive";
            }
            j.execution = "Cancelled";
        }
        private static void Update() { foreach (var j in jobs.Values.Where(j => j.execution == "Running").ToArray()) Tick(j); }
        public static void Tick(TestSuiteRun j)
        {
            var entry = j.entries.FirstOrDefault(e => e.execution == "Running" || e.execution == "NotStarted");
            if (entry == null) { j.execution = "Completed"; return; }
            try
            {
                if (entry.execution == "NotStarted")
                {
                    if (entry.kind == "balance")
                    {
                        var test = BalanceTests.List(j.Project).Single(t => t.Read().id == entry.testId);
                        if (test.Revision != entry.testRevision) throw new InvalidOperationException("Suite test changed since execution began.");
                        entry.jobId = BalanceTests.Start(test).Id;
                    }
                    else
                    {
                        var test = EndingTests.List(j.Project).Single(t => t.Read().id == entry.testId);
                        if (test.Revision != entry.testRevision) throw new InvalidOperationException("Suite test changed since execution began.");
                        entry.jobId = EndingTests.Start(j.Project, test.Read(), test.Revision).Id;
                    }
                    entry.execution = "Running"; return;
                }
                if (entry.kind == "balance")
                {
                    var child = BalanceTests.Find(entry.jobId); if (child.Data.status == ExplorationStatus.Running || child.Data.status == ExplorationStatus.Paused) return;
                    entry.execution = child.Data.status.ToString(); var verdicts = child.Reports(false).SelectMany(p => p.criteria).Select(c => c.verdict).ToList();
                    entry.verdict = verdicts.Count == 0 ? "NoCriteria" : verdicts.Contains(BalanceVerdict.Fail) ? "Fail" : verdicts.All(v => v == BalanceVerdict.Pass) ? "Pass" : "Inconclusive";
                    entry.resultId = BalanceTests.Preserve(child); BalanceTests.Forget(child);
                }
                else
                {
                    var child = SimulationJobs.Find(entry.jobId); if (child.Explorer.Report.status == ExplorationStatus.Running || child.Explorer.Report.status == ExplorationStatus.Paused) return;
                    entry.execution = child.Explorer.Report.status.ToString(); entry.verdict = EndingTests.Verdict(child).ToString();
                    entry.resultId = EndingTests.Preserve(child); SimulationJobs.Forget(child.Id);
                }
            }
            catch (Exception e) { entry.execution = "Error"; entry.verdict = "Inconclusive"; entry.error = e.Message; }
        }
    }
}
