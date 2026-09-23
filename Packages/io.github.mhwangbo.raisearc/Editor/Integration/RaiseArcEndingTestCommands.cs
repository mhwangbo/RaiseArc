using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using UnityEngine;

namespace RaiseArc.Editor
{
    [Serializable] public sealed class EndingTestCommand
    {
        public string operation, testId, jobId, resultId;
        public int expectedTestRevision, offset, limit = 100;
        public bool currentCandidate;
        public EndingTestDefinition definition;
    }
    [Serializable] public sealed class EndingTestSummary
    {
        public string id, name;
        public int revision;
    }
    [Serializable] public sealed class EndingTestResponse
    {
        public bool success, stale;
        public string error, jobId, resultId, scope, stopReason, replayStatus;
        public int revision, totalMatches;
        public EndingTestVerdict verdict;
        public ExplorationStatus status;
        public EndingTestDefinition definition;
        public List<EndingTestSummary> tests;
        public List<string> assumptions, unsupportedBoundaries;
        public List<SimulationRecord> path;
        public EndingPathReplay replay;
        public AnalysisManifest manifest;
    }
    public static class EndingTestCommands
    {
        public static bool Handles(string operation) => new[] { "CreateEndingTest", "ReadEndingTest", "UpdateEndingTest", "ListEndingTests", "ValidateEndingTest", "RunEndingTest", "GetEndingTestResult", "PreserveEndingTestResult", "GetEndingTestPath", "ReplayEndingTest", "GetEndingReplay", "CancelEndingReplay" }.Contains(operation);
        public static string Execute(GameProjectAsset project, string json)
        {
            var response = new EndingTestResponse();
            try
            {
                var command = JsonUtility.FromJson<EndingTestCommand>(json);
                if (!Handles(command.operation)) throw new ArgumentException("Unknown ending test operation.");
                if (command.operation == "ListEndingTests")
                    response.tests = EndingTests.List(project).Select(t => new EndingTestSummary { id = t.Read().id, name = t.Read().name, revision = t.Revision }).ToList();
                else if (command.operation == "CreateEndingTest" || command.operation == "ValidateEndingTest")
                {
                    if (command.definition == null) throw new ArgumentException("A test definition is required.");
                    if (command.operation == "ValidateEndingTest") EndingTests.Validate(project, command.definition);
                    else
                    {
                        var saved = EndingTests.Save(project, command.definition, null, command.expectedTestRevision);
                        response.definition = saved.Read(); response.revision = saved.Revision;
                    }
                }
                else if (command.operation == "ReadEndingTest" || command.operation == "UpdateEndingTest" || command.operation == "RunEndingTest")
                {
                    var saved = EndingTests.List(project).SingleOrDefault(t => t.Read().id == command.testId) ?? throw new ArgumentException("Test not found in the selected project.");
                    if (command.operation == "UpdateEndingTest") saved = EndingTests.Save(project, command.definition, saved, command.expectedTestRevision);
                    response.definition = saved.Read(); response.revision = saved.Revision;
                    if (command.operation == "RunEndingTest") response.jobId = EndingTests.Start(project, saved.Read(), saved.Revision).Id;
                }
                else if (command.operation == "ReplayEndingTest") response.jobId = EndingReplayJobs.Start(project, command.resultId, command.currentCandidate).id;
                else if (command.operation == "GetEndingReplay" || command.operation == "CancelEndingReplay")
                {
                    var job = EndingReplayJobs.Find(project, command.jobId);
                    if (command.operation == "CancelEndingReplay") EndingReplayJobs.Cancel(job);
                    response.jobId = job.id; response.replayStatus = job.status; response.error = job.error; response.resultId = job.artifactId;
                    response.replay = new EndingPathReplay { sameStates = job.replay.sameStates, goalReached = job.replay.goalReached,
                        firstDifference = job.replay.firstDifference, reason = job.replay.reason, endingId = job.replay.endingId };
                    response.totalMatches = job.replay.path.Count;
                    response.path = job.replay.path.Skip(Math.Max(0, command.offset)).Take(Math.Max(1, Math.Min(100, command.limit))).ToList();
                }
                else
                {
                    var job = SimulationJobs.Find(command.jobId);
                    if (job.Asset != project) throw new ArgumentException("Job belongs to a different project.");
                    var report = job.Explorer.Report;
                    response.jobId = job.Id; response.status = report.status; response.verdict = EndingTests.Verdict(job);
                    response.manifest = report.manifest; response.scope = report.manifest.scope; response.stopReason = report.manifest.stopReason;
                    response.assumptions = report.manifest.assumptions;
                    response.unsupportedBoundaries = job.Explorer.Records.Where(r => r.result == PlayResult.Unsupported).Select(r => r.reason).Distinct().ToList(); response.stale = SimulationJobs.Stale(job);
                    if (command.operation == "PreserveEndingTestResult") response.resultId = EndingTests.Preserve(job);
                    if (command.operation == "GetEndingTestPath")
                    {
                        var target = EndingTests.TargetRecord(job);
                        var path = target >= 0 ? job.Explorer.Path(target) : new List<SimulationRecord>();
                        response.totalMatches = path.Count; response.path = path.Skip(Math.Max(0, command.offset)).Take(Math.Max(1, Math.Min(command.limit, 100))).ToList();
                    }
                }
                response.success = true;
            }
            catch (Exception ex) { response.error = ex.Message; }
            return JsonUtility.ToJson(response);
        }
    }
}
