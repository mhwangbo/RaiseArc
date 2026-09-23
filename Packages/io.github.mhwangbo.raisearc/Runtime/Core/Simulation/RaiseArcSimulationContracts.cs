using System;
using System.Collections.Generic;
using PrincessStudio.Core;

namespace RaiseArc.Analysis
{
    public enum ExplorationMode { QuickCheck, TargetSearch, Exhaustive, MonteCarlo }
    public enum ExplorationStatus { Running, Paused, Completed, Cancelled, BudgetStopped }
    public enum PlayResult { Running, Ending, Death, OtherFailure, Deadlock, RuntimeError, Unsupported, Budget, Cancelled, PlanComplete }
    public enum ReachStatus { NotFound, Reached, ModelOnly, UnreachableInScope, Unverified, OutOfScope }
    [Serializable] public sealed class InputWeight { public string id; public double weight = 1; }
    [Serializable] public sealed class ExplorationSettings
    {
        public ExplorationMode mode;
        public string targetId = "", policy = "uniform-v1";
        public PlayResult targetResult;
        public int seed = 1, runs = 100, maxStates = 10000, maxTransitions = 50000, maxSteps = 1000, horizonDays = 2880;
        public int maxSeconds = 120, maxMemoryMiB = 128, maxCheckpointMiB = 128;
        public double rareRunRate = .01, frequentRunRate = .9;
        public bool allowExternalCode;
        public bool endingGoal;
        public bool useCommittedPlan;
        public List<string> committedActivities = new List<string>();
        public int incomeThreshold = 3, runOffset, gameSeed = 1;
        public bool separateGameSeed;
        public string incomeActivityId = "";
        public List<string> allowedActivities = new List<string>(), forbiddenActivities = new List<string>();
        public List<InputWeight> weights = new List<InputWeight>();
        public List<string> deathEndingIds = new List<string>(), failureEndingIds = new List<string>();
        internal ExplorationSettings Copy()
        {
            var copy = (ExplorationSettings)MemberwiseClone();
            copy.committedActivities = new List<string>(committedActivities);
            copy.weights = weights.ConvertAll(w => new InputWeight { id = w.id, weight = w.weight });
            copy.deathEndingIds = new List<string>(deathEndingIds); copy.failureEndingIds = new List<string>(failureEndingIds);
            copy.allowedActivities = new List<string>(allowedActivities); copy.forbiddenActivities = new List<string>(forbiddenActivities);
            return copy;
        }
    }
    [Serializable] public sealed class AnalysisManifest
    {
        public string format = "raisearc-simulation-1", engine = "simulation-2";
        public string projectId, contentFingerprint, runtimeFingerprint, adapterFingerprint, createdUtc;
        public int revision;
        public int gameDurationDays;
        public StateData initial;
        public ExplorationSettings settings;
        public string scope = "Actual GameSession inputs from the recorded initial state. No injected graph start.";
        public string initialOrigin;
        public string random = "xorshift32-v1 policy/module streams; saved counter-mixer-v1 gameplay stream; seeds sample rather than enumerate chance outcomes";
        public string policyInformation = "Valid actions only; no future-state or ending inspection; ordinal ID tie order; missing weights = 1";
        public string completeness = "Not established", stopReason = "";
        public bool completeWithinScope, hasModels, hasUnsupported;
        public List<string> assumptions = new List<string>(), limitations = new List<string>();
        public List<ModuleAnalysisContract> modules = new List<ModuleAnalysisContract>();
        public List<RuleAnalysisContract> rules = new List<RuleAnalysisContract>();
        public List<ModelDefinition> models = new List<ModelDefinition>();
    }
    [Serializable] public sealed class CoverageRow
    {
        public string id, kind, ownerId, nameKey, sourcePath;
        public ReachStatus status;
        public int occurrences, runsReached, opportunities, trueCount, falseCount, unevaluatedCount, attempted, committed;
        public int firstDay = -1, firstRecord = -1;
        public int eligible, selected, priorityLosses;
        public long moneySpent, moneyEarned, transactionMoneyDelta, daysSpent;
        public long dayTotal;
        public string reason = "Not observed in this analysis", evidence = "observed";
    }
    [Serializable] public sealed class SimulationInput
    {
        public string kind, id, moduleId = "", outcomeId = "";
        public string boundary = "";
        public uint randomSeed;
    }
    [Serializable] public sealed class BranchCoverage
    {
        public string id, eventId, sourceId, targetId, port;
        public ReachStatus status;
        public int occurrences, runsReached, firstRecord = -1;
    }
    [Serializable] public sealed class ModuleState { public string id, payload = ""; }
    [Serializable] public sealed class SimulationRecord
    {
        public int parent = -1, depth, run, cursor, recordId;
        public int planCursor;
        public bool inputsReady;
        public StateData state;
        public SimulationInput input;
        public List<SimulationInput> inputs;
        public List<ModuleState> modules = new List<ModuleState>();
        public List<RuntimeObservation> observations = new List<RuntimeObservation>();
        public List<string> assumptions = new List<string>();
        public PlayResult result;
        public string reason = "", eventId = "", nodeId = "";
        public uint policyRandom, moduleRandom;
    }
    [Serializable] public sealed class PlaySummary
    {
        public int run, record, day;
        public string endingId, reason;
        public PlayResult result;
        public uint policySeed, gameplaySeed, moduleSeed;
        public StateData finalState;
        public List<string> reached = new List<string>();
        public int firstMoneyBlockedDay = -1;
        public List<IntEntry> qualificationDays = new List<IntEntry>(), modifierActiveDays = new List<IntEntry>();
    }
    [Serializable] public sealed class StateSample
    {
        public int run, day, age, money;
        public List<IntEntry> stats;
    }
    [Serializable] public sealed class ExplorationReport
    {
        public AnalysisManifest manifest;
        public ExplorationStatus status;
        public int states, transitions, frontier, startedRuns, runningRuns, targetRecord = -1;
        public long retainedBytes;
        public double elapsedSeconds;
        public List<CoverageRow> coverage = new List<CoverageRow>();
        public List<BranchCoverage> branches = new List<BranchCoverage>();
        public List<PlaySummary> plays = new List<PlaySummary>();
        public List<StateSample> samples = new List<StateSample>();
        public List<string> problems = new List<string>();
        public List<ModelComparison> modelComparisons = new List<ModelComparison>();
        public List<BalanceLedgerRow> ledger = new List<BalanceLedgerRow>();
    }
    [Serializable] public sealed class SimulationCheckpoint
    {
        public ExplorationReport report;
        public List<SimulationRecord> records = new List<SimulationRecord>();
        public List<int> frontier = new List<int>();
        public int front, activeRunRecord = -1;
        public int currentRunStart;
        public List<string> currentRunSeen = new List<string>();
        public int firstMoneyBlockedDay = -1;
        public List<IntEntry> qualificationDays = new List<IntEntry>(), modifierActiveDays = new List<IntEntry>();
    }
    public sealed class AnalysisBoundaryException : InvalidOperationException
    {
        public AnalysisBoundaryException(string message) : base(message) { }
    }
    [Serializable] public sealed class ReplayVerification
    {
        public bool matches;
        public int checkedInputs;
        public string detail;
    }
}
