using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    [Serializable] public sealed class BalanceArtifact
    {
        public int formatVersion = 1, testRevision;
        public string id, testAssetGuid, projectGuid, createdUtc, jobId;
        public BalanceTestDefinition definition;
        public ProjectDefinition source;
        public ExplorationStatus status;
        public List<SimulationCheckpoint> policies = new List<SimulationCheckpoint>();
        public string preservation = "Original content, definition and checkpoints; first outcome/coverage/ledger witnesses kept; other runs reproducible by stored seeds and policy when fingerprints match.";
    }
    public sealed class BalanceJob
    {
        public string Id;
        public GameProjectAsset Asset;
        public BalanceArtifact Data;
        public List<SimulationExplorer> Explorers = new List<SimulationExplorer>();
        public IEnumerable<BalancePolicyReport> Reports(bool distributions = true) => Explorers.Select((e, i) => BalanceReports.Build(Data.definition, Data.definition.policies[i], e.Report, distributions));
    }
    [Serializable] public sealed class BalanceDifference
    {
        public string path, before, after;
    }
    [Serializable] public sealed class BalanceResultComparison
    {
        public string beforeId, afterId;
        public bool sameDefinition, sameRuntime, sameAdapters;
        public string interpretation = "Observed differences under recorded policies; same seeds do not guarantee the same random draws after content edits. Multiple edits are not a causal attribution.";
        public List<string> addedActivities, removedActivities;
        public List<BalanceDifference> changes = new List<BalanceDifference>();
        public List<BalancePolicyReport> before, after;
        public List<BalanceLedgerRow> beforeLedger, afterLedger;
        [NonSerialized] internal BalanceArtifact beforeSource, afterSource;
    }
    [InitializeOnLoad] public static class BalanceTests
    {
        private static readonly UnityProjectCodec codec = new UnityProjectCodec();
        private static readonly Dictionary<string, BalanceJob> jobs = new Dictionary<string, BalanceJob>();
        public static IEnumerable<BalanceJob> All => jobs.Values;
        public static event Action<RaiseArcBalanceTestAsset> OpenRequested;
        public static void Open(RaiseArcBalanceTestAsset test) => OpenRequested?.Invoke(test);
        public static string RuntimeFingerprint => typeof(GameSession).Assembly.ManifestModule.ModuleVersionId.ToString();
        private const string DirectoryPath = "RaiseArcAnalysisResults";
        static BalanceTests()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += SaveActive;
            EditorApplication.quitting += SaveActive;
        }
        private static T Copy<T>(T v) => JsonUtility.FromJson<T>(JsonUtility.ToJson(v));
        public static BalanceTestDefinition New(GameProjectAsset asset) => new BalanceTestDefinition { id = Guid.NewGuid().ToString("N"), projectId = asset.Read().id,
            money = asset.Read().startingMoney, settings = new ExplorationSettings { mode = ExplorationMode.MonteCarlo, horizonDays = asset.Read().durationDays } };
        public static void Validate(GameProjectAsset asset, BalanceTestDefinition definition)
        {
            if (asset == null || definition == null) throw new ArgumentException("Select a saved game and balance definition.");
            var p = asset.Read(); var initial = definition.CreateInitial(p, codec);
            foreach (var policy in definition.policies) new SimulationExplorer(p, codec, Manifest(p, definition.PolicySettings(policy), initial), SimulationJobs.Adapters);
        }
        private static AnalysisManifest Manifest(ProjectDefinition p, ExplorationSettings s, StateData initial) => new AnalysisManifest {
            initial = initial, settings = s, contentFingerprint = SimulationIdentity.Hash(JsonUtility.ToJson(p)), runtimeFingerprint = RuntimeFingerprint };
        public static IEnumerable<RaiseArcBalanceTestAsset> List(GameProjectAsset project) => AssetDatabase.FindAssets("t:RaiseArcBalanceTestAsset")
            .Select(g => AssetDatabase.LoadAssetAtPath<RaiseArcBalanceTestAsset>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t != null && t.Project == project);
        public static RaiseArcBalanceTestAsset Save(GameProjectAsset project, BalanceTestDefinition definition, RaiseArcBalanceTestAsset existing, int expectedRevision)
        {
            if ((existing == null ? 0 : existing.Revision) != expectedRevision) throw new InvalidOperationException("Balance test revision conflict; reload before editing.");
            if (existing != null && (existing.Project != project || existing.Read().id != definition.id)) throw new ArgumentException("Test identity/project cannot change.");
            Validate(project, definition);
            if (existing == null)
            {
                if (List(project).Any(t => t.Read().id == definition.id)) throw new ArgumentException("Test ID already exists.");
                if (!AssetDatabase.IsValidFolder("Assets/RaiseArcTests")) AssetDatabase.CreateFolder("Assets", "RaiseArcTests");
                existing = ScriptableObject.CreateInstance<RaiseArcBalanceTestAsset>(); existing.Write(project, definition);
                AssetDatabase.CreateAsset(existing, AssetDatabase.GenerateUniqueAssetPath("Assets/RaiseArcTests/BalanceTest.asset")); Undo.RegisterCreatedObjectUndo(existing, "Create balance test");
            }
            else { Undo.RecordObject(existing, "Edit balance test"); existing.Write(project, definition); EditorUtility.SetDirty(existing); }
            AssetDatabase.SaveAssets(); return existing;
        }
        public static BalanceJob Start(RaiseArcBalanceTestAsset test, GameProjectAsset candidate = null)
        {
            if (test == null) throw new ArgumentException("Save/select a balance test first.");
            var asset = candidate == null ? test.Project : candidate; var definition = test.Read(); Validate(asset, definition); CheckCapacity();
            var p = new AuthoringService(asset.Read(), codec, asset.CreateExtensions()).Snapshot(); var initial = definition.CreateInitial(p, codec);
            var job = new BalanceJob { Id = Guid.NewGuid().ToString("N"), Asset = asset, Data = new BalanceArtifact { definition = Copy(definition), source = p, testRevision = test.Revision,
                projectGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)), testAssetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(test)), createdUtc = DateTime.UtcNow.ToString("O") } };
            job.Data.jobId = job.Id;
            foreach (var policy in definition.policies)
            {
                var explorer = new SimulationExplorer(p, codec, Manifest(p, definition.PolicySettings(policy), initial), SimulationJobs.Adapters);
                explorer.Report.manifest.initialOrigin = "Saved balance test; detached new-game money/stat overrides";
                explorer.Report.manifest.policyInformation = "Valid inputs, ordinal ID order. Uniform or authored input weights; income-first uses current money only and falls back to weights when preferred activity is unavailable. Choice uses same weights; dialogue advances automatically. No future-state inspection.";
                job.Explorers.Add(explorer); job.Data.policies.Add(explorer.Checkpoint);
            }
            jobs.Add(job.Id, job); return job;
        }
        private static void CheckCapacity()
        {
            if (jobs.Count >= 4) throw new InvalidOperationException("Four balance reports retained. Preserve and close one first.");
            if (jobs.Values.Any(j => j.Data.status == ExplorationStatus.Running)) throw new InvalidOperationException("Pause the active balance test first.");
        }
        public static BalanceJob Find(string id) => jobs.TryGetValue(id ?? "", out var j) ? j : throw new ArgumentException("Unknown balance job; reopen its preserved checkpoint.");
        public static void Navigate(BalanceJob job, int policy, string id, int record) => SimulationJobs.Navigate(new SimulationJob { Asset = job.Asset, Explorer = job.Explorers[policy] }, id, job.Explorers[policy].Records[record]);
        public static bool Stale(BalanceJob job) => job.Asset == null || SimulationJobs.Fingerprint(job.Asset) != job.Data.policies[0].report.manifest.contentFingerprint ||
            RuntimeFingerprint != job.Data.policies[0].report.manifest.runtimeFingerprint || SimulationJobs.Adapters.Fingerprint != job.Data.policies[0].report.manifest.adapterFingerprint;
        private static void Update() { foreach (var job in jobs.Values.ToArray()) Tick(job); }
        public static void Tick(BalanceJob job, int inputs = 8, int milliseconds = 4)
        {
            if (job.Data.status != ExplorationStatus.Running) return;
            var active = job.Explorers.FirstOrDefault(e => e.Report.status == ExplorationStatus.Running);
            if (active == null) { job.Data.status = job.Explorers.Any(e => e.Report.status == ExplorationStatus.BudgetStopped) ? ExplorationStatus.BudgetStopped : ExplorationStatus.Completed; return; }
            var s = job.Data.definition.settings;
            if (job.Explorers.Sum(e => e.Report.retainedBytes) >= s.maxMemoryMiB * 1048576L || job.Explorers.Sum(e => e.Report.elapsedSeconds) >= s.maxSeconds || job.Explorers.Sum(e => (long)e.Report.transitions) >= s.maxTransitions)
            {
                foreach (var e in job.Explorers.Where(e => e.Report.status == ExplorationStatus.Running)) e.StopForBudget("Balance test combined policy budget");
                job.Data.status = ExplorationStatus.BudgetStopped; return;
            }
            try { active.Tick(inputs, milliseconds); }
            catch (Exception ex) { Pause(job); Debug.LogError("Balance analysis paused: " + ex.Message); }
        }
        public static void Pause(BalanceJob job)
        {
            foreach (var e in job.Explorers) e.Pause();
            if (job.Data.status == ExplorationStatus.Running) job.Data.status = ExplorationStatus.Paused;
        }
        public static void Resume(BalanceJob job)
        {
            if (Stale(job)) throw new InvalidOperationException("Content/runtime/adapter mismatch. Start a separate result.");
            if (job.Data.status != ExplorationStatus.Paused) throw new InvalidOperationException("Only paused jobs can resume.");
            if (jobs.Values.Any(j => j != job && j.Data.status == ExplorationStatus.Running)) throw new InvalidOperationException("Pause the other job first.");
            foreach (var e in job.Explorers.Where(e => e.Report.status == ExplorationStatus.Paused)) e.Resume(); job.Data.status = ExplorationStatus.Running;
        }
        public static void Cancel(BalanceJob job) { if (job.Data.status != ExplorationStatus.Running && job.Data.status != ExplorationStatus.Paused) return; foreach (var e in job.Explorers) e.Cancel(); job.Data.status = ExplorationStatus.Cancelled; }
        public static void Forget(BalanceJob job) { if (job.Data.status == ExplorationStatus.Running) throw new InvalidOperationException("Pause before closing."); jobs.Remove(job.Id); }
        private static void SaveActive()
        {
            foreach (var j in jobs.Values.Where(j => j.Data.status == ExplorationStatus.Running || j.Data.status == ExplorationStatus.Paused).ToArray())
                try { Preserve(j); } catch (Exception e) { Debug.LogWarning("Balance checkpoint could not be saved: " + e.Message); }
        }
        public static string Preserve(BalanceJob job)
        {
            Pause(job); var copy = Copy(job.Data); copy.id = Guid.NewGuid().ToString("N");
            var json = JsonUtility.ToJson(copy); if (System.Text.Encoding.UTF8.GetByteCount(json) > copy.definition.settings.maxCheckpointMiB * 1048576L)
                throw new InvalidOperationException("Artifact exceeds configured size budget; job remains paused.");
            Directory.CreateDirectory(DirectoryPath); using (var writer = new StreamWriter(new FileStream(ArtifactPath(copy.id), FileMode.CreateNew))) writer.Write(json);
            return copy.id;
        }
        public static string ArtifactPath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid balance result ID.");
            return Path.Combine(DirectoryPath, id + ".balance.json");
        }
        public static BalanceArtifact ReadResult(string id)
        {
            var path = ArtifactPath(id); if (new FileInfo(path).Length > 512 * 1048576L) throw new ArgumentException("Artifact import exceeds 512 MiB.");
            var data = JsonUtility.FromJson<BalanceArtifact>(File.ReadAllText(path));
            ValidateArtifact(data);
            if (data.id != id) throw new ArgumentException("Balance result ID does not match its filename.");
            return data;
        }
        internal static void ValidateArtifact(BalanceArtifact data)
        {
            if (data == null || data.formatVersion != 1 || !Guid.TryParseExact(data.id, "N", out _) || data.source == null || data.definition?.policies == null ||
                data.policies == null || data.policies.Count == 0 || data.policies.Count != data.definition.policies.Count) throw new ArgumentException("Invalid balance artifact.");
            var fingerprint = SimulationIdentity.Hash(JsonUtility.ToJson(data.source));
            for (var i = 0; i < data.policies.Count; i++)
            {
                if (data.policies[i]?.report?.manifest == null) throw new ArgumentException("Missing balance result manifest.");
                if (data.policies[i].report.manifest.contentFingerprint != fingerprint) throw new ArgumentException("Preserved content fingerprint mismatch.");
                if (JsonUtility.ToJson(data.policies[i].report.manifest.settings) != JsonUtility.ToJson(data.definition.PolicySettings(data.definition.policies[i])))
                    throw new ArgumentException("Preserved policy settings do not match the saved definition.");
            }
        }
        public static BalanceJob Load(GameProjectAsset asset, string id)
        {
            CheckCapacity(); var data = ReadResult(id);
            if (data.projectGuid != AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))) throw new ArgumentException("Checkpoint belongs to another project asset GUID.");
            var job = new BalanceJob { Id = Guid.NewGuid().ToString("N"), Asset = asset, Data = data };
            for (var i = 0; i < data.policies.Count; i++)
                job.Explorers.Add(new SimulationExplorer(asset.Read(), codec, Manifest(asset.Read(), data.policies[i].report.manifest.settings, data.policies[i].report.manifest.initial), SimulationJobs.Adapters, data.policies[i]));
            jobs.Add(job.Id, job); return job;
        }
        public static List<SimulationRecord> RunPath(BalanceArtifact data, int policyIndex, int run)
        {
            if (policyIndex < 0 || policyIndex >= data.policies.Count) throw new ArgumentException("Unknown policy index.");
            var saved = data.policies[policyIndex]; var manifest = saved.report.manifest;
            if (manifest.runtimeFingerprint != RuntimeFingerprint || manifest.adapterFingerprint != SimulationJobs.Adapters.Fingerprint)
                throw new InvalidOperationException("Original runtime/adapters unavailable. Exact replay refused.");
            var summary = saved.report.plays.SingleOrDefault(p => p.run == run) ?? throw new ArgumentException("No completed/interrupted run with this ID.");
            if (summary.record >= 0)
            {
                var e = new SimulationExplorer(data.source, codec, manifest, SimulationJobs.Adapters, Copy(saved)); return e.Path(summary.record);
            }
            var settings = Copy(manifest.settings); settings.runs = 1; settings.runOffset = run;
            var replay = new SimulationExplorer(data.source, codec, Manifest(data.source, settings, manifest.initial), SimulationJobs.Adapters);
            while (replay.Report.status == ExplorationStatus.Running) replay.Tick(128, 20);
            var actual = replay.Report.plays.SingleOrDefault();
            if (actual == null || actual.result != summary.result || SimulationIdentity.StateKey(actual.finalState, new ModuleState[0]) != SimulationIdentity.StateKey(summary.finalState, new ModuleState[0]))
                throw new InvalidOperationException("Seed regeneration did not match original run; original full path was not retained.");
            return replay.Path(actual.record);
        }
        public static EndingPathReplay Replay(BalanceArtifact data, int policyIndex, int run, GameProjectAsset current = null)
        {
            var path = RunPath(data, policyIndex, run); var p = current == null ? data.source : current.Read();
            var m = data.policies[policyIndex].report.manifest;
            var initial = current == null ? path[0].state : data.definition.CreateInitial(p, codec); initial.randomState = path[0].state.randomState;
            return new SimulationExplorer(p, codec, Manifest(p, m.settings, initial), SimulationJobs.Adapters).ReplayInputs(path, initial);
        }
        public static BalanceResultComparison Compare(string beforeId, string afterId)
        {
            return Compare(ReadResult(beforeId), ReadResult(afterId));
        }
        internal static BalanceResultComparison Compare(BalanceArtifact a, BalanceArtifact b)
        {
            var result = new BalanceResultComparison { beforeId = a.id, afterId = b.id, beforeSource = a, afterSource = b, sameDefinition = JsonUtility.ToJson(a.definition) == JsonUtility.ToJson(b.definition),
                sameRuntime = a.policies[0].report.manifest.runtimeFingerprint == b.policies[0].report.manifest.runtimeFingerprint,
                sameAdapters = a.policies[0].report.manifest.adapterFingerprint == b.policies[0].report.manifest.adapterFingerprint,
                addedActivities = b.source.activities.Select(x => x.id).Except(a.source.activities.Select(x => x.id)).ToList(),
                removedActivities = a.source.activities.Select(x => x.id).Except(b.source.activities.Select(x => x.id)).ToList(),
                before = a.policies.Select((x, i) => BalanceReports.Build(a.definition, a.definition.policies[i], x.report)).ToList(),
                after = b.policies.Select((x, i) => BalanceReports.Build(b.definition, b.definition.policies[i], x.report)).ToList(),
                beforeLedger = a.policies.SelectMany((x, i) => x.report.ledger.Select(row => { var copy = Copy(row); copy.policyId = a.definition.policies[i].id; return copy; })).ToList(),
                afterLedger = b.policies.SelectMany((x, i) => x.report.ledger.Select(row => { var copy = Copy(row); copy.policyId = b.definition.policies[i].id; return copy; })).ToList() };
            foreach (var activity in a.source.activities)
            {
                var other = b.source.activities.Find(x => x.id == activity.id);
                if (other != null && JsonUtility.ToJson(activity) != JsonUtility.ToJson(other)) result.changes.Add(new BalanceDifference { path = "activities/" + activity.id, before = JsonUtility.ToJson(activity), after = JsonUtility.ToJson(other) });
            }
            // Preserve complete candidates so non-activity edits can also be inspected without inventing causation.
            if (JsonUtility.ToJson(a.source) != JsonUtility.ToJson(b.source)) result.changes.Add(new BalanceDifference { path = "project", before = SimulationIdentity.Hash(JsonUtility.ToJson(a.source)), after = SimulationIdentity.Hash(JsonUtility.ToJson(b.source)) });
            return result;
        }
    }
}
