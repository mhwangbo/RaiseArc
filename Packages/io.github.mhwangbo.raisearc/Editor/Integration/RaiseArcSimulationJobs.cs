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
    [Serializable] public sealed class SimulationCapabilities
    {
        public string engine = "simulation-1";
        public string[] modes = { "0 QuickCheck", "1 TargetSearch", "2 Exhaustive", "3 MonteCarlo" };
        public string[] policies = { "uniform-v1", "weighted-v1" };
        public bool checkpoints = true, targetFinalResults = true, statelessDeterministicCustomRules = true, requiresExternalCodeOptIn = true;
        public bool sceneAutoplay, statefulRandomCustomRules, liveDiskFrontier;
        public int maxConcurrentJobs = 2, maxRetainedJobs = 8, maxRuns = 100000, maxStateRecords = 1000000;
        public string scope = "Actual GameSession inputs; provider contracts are premises. Model paths are hypothetical. NotFound is not Unreachable. No content edits.";
    }
    [Serializable] public sealed class SimulationCommand
    {
        public string operation, jobId, targetId;
        public int recordId = -1, offset, limit = 100;
        public ExplorationSettings settings = new ExplorationSettings();
        public StateData initial;
        public bool useInitial;
    }
    [Serializable] public sealed class SimulationResponse
    {
        public bool success, stale, found;
        public string error, jobId, checkpointId;
        public SimulationCapabilities capabilities;
        public AnalysisManifest manifest;
        public ExplorationReport report;
        public List<CoverageRow> coverage;
        public List<BranchCoverage> branches;
        public List<SimulationRecord> path;
        public ReplayVerification replay;
        public int startedRuns, runningRuns, totalMatches;
        public ExplorationStatus status;
        public int states, transitions, frontier;
        public double elapsedSeconds;
        public List<PlaySummary> outcomes;
    }
    [Serializable] internal sealed class SimulationDiskFile
    {
        public string assetGuid, jobId;
        public SimulationCheckpoint checkpoint;
    }
    public sealed class SimulationJob
    {
        internal ProjectDefinition Source;
        public string Id { get; internal set; }
        public GameProjectAsset Asset { get; internal set; }
        public SimulationExplorer Explorer { get; internal set; }
    }

    [InitializeOnLoad]
    public static class SimulationJobs
    {
        private static readonly Dictionary<string, SimulationJob> jobs = new Dictionary<string, SimulationJob>(StringComparer.Ordinal);
        private static readonly UnityProjectCodec codec = new UnityProjectCodec();
        private const string DirectoryPath = "Library/RaiseArc/Simulation";
        public static event Action<GameProjectAsset, string, SimulationRecord, RuntimeObservation> NavigateRequested;
        public static event Action<GameProjectAsset, RaiseArc.Core.GamePlan, StateData> PlanRequested;
        public static void OpenPlan(GameProjectAsset project, RaiseArc.Core.GamePlan plan, StateData state)
        { if (PlanRequested == null) throw new InvalidOperationException("Simulation Explorer is not installed."); PlanRequested(project, plan, state); }
        public static IEnumerable<SimulationJob> All => jobs.Values;
        public static AnalysisAdapters Adapters { get; } = new AnalysisAdapters();
        static SimulationJobs()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += SaveBeforeReload;
            EditorApplication.quitting += SaveBeforeReload;
        }
        public static string Fingerprint(GameProjectAsset asset) => SimulationIdentity.Hash(JsonUtility.ToJson(asset.Read()));
        private static string RuntimeFingerprint => typeof(GameSession).Assembly.ManifestModule.ModuleVersionId.ToString();
        public static bool Stale(SimulationJob job) => job.Asset == null || Fingerprint(job.Asset) != job.Explorer.Report.manifest.contentFingerprint || RuntimeFingerprint != job.Explorer.Report.manifest.runtimeFingerprint || Adapters.Fingerprint != job.Explorer.Report.manifest.adapterFingerprint;
        private static AnalysisManifest Manifest(GameProjectAsset asset, ExplorationSettings settings, StateData initial) => new AnalysisManifest
        {
            settings = JsonUtility.FromJson<ExplorationSettings>(JsonUtility.ToJson(settings)),
            initial = initial == null ? null : JsonUtility.FromJson<StateData>(JsonUtility.ToJson(initial)),
            contentFingerprint = Fingerprint(asset), runtimeFingerprint = RuntimeFingerprint
        };
        public static SimulationJob Start(GameProjectAsset asset, ExplorationSettings settings, StateData initial = null)
        {
            if (asset == null) throw new ArgumentException("Select a GameProjectAsset.");
            if (jobs.Count >= 8) throw new InvalidOperationException("Eight analyses are retained. Export or checkpoint a result, then remove it from memory before starting another.");
            if (jobs.Values.Count(j => j.Explorer.Report.status == ExplorationStatus.Running) >= 2) throw new InvalidOperationException("Pause another analysis before starting a third concurrent run.");
            var api = new AuthoringService(asset.Read(), codec, asset.CreateExtensions());
            var job = new SimulationJob { Id = Guid.NewGuid().ToString("N"), Asset = asset, Source = api.Snapshot(),
                Explorer = new SimulationExplorer(api.Snapshot(), codec, Manifest(asset, settings, initial), Adapters) };
            jobs.Add(job.Id, job); return job;
        }
        public static SimulationJob Find(string id) => jobs.TryGetValue(id ?? "", out var job) ? job : throw new ArgumentException("Unknown simulation job. Load its checkpoint or start an analysis.");
        public static void Forget(string id)
        {
            var job = Find(id);
            if (job.Explorer.Report.status == ExplorationStatus.Running) throw new InvalidOperationException("Pause or cancel this analysis before removing it from memory.");
            jobs.Remove(id);
        }
        private static void Update()
        {
            foreach (var job in jobs.Values.ToArray())
            {
                if (job.Explorer.Report.status != ExplorationStatus.Running) continue;
                try { job.Explorer.Tick(8, 4); }
                catch (Exception ex) { job.Explorer.Pause(); Debug.LogError("RaiseArc Simulation paused: " + ex.Message); }
            }
        }
        public static string SaveCheckpoint(SimulationJob job)
        {
            job.Explorer.Pause();
            var file = new SimulationDiskFile { assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(job.Asset)), jobId = job.Id, checkpoint = job.Explorer.Checkpoint };
            var json = JsonUtility.ToJson(file);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > job.Explorer.Report.manifest.settings.maxCheckpointMiB * 1048576L)
                throw new InvalidOperationException("Checkpoint exceeds configured disk budget. Analysis remains paused; previous checkpoint was preserved.");
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, job.Id + ".json"); var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            return job.Id;
        }
        public static SimulationJob LoadCheckpoint(GameProjectAsset asset, string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid checkpoint ID.");
            if (jobs.TryGetValue(id, out var existing) && existing.Explorer.Report.status == ExplorationStatus.Running) throw new InvalidOperationException("Pause the existing job before loading an earlier checkpoint.");
            var path = Path.Combine(DirectoryPath, id + ".json");
            if (new FileInfo(path).Length > 256 * 1048576L) throw new ArgumentException("Checkpoint exceeds import size limit.");
            var file = JsonUtility.FromJson<SimulationDiskFile>(File.ReadAllText(path));
            if (file.assetGuid != AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))) throw new ArgumentException("Checkpoint belongs to a different project asset.");
            var saved = file.checkpoint.report.manifest;
            var job = new SimulationJob { Id = id, Asset = asset, Explorer = new SimulationExplorer(new AuthoringService(asset.Read(), codec, asset.CreateExtensions()).Snapshot(), codec,
                Manifest(asset, saved.settings, saved.initial), Adapters, file.checkpoint) };
            jobs[id] = job; return job;
        }
        public static IEnumerable<string> SavedCheckpointIds() => Directory.Exists(DirectoryPath) ? Directory.GetFiles(DirectoryPath, "*.json").Select(Path.GetFileNameWithoutExtension) : Enumerable.Empty<string>();
        private static void SaveBeforeReload()
        {
            foreach (var job in jobs.Values)
                if (job.Explorer.Report.status == ExplorationStatus.Running || job.Explorer.Report.status == ExplorationStatus.Paused)
                    try { SaveCheckpoint(job); } catch (Exception ex) { Debug.LogWarning("RaiseArc checkpoint not saved: " + ex.Message); }
        }
        public static void Navigate(SimulationJob job, string contentId, SimulationRecord record = null, RuntimeObservation observation = null)
        {
            if (Stale(job)) throw new InvalidOperationException("Analysis is stale. Rerun against the current content before applying a graph overlay.");
            NavigateRequested?.Invoke(job.Asset, contentId, record, observation);
        }
        public static bool Handles(string operation) => new[] { "SimulationCapabilities", "StartSimulation", "GetSimulationProgress", "PauseSimulation", "ResumeSimulation", "CancelSimulation", "ForgetSimulation", "SaveSimulationCheckpoint", "LoadSimulationCheckpoint", "GetSimulationCoverage", "GetSimulationBranches", "GetSimulationOutcomes", "GetUnverifiedContent", "GetSimulationPath", "VerifySimulationPath", "ExportSimulationReport" }.Contains(operation);
        public static string Execute(GameProjectAsset asset, string json)
        {
            var response = new SimulationResponse();
            try
            {
                var c = JsonUtility.FromJson<SimulationCommand>(json);
                if (!Handles(c.operation)) throw new ArgumentException("Simulation operation is not allowlisted.");
                if (c.operation == "SimulationCapabilities")
                {
                    response.capabilities = new SimulationCapabilities();
                    response.success = true; return JsonUtility.ToJson(response);
                }
                var job = c.operation == "StartSimulation" ? Start(asset, c.settings, c.useInitial ? c.initial : null) :
                    c.operation == "LoadSimulationCheckpoint" ? LoadCheckpoint(asset, c.jobId) : Find(c.jobId);
                if (job.Asset != asset) throw new ArgumentException("Job belongs to a different selected project.");
                response.manifest = job.Explorer.Report.manifest; response.jobId = job.Id; response.stale = Stale(job);
                switch (c.operation)
                {
                    case "PauseSimulation": job.Explorer.Pause(); break;
                    case "ResumeSimulation": if (Stale(job)) throw new InvalidOperationException("Stale analysis cannot resume."); job.Explorer.Resume(); break;
                    case "CancelSimulation": job.Explorer.Cancel(); break;
                    case "ForgetSimulation": Forget(job.Id); break;
                    case "SaveSimulationCheckpoint": response.checkpointId = SaveCheckpoint(job); break;
                    case "GetSimulationCoverage": case "GetUnverifiedContent":
                        var rows = job.Explorer.Report.coverage.AsEnumerable();
                        if (c.operation == "GetUnverifiedContent") rows = rows.Where(r => r.status != ReachStatus.Reached);
                        response.totalMatches = rows.Count(); response.coverage = rows.Skip(Math.Max(0, c.offset)).Take(Math.Max(1, Math.Min(500, c.limit))).ToList(); break;
                    case "GetSimulationPath":
                        var record = c.recordId >= 0 ? c.recordId : job.Explorer.Report.coverage.Find(r => r.id == c.targetId)?.firstRecord ?? -1;
                        response.found = record >= 0;
                        if (response.found) { var path = job.Explorer.Path(record); response.totalMatches = path.Count; response.path = path.Skip(Math.Max(0, c.offset)).Take(Math.Max(1, Math.Min(200, c.limit))).ToList(); } break;
                    case "GetSimulationOutcomes":
                        response.totalMatches = job.Explorer.Report.plays.Count; response.outcomes = job.Explorer.Report.plays.Skip(Math.Max(0, c.offset)).Take(Math.Max(1, Math.Min(500, c.limit))).ToList(); break;
                    case "GetSimulationBranches":
                        response.totalMatches = job.Explorer.Report.branches.Count; response.branches = job.Explorer.Report.branches.Skip(Math.Max(0, c.offset)).Take(Math.Max(1, Math.Min(500, c.limit))).ToList(); break;
                    case "VerifySimulationPath":
                        if (Stale(job)) throw new InvalidOperationException("Stale analysis cannot be replayed against changed content/code.");
                        response.replay = job.Explorer.VerifyPath(c.recordId); break;
                    case "ExportSimulationReport": response.report = job.Explorer.Report; break;
                }
                response.manifest = job.Explorer.Report.manifest; response.jobId = job.Id; response.stale = Stale(job); response.success = true;
                response.startedRuns = job.Explorer.Report.startedRuns; response.runningRuns = job.Explorer.Report.runningRuns; response.status = job.Explorer.Report.status;
                response.states = job.Explorer.Report.states; response.transitions = job.Explorer.Report.transitions; response.frontier = job.Explorer.Report.frontier; response.elapsedSeconds = job.Explorer.Report.elapsedSeconds;
            }
            catch (Exception ex) { response.error = ex.Message; }
            var output = JsonUtility.ToJson(response, true);
            if (System.Text.Encoding.UTF8.GetByteCount(output) > 15 * 1048576)
            {
                response.success = false; response.error = "Response exceeds pipe budget. Use paginated coverage/outcome/path queries or Editor file export.";
                response.report = null; response.path = null; response.coverage = null; response.outcomes = null; response.branches = null; output = JsonUtility.ToJson(response, true);
            }
            return output;
        }
    }
}
