using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    [Serializable] public sealed class EndingReplayJob
    {
        public string id, sourceResultId, projectGuid, contentFingerprint, runtimeFingerprint, adapterFingerprint, artifactId;
        public bool currentCandidate;
        public string status = "Running", error = "";
        public EndingPathReplay replay = new EndingPathReplay();
        [NonSerialized] internal IEnumerator<SimulationRecord> steps;
        [NonSerialized] internal Stopwatch clock;
        [NonSerialized] internal int maxSeconds;
    }
    [InitializeOnLoad]
    public static class EndingReplayJobs
    {
        private static readonly Dictionary<string, EndingReplayJob> jobs = new Dictionary<string, EndingReplayJob>();
        static EndingReplayJobs() { EditorApplication.update += Update; AssemblyReloadEvents.beforeAssemblyReload += CancelAll; }
        public static EndingReplayJob Start(GameProjectAsset asset, string resultId, bool currentCandidate)
        {
            if (jobs.Count >= 32) throw new InvalidOperationException("Replay retention limit reached. Reload the editor after exporting results.");
            var saved = EndingTests.ReadResult(asset, resultId);
            var job = new EndingReplayJob { id = Guid.NewGuid().ToString("N"), sourceResultId = resultId, currentCandidate = currentCandidate,
                projectGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)),
                contentFingerprint = currentCandidate ? SimulationJobs.Fingerprint(asset) : saved.checkpoint.report.manifest.contentFingerprint,
                runtimeFingerprint = typeof(PrincessStudio.Core.GameSession).Assembly.ManifestModule.ModuleVersionId.ToString(),
                adapterFingerprint = SimulationJobs.Adapters.Fingerprint, maxSeconds = saved.definition.settings.maxSeconds, clock = Stopwatch.StartNew() };
            job.steps = EndingTests.ReplaySteps(asset, resultId, currentCandidate, job.replay).GetEnumerator();
            jobs.Add(job.id, job); return job;
        }
        public static EndingReplayJob Find(GameProjectAsset asset, string id)
        {
            if (!jobs.TryGetValue(id ?? "", out var job) || job.projectGuid != AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))) throw new ArgumentException("Unknown replay job for this project.");
            return job;
        }
        public static void Cancel(EndingReplayJob job)
        { if (job.status == "Running") Finish(job, "Cancelled"); }
        private static void CancelAll() { foreach (var job in jobs.Values) Cancel(job); }
        private static void Update()
        {
            foreach (var job in jobs.Values.Where(j => j.status == "Running").ToArray())
            {
                try
                {
                    if (job.clock.Elapsed.TotalSeconds >= job.maxSeconds) { Finish(job, "BudgetStopped"); continue; }
                    var timer = Stopwatch.StartNew();
                    for (var i = 0; i < 8 && timer.ElapsedMilliseconds < 4; i++)
                        if (!job.steps.MoveNext()) { Finish(job, "Completed"); break; }
                }
                catch (Exception ex) { job.error = ex.Message; Finish(job, "Error"); }
            }
        }
        private static void Finish(EndingReplayJob job, string status)
        {
            job.steps.Dispose(); job.clock.Stop(); job.status = status;
            if (status != "Completed") job.replay.sameStates = false;
            job.artifactId = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory("RaiseArcAnalysisResults");
            using (var writer = new StreamWriter(new FileStream("RaiseArcAnalysisResults/" + job.artifactId + ".replay.json", FileMode.CreateNew)))
                writer.Write(JsonUtility.ToJson(job));
        }
    }
}
