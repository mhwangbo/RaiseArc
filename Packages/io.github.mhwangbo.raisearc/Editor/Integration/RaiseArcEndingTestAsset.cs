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
    public sealed class RaiseArcEndingTestAsset : ScriptableObject
    {
        [SerializeField] private GameProjectAsset project;
        [SerializeField] private int revision;
        [SerializeField, TextArea] private string definitionJson;
        public GameProjectAsset Project => project;
        public int Revision => revision;
        public EndingTestDefinition Read() => JsonUtility.FromJson<EndingTestDefinition>(definitionJson);
        internal void Write(GameProjectAsset target, EndingTestDefinition definition)
        { project = target; definitionJson = JsonUtility.ToJson(definition); revision++; }
    }

    [Serializable] public sealed class EndingTestResult
    {
        public int formatVersion = 1;
        public string id, testId, projectGuid, createdUtc;
        public int testRevision;
        public EndingTestDefinition definition;
        public ProjectDefinition source;
        public SimulationCheckpoint checkpoint;
    }

    public static class EndingTests
    {
        public static event Action<GameProjectAsset, string> OpenRequested;
        public static event Action<RaiseArcEndingTestAsset> OpenSavedRequested;
        public static void Open(GameProjectAsset asset, string endingId) => OpenRequested?.Invoke(asset, endingId);
        public static void Open(RaiseArcEndingTestAsset test) => OpenSavedRequested?.Invoke(test);
        private static readonly UnityProjectCodec codec = new UnityProjectCodec();
        private const string ResultsDirectory = "RaiseArcAnalysisResults";
        private sealed class Run
        {
            internal EndingTestDefinition definition;
            internal int revision;
        }
        private static readonly Dictionary<string, Run> runs = new Dictionary<string, Run>();

        public static EndingTestDefinition New(GameProjectAsset project, string endingId) => new EndingTestDefinition {
            id = Guid.NewGuid().ToString("N"), name = "Ending · " + endingId, projectId = project.Read().id, endingId = endingId,
            settings = new ExplorationSettings { mode = ExplorationMode.TargetSearch, endingGoal = true, horizonDays = project.Read().durationDays }
        };

        public static void Validate(GameProjectAsset asset, EndingTestDefinition definition)
        {
            if (asset == null || definition == null) throw new ArgumentException("Select a project and test.");
            var source = asset.Read();
            var initial = definition.CreateInitial(source, codec);
            new SimulationExplorer(source, codec, new AnalysisManifest { initial = initial, settings = definition.SearchSettings() }, SimulationJobs.Adapters);
        }

        public static RaiseArcEndingTestAsset Save(GameProjectAsset project, EndingTestDefinition definition, RaiseArcEndingTestAsset existing, int expectedRevision)
        {
            if ((existing == null ? 0 : existing.Revision) != expectedRevision) throw new InvalidOperationException("Test revision conflict. Reload the saved test before editing.");
            if (existing != null && (existing.Project != project || existing.Read().id != definition.id)) throw new ArgumentException("Test identity and project cannot be changed.");
            Validate(project, definition);
            if (existing == null)
            {
                if (List(project).Any(t => t.Read().id == definition.id)) throw new ArgumentException("Test ID already exists.");
                if (!AssetDatabase.IsValidFolder("Assets/RaiseArcTests")) AssetDatabase.CreateFolder("Assets", "RaiseArcTests");
                existing = ScriptableObject.CreateInstance<RaiseArcEndingTestAsset>();
                existing.Write(project, definition);
                AssetDatabase.CreateAsset(existing, AssetDatabase.GenerateUniqueAssetPath("Assets/RaiseArcTests/EndingTest.asset"));
                Undo.RegisterCreatedObjectUndo(existing, "Create ending test");
            }
            else
            {
                Undo.RecordObject(existing, "Edit ending test");
                existing.Write(project, definition);
                EditorUtility.SetDirty(existing);
            }
            AssetDatabase.SaveAssets();
            return existing;
        }

        public static IEnumerable<RaiseArcEndingTestAsset> List(GameProjectAsset project) => AssetDatabase.FindAssets("t:RaiseArcEndingTestAsset")
            .Select(g => AssetDatabase.LoadAssetAtPath<RaiseArcEndingTestAsset>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t != null && t.Project == project);

        public static SimulationJob Start(GameProjectAsset project, EndingTestDefinition definition, int revision = 0)
        {
            Validate(project, definition);
            var snapshot = JsonUtility.FromJson<EndingTestDefinition>(JsonUtility.ToJson(definition));
            var job = SimulationJobs.Start(project, snapshot.SearchSettings(), snapshot.CreateInitial(project.Read(), codec));
            job.Explorer.Report.manifest.initialOrigin = "new-game with test-only money/stat overrides";
            job.Explorer.Report.manifest.scope = "New-game activity-selection paths under the saved test's overrides and activity restrictions. GameSession owns daily progression, event waits and ending selection. Arbitrary queued schedules, module cancellation/resume inputs and Scene restoration are not enumerated by this search.";
            runs.Add(job.Id, new Run { definition = snapshot, revision = revision });
            return job;
        }

        public static int TargetRecord(SimulationJob job) => job.Explorer.Records.ToList().FindIndex(r => r.state.endingId == job.Explorer.Report.manifest.settings.targetId);
        public static EndingTestVerdict Verdict(SimulationJob job)
        {
            var r = job.Explorer.Report;
            var target = TargetRecord(job);
            if (target >= 0) return job.Explorer.Records[target].assumptions.Count > 0 ? EndingTestVerdict.ReachedUnderModel : EndingTestVerdict.Reached;
            if (r.status == ExplorationStatus.Cancelled) return EndingTestVerdict.Cancelled;
            if (r.status == ExplorationStatus.BudgetStopped || job.Explorer.Records.Any(x => x.result == PlayResult.Budget)) return EndingTestVerdict.BudgetStopped;
            if (job.Explorer.Records.Any(x => x.result == PlayResult.RuntimeError)) return EndingTestVerdict.Error;
            if (r.manifest.hasUnsupported) return EndingTestVerdict.Unsupported;
            if (r.status == ExplorationStatus.Running || r.status == ExplorationStatus.Paused) return EndingTestVerdict.Searching;
            return r.manifest.completeWithinScope ? EndingTestVerdict.UnreachableInScope : EndingTestVerdict.NotFound;
        }

        public static string Preserve(SimulationJob job)
        {
            if (job.Explorer.Report.status == ExplorationStatus.Running) throw new InvalidOperationException("Wait for completion or pause before preserving a result.");
            if (!runs.TryGetValue(job.Id, out var run)) throw new InvalidOperationException("This job was not started from an ending test in this editor session.");
            var result = new EndingTestResult { id = Guid.NewGuid().ToString("N"), testId = run.definition.id, testRevision = run.revision,
                definition = run.definition, source = job.Source, checkpoint = job.Explorer.Checkpoint,
                projectGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(job.Asset)), createdUtc = DateTime.UtcNow.ToString("O") };
            Directory.CreateDirectory(ResultsDirectory);
            var json = JsonUtility.ToJson(result);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > job.Explorer.Report.manifest.settings.maxCheckpointMiB * 1048576L) throw new InvalidOperationException("Result exceeds the configured artifact budget.");
            using (var writer = new StreamWriter(new FileStream(Path.Combine(ResultsDirectory, result.id + ".json"), FileMode.CreateNew))) writer.Write(json);
            return result.id;
        }

        public static EndingTestResult ReadResult(GameProjectAsset asset, string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid result ID.");
            var path = Path.Combine(ResultsDirectory, id + ".json");
            if (new FileInfo(path).Length > 256 * 1048576L) throw new ArgumentException("Result exceeds import budget.");
            var result = JsonUtility.FromJson<EndingTestResult>(File.ReadAllText(path));
            if (result.formatVersion != 1 || result.projectGuid != AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))) throw new ArgumentException("Unsupported result or different project.");
            return result;
        }

        public static EndingPathReplay Replay(GameProjectAsset asset, string resultId, bool currentCandidate)
        {
            var result = new EndingPathReplay();
            foreach (var step in ReplaySteps(asset, resultId, currentCandidate, result)) { }
            return result;
        }

        public static IEnumerable<SimulationRecord> ReplaySteps(GameProjectAsset asset, string resultId, bool currentCandidate, EndingPathReplay result)
        {
            var saved = ReadResult(asset, resultId);
            var manifest = saved.checkpoint.report.manifest;
            var runtime = typeof(GameSession).Assembly.ManifestModule.ModuleVersionId.ToString();
            if (!currentCandidate && (manifest.runtimeFingerprint != runtime || manifest.adapterFingerprint != SimulationJobs.Adapters.Fingerprint))
                throw new InvalidOperationException("Original runtime or adapters are unavailable. Use current-candidate recheck; exact original replay is unverified.");
            var source = currentCandidate ? asset.Read() : saved.source;
            var initial = currentCandidate ? saved.definition.CreateInitial(source, codec) : manifest.initial;
            var explorer = new SimulationExplorer(source, codec, new AnalysisManifest { initial = initial, settings = saved.definition.SearchSettings() }, SimulationJobs.Adapters);
            var target = saved.checkpoint.records.FindIndex(r => r.state.endingId == saved.definition.endingId);
            if (target < 0) throw new InvalidOperationException("This result has no discovered target path.");
            var path = new List<SimulationRecord>();
            for (var i = target; i >= 0;)
            {
                var record = saved.checkpoint.records[i];
                if (record.parent >= i || record.parent < -1)
                    throw new InvalidOperationException("Saved path has an invalid parent chain.");
                path.Add(record);
                i = record.parent;
            }
            path.Reverse();
            return explorer.ReplaySteps(path, initial, result);
        }
    }
}
