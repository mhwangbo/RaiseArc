using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    [Serializable] public sealed class BalanceCommand
    {
        public string operation, testId, jobId, resultId, otherResultId, candidateGuid, suiteId;
        public int expectedTestRevision, policyIndex, runId, offset, limit = 50;
        public bool currentCandidate;
        public BalanceTestDefinition definition;
        public TestSuiteDefinition suite;
    }
    [Serializable] public sealed class BalanceResponse
    {
        public bool success, stale;
        public string error, jobId, resultId, projectGuid, assetPath, exportPath;
        public int revision, totalMatches, totalDistributions, totalRules;
        public int firstDifference = -1, blockedInput = -1;
        public string endingId, replayContent;
        public ExplorationStatus status;
        public BalanceTestDefinition definition;
        public List<EndingTestSummary> tests;
        public List<BalancePolicyReport> policies;
        public List<AnalysisManifest> manifests;
        public List<BalanceLedgerRow> ledger;
        public List<BalanceDistribution> distributions;
        public List<BalanceRuleSummary> rules;
        public List<CoverageRow> coverage;
        public List<PlaySummary> runs;
        public List<SimulationRecord> path;
        public ReplayVerification replay;
        public BalanceResultComparison comparison;
        public TestSuiteDefinition suite;
        public TestSuiteRun suiteRun;
        public string[] supportedPolicies, supportedMetrics, limitations;
    }
    public static class BalanceCommands
    {
        public static readonly string[] Operations = { "BalanceCapabilities", "CreateBalanceTest", "ReadBalanceTest", "UpdateBalanceTest", "ListBalanceTests", "ValidateBalanceTest", "RunBalanceTest",
            "GetBalanceResult", "GetBalanceMetrics", "GetBalanceCoverage", "GetBalanceRuns", "PauseBalanceTest", "ResumeBalanceTest", "CancelBalanceTest", "CloseBalanceTest",
            "SaveBalanceCheckpoint", "LoadBalanceCheckpoint", "PreserveBalanceResult", "ExportBalanceReport", "GetBalancePath", "ReplayBalanceRun", "CompareBalanceResults",
            "SaveTestSuite", "ReadTestSuite", "ListTestSuites", "RunTestSuite", "GetTestSuiteResult", "CancelTestSuite" };
        public static bool Handles(string op) => Operations.Contains(op);
        public static string Execute(GameProjectAsset p, string json)
        {
            var r = new BalanceResponse();
            try
            {
                var c = JsonUtility.FromJson<BalanceCommand>(json); if (!Handles(c.operation)) throw new ArgumentException("Unknown balance operation.");
                var limit = Math.Max(1, Math.Min(100, c.limit)); var offset = Math.Max(0, c.offset);
                r.projectGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)); r.assetPath = AssetDatabase.GetAssetPath(p);
                RaiseArcBalanceTestAsset Test() => BalanceTests.List(p).SingleOrDefault(t => t.Read().id == c.testId) ?? throw new ArgumentException("Saved balance test not found in selected project asset.");
                RaiseArcTestSuiteAsset Suite() => TestSuites.List(p).SingleOrDefault(t => t.Read().id == c.suiteId) ?? throw new ArgumentException("Saved suite not found.");
                BalanceJob Job() { var j = BalanceTests.Find(c.jobId); if (j.Asset != p) throw new ArgumentException("Job belongs to another project asset."); return j; }
                BalanceArtifact Result(string id) { var a = BalanceTests.ReadResult(id); if (a.source.id != p.Read().id) throw new ArgumentException("Result belongs to another game."); return a; }
                switch (c.operation)
                {
                    case "BalanceCapabilities":
                        r.supportedPolicies = new[] { "uniform-v1", "weighted-v1", "income-first-v1" }; r.supportedMetrics = Enum.GetNames(typeof(BalanceMetric));
                        r.limitations = new[] { "Authored policies, not predictions of human behavior", "GameSession activity-selection inputs; no arbitrary Scene restore or full schedule cancellation search", "Wilson 95% fixed sample; missing outcomes/optional stopping remain inconclusive", "Models remain hypothetical; unknown custom internals have no invented source attribution" }; break;
                    case "ListBalanceTests": r.tests = BalanceTests.List(p).Select(t => new EndingTestSummary { id = t.Read().id, name = t.Read().name, revision = t.Revision }).ToList(); break;
                    case "CreateBalanceTest": case "UpdateBalanceTest":
                        var saved = BalanceTests.Save(p, c.definition, c.operation == "CreateBalanceTest" ? null : Test(), c.expectedTestRevision); r.definition = saved.Read(); r.revision = saved.Revision; break;
                    case "ReadBalanceTest": r.definition = Test().Read(); r.revision = Test().Revision; break;
                    case "ValidateBalanceTest": BalanceTests.Validate(p, c.definition); break;
                    case "RunBalanceTest":
                        var candidate = string.IsNullOrEmpty(c.candidateGuid) ? p : AssetDatabase.LoadAssetAtPath<GameProjectAsset>(AssetDatabase.GUIDToAssetPath(c.candidateGuid));
                        if (candidate == null) throw new ArgumentException("Candidate GUID not found.");
                        var started = BalanceTests.Start(Test(), candidate); r.jobId = started.Id; r.projectGuid = started.Data.projectGuid; r.assetPath = AssetDatabase.GetAssetPath(candidate); break;
                    case "PauseBalanceTest": BalanceTests.Pause(Job()); break;
                    case "ResumeBalanceTest": BalanceTests.Resume(Job()); break;
                    case "CancelBalanceTest": BalanceTests.Cancel(Job()); break;
                    case "CloseBalanceTest": BalanceTests.Forget(Job()); break;
                    case "SaveBalanceCheckpoint": case "PreserveBalanceResult": r.resultId = BalanceTests.Preserve(Job()); break;
                    case "LoadBalanceCheckpoint": r.jobId = BalanceTests.Load(p, c.resultId).Id; break;
                    case "GetBalanceResult":
                        var job = Job(); r.status = job.Data.status; r.stale = BalanceTests.Stale(job); r.definition = job.Data.definition; r.revision = job.Data.testRevision;
                        r.policies = job.Reports(false).ToList(); r.manifests = job.Explorers.Select(e => e.Report.manifest).ToList(); r.jobId = job.Id; break;
                    case "GetBalanceMetrics": case "GetBalanceCoverage": case "GetBalanceRuns":
                        var current = Job(); if (c.policyIndex < 0 || c.policyIndex >= current.Explorers.Count) throw new ArgumentException("Unknown policy index.");
                        var report = current.Explorers[c.policyIndex].Report;
                        if (c.operation == "GetBalanceRuns") { r.totalMatches = report.plays.Count; r.runs = report.plays.Skip(offset).Take(limit).ToList(); }
                        else if (c.operation == "GetBalanceCoverage") { r.totalMatches = report.coverage.Count; r.coverage = report.coverage.Skip(offset).Take(limit).ToList(); }
                        else { r.totalMatches = report.ledger.Count; r.ledger = report.ledger.Skip(offset).Take(limit).ToList(); var detail = BalanceReports.Build(current.Data.definition, current.Data.definition.policies[c.policyIndex], report);
                            r.totalDistributions = detail.distributions.Count; r.distributions = detail.distributions.Skip(offset).Take(limit).ToList();
                            r.totalRules = detail.rules.Count; r.rules = detail.rules.Skip(offset).Take(limit).ToList(); }
                        break;
                    case "GetBalancePath": case "ReplayBalanceRun":
                        var data = Result(c.resultId);
                        if (c.operation == "GetBalancePath") { var path = BalanceTests.RunPath(data, c.policyIndex, c.runId); r.totalMatches = path.Count; r.path = path.Skip(offset).Take(limit).ToList(); }
                        else { var replay = BalanceTests.Replay(data, c.policyIndex, c.runId, c.currentCandidate ? p : null); r.replay = new ReplayVerification { matches = replay.sameStates, checkedInputs = replay.path.Count - 1, detail = replay.sameStates ? "Actual inputs reproduced original states; model assumptions remain" : replay.reason };
                            r.firstDifference = replay.firstDifference; r.blockedInput = replay.path.FindIndex(x => x.result == PlayResult.RuntimeError || x.result == PlayResult.Unsupported || x.result == PlayResult.Deadlock);
                            r.endingId = replay.endingId; r.replayContent = c.currentCandidate ? SimulationJobs.Fingerprint(p) : data.policies[c.policyIndex].report.manifest.contentFingerprint;
                            r.totalMatches = replay.path.Count; r.path = replay.path.Skip(offset).Take(limit).ToList(); }
                        break;
                    case "CompareBalanceResults":
                        Result(c.resultId); Result(c.otherResultId); r.comparison = BalanceTests.Compare(c.resultId, c.otherResultId);
                        r.totalMatches = Math.Max(r.comparison.beforeLedger.Count, r.comparison.afterLedger.Count);
                        r.comparison.beforeLedger = r.comparison.beforeLedger.Skip(offset).Take(limit).ToList(); r.comparison.afterLedger = r.comparison.afterLedger.Skip(offset).Take(limit).ToList();
                        foreach (var summary in r.comparison.before.Concat(r.comparison.after))
                        { r.totalDistributions = Math.Max(r.totalDistributions, summary.distributions.Count); r.totalRules = Math.Max(r.totalRules, summary.rules.Count); summary.distributions = summary.distributions.Skip(offset).Take(limit).ToList(); summary.rules = summary.rules.Skip(offset).Take(limit).ToList(); }
                        break;
                    case "ExportBalanceReport":
                        var export = Job(); r.resultId = BalanceTests.Preserve(export); r.exportPath = Path.GetFullPath(BalanceTests.ArtifactPath(r.resultId)); break;
                    case "ListTestSuites": r.tests = TestSuites.List(p).Select(t => new EndingTestSummary { id = t.Read().id, name = t.Read().name, revision = t.Revision }).ToList(); break;
                    case "SaveTestSuite":
                        var suite = TestSuites.Save(p, c.suite, string.IsNullOrEmpty(c.suiteId) ? null : Suite(), c.expectedTestRevision); r.suite = suite.Read(); r.revision = suite.Revision; break;
                    case "ReadTestSuite": r.suite = Suite().Read(); r.revision = Suite().Revision; break;
                    case "RunTestSuite": r.jobId = TestSuites.Start(Suite()).id; break;
                    case "GetTestSuiteResult": r.suiteRun = TestSuites.Find(c.jobId, p); break;
                    case "CancelTestSuite": TestSuites.Cancel(TestSuites.Find(c.jobId, p)); break;
                }
                r.success = true;
            }
            catch (Exception e) { r.error = e.Message; }
            return JsonUtility.ToJson(r);
        }
    }
}
