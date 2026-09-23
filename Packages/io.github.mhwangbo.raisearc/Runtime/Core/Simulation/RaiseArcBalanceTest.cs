using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Analysis
{
    [Serializable] public sealed class BalancePolicy
    {
        public int version = 1, runs = 100, incomeThreshold = 3;
        public string id = "random", name = "Random", kind = "uniform-v1", incomeActivityId = "";
        public List<InputWeight> weights = new List<InputWeight>();
    }
    public enum BalanceMetric { EndingRate, DeathBeforeDay, MoneyBlockedBeforeDay, StatAtDay, EventReached }
    public enum BalanceComparison { AtLeast, AtMost }
    public enum BalanceVerdict { NoCriteria, Pending, Pass, Fail, Inconclusive }
    [Serializable] public sealed class BalanceCriterion
    {
        public string id = "criterion", policyId = "random", targetId = "";
        public BalanceMetric metric;
        public BalanceComparison comparison;
        public int day = 30, value;
        public double rate = .5;
    }
    [Serializable] public sealed class BalanceTestDefinition
    {
        public int formatVersion = 1, policyVersion = 1, resultVersion = 1;
        public string id = "", name = "Balance test", projectId = "", start = "new-game";
        public bool overrideMoney;
        public int money, policySeed = 1, gameSeed = 1;
        public List<IntEntry> stats = new List<IntEntry>();
        public ExplorationSettings settings = new ExplorationSettings { mode = ExplorationMode.MonteCarlo };
        public List<BalancePolicy> policies = new List<BalancePolicy> { new BalancePolicy() };
        public List<BalanceCriterion> criteria = new List<BalanceCriterion>();
        public List<string> metrics = new List<string> { "economy", "growth", "content", "rules", "dates" };
        public StateData CreateInitial(ProjectDefinition p, IProjectCodec codec)
        {
            Validate(p);
            return AnalysisStartingConditions.Create(p, codec, overrideMoney, money, stats, gameSeed);
        }
        public ExplorationSettings PolicySettings(BalancePolicy p)
        {
            var s = settings.Copy(); s.mode = ExplorationMode.MonteCarlo; s.endingGoal = false; s.targetId = "";
            s.runs = p.runs; s.runOffset = 0; s.policy = p.kind; s.seed = policySeed; s.gameSeed = gameSeed; s.separateGameSeed = true;
            s.incomeActivityId = p.incomeActivityId; s.incomeThreshold = p.incomeThreshold;
            s.weights = p.weights.ConvertAll(w => new InputWeight { id = w.id, weight = w.weight }); return s;
        }
        public void Validate(ProjectDefinition p)
        {
            if (formatVersion != 1 || policyVersion != 1 || resultVersion != 1 || start != "new-game") throw new ArgumentException("Unsupported balance definition version/start.");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || p.id != projectId) throw new ArgumentException("Test identity/name/project mismatch.");
            if (settings == null || policies == null || policies.Count < 1 || policies.Count > 8 || policies.Any(x => x == null) || policies.Select(x => x.id).Distinct().Count() != policies.Count)
                throw new ArgumentException("Provide one to eight uniquely named policies.");
            foreach (var policy in policies)
            {
                if (policy.version != 1 || string.IsNullOrWhiteSpace(policy.id) || string.IsNullOrWhiteSpace(policy.name) || policy.runs < 1 || policy.runs > 100000)
                    throw new ArgumentException("Invalid policy identity/version/run count.");
                foreach (var w in policy.weights)
                    if (!new ContentIndex(p).Data.entries.Any(e => e.id == w.id)) throw new ArgumentException("Unknown weighted input: " + w.id);
            }
            if (metrics.Any(x => !new[] { "economy", "growth", "content", "rules", "dates" }.Contains(x))) throw new ArgumentException("Unknown report metric.");
            if (criteria.Select(c => c.id).Distinct().Count() != criteria.Count) throw new ArgumentException("Criterion IDs must be unique.");
            foreach (var c in criteria)
            {
                if (string.IsNullOrWhiteSpace(c.id) || !policies.Any(x => x.id == c.policyId) || !Enum.IsDefined(typeof(BalanceMetric), c.metric) ||
                    !Enum.IsDefined(typeof(BalanceComparison), c.comparison) || double.IsNaN(c.rate) || c.rate < 0 || c.rate > 1 || c.day < 0)
                    throw new ArgumentException("Invalid creator criterion.");
                if (c.metric == BalanceMetric.EndingRate && !p.endings.Any(x => x.id == c.targetId) || c.metric == BalanceMetric.StatAtDay && !p.stats.Any(x => x.id == c.targetId) ||
                    c.metric == BalanceMetric.EventReached && !p.events.Any(x => x.id == c.targetId)) throw new ArgumentException("Criterion target is missing: " + c.targetId);
            }
            if (settings.deathEndingIds.Any(id => !p.endings.Any(e => e.id == id))) throw new ArgumentException("Unknown explicit death ending ID.");
        }
    }
    [Serializable] public sealed class BalanceCriterionResult
    {
        public string id, policyId, reason;
        public BalanceVerdict verdict;
        public RateEstimate estimate;
    }
    [Serializable] public sealed class BalancePolicyReport
    {
        public string policyId, name;
        public int requested, notStarted, started, finished, running;
        public ExplorationStatus status;
        public List<BalanceOutcomeCount> outcomes = new List<BalanceOutcomeCount>();
        public List<BalanceCriterionResult> criteria = new List<BalanceCriterionResult>();
        public List<BalanceDistribution> distributions = new List<BalanceDistribution>();
        public List<BalanceRuleSummary> rules = new List<BalanceRuleSummary>();
    }
    [Serializable] public sealed class BalanceRuleSummary
    {
        public string kind, id, population;
        public int n;
        public long attempts, passes, total;
        public Distribution values;
    }
    [Serializable] public sealed class BalanceOutcomeCount
    {
        public PlayResult result;
        public string endingId;
        public int count, representativeRun;
        public RateEstimate allStarted;
        public int completedEndingDenominator;
        public double amongCompletedEndings;
    }
    [Serializable] public sealed class BalanceDistribution
    {
        public string population, resource, endingId = "";
        public int day = -1;
        public Distribution distribution;
    }
    public static class BalanceReports
    {
        public static bool Terminal(PlaySummary p) => p.result == PlayResult.Ending || p.result == PlayResult.Death || p.result == PlayResult.OtherFailure;
        public static BalancePolicyReport Build(BalanceTestDefinition definition, BalancePolicy policy, ExplorationReport r, bool distributions = true)
        {
            var result = new BalancePolicyReport { policyId = policy.id, name = policy.name, requested = policy.runs,
                notStarted = policy.runs - r.startedRuns, started = r.startedRuns, finished = r.plays.Count, running = r.runningRuns, status = r.status };
            var terminal = r.plays.Count(Terminal);
            var complete = r.status == ExplorationStatus.Completed && r.startedRuns == policy.runs && r.plays.Count == policy.runs && !r.manifest.hasModels && !r.manifest.hasUnsupported;
            foreach (var group in r.plays.GroupBy(p => new { p.result, p.endingId }))
            {
                var rate = BalanceStatistics.Wilson(group.Count(), r.startedRuns); rate.fixedSampleComplete = complete;
                rate.unknown = r.runningRuns + r.plays.Count(p => !Terminal(p));
                result.outcomes.Add(new BalanceOutcomeCount { result = group.Key.result, endingId = group.Key.endingId,
                    count = group.Count(), representativeRun = group.First().run, allStarted = rate, completedEndingDenominator = terminal,
                    amongCompletedEndings = terminal > 0 && Terminal(group.First()) ? (double)group.Count() / terminal : 0 });
            }
            var daySamples = definition.criteria.Any(c => c.policyId == policy.id && c.metric == BalanceMetric.StatAtDay)
                ? r.samples.ToDictionary(s => (s.run, s.day)) : null;
            foreach (var c in definition.criteria.Where(c => c.policyId == policy.id))
            {
                int observed = 0, known = 0;
                foreach (var p in r.plays)
                {
                    bool yes = false, evaluated = Terminal(p);
                    switch (c.metric)
                    {
                        case BalanceMetric.EndingRate: yes = Terminal(p) && p.endingId == c.targetId; break;
                        case BalanceMetric.DeathBeforeDay: yes = p.result == PlayResult.Death && p.day <= c.day; evaluated |= p.day > c.day; break;
                        case BalanceMetric.MoneyBlockedBeforeDay: yes = p.firstMoneyBlockedDay >= 0 && p.firstMoneyBlockedDay <= c.day; evaluated |= yes || p.day > c.day; break;
                        case BalanceMetric.EventReached: yes = p.reached.Contains(c.targetId); evaluated |= yes; break;
                        case BalanceMetric.StatAtDay:
                            daySamples.TryGetValue((p.run, c.day), out var sample);
                            evaluated = sample != null; yes = sample?.stats.Find(s => s.id == c.targetId)?.value >= c.value; break;
                    }
                    if (evaluated) known++; if (yes) observed++;
                }
                var estimate = BalanceStatistics.Wilson(observed, r.startedRuns); estimate.unknown = r.startedRuns - known; estimate.fixedSampleComplete = complete && known == r.startedRuns;
                var verdict = BalanceVerdict.Inconclusive;
                if (estimate.fixedSampleComplete)
                    verdict = c.comparison == BalanceComparison.AtLeast
                        ? estimate.lower >= c.rate ? BalanceVerdict.Pass : estimate.upper < c.rate ? BalanceVerdict.Fail : BalanceVerdict.Inconclusive
                        : estimate.upper <= c.rate ? BalanceVerdict.Pass : estimate.lower > c.rate ? BalanceVerdict.Fail : BalanceVerdict.Inconclusive;
                result.criteria.Add(new BalanceCriterionResult { id = c.id, policyId = policy.id, estimate = estimate, verdict = verdict,
                    reason = estimate.fixedSampleComplete ? "Fixed sample Wilson interval compared with creator threshold; crossing interval remains inconclusive."
                        : "Incomplete/unevaluated outcomes or unfinished fixed sample; observed frequency is not a final game probability." });
            }
            if (distributions)
            {
                foreach (var group in r.plays.Where(p => p.finalState != null).GroupBy(Terminal))
                {
                    var population = group.Key ? "Final" : "Last observation at interruption";
                    foreach (var id in group.SelectMany(p => p.finalState.recordAttempts).Select(x => x.id).Distinct())
                        result.rules.Add(new BalanceRuleSummary { kind = "Evaluation", id = id, population = population, n = group.Count(),
                            attempts = group.Sum(p => (long)(p.finalState.recordAttempts.Find(x => x.id == id)?.value ?? 0)), passes = group.Sum(p => (long)(p.finalState.recordPasses.Find(x => x.id == id)?.value ?? 0)),
                            total = group.Sum(p => (long)(p.finalState.recordTotal.Find(x => x.id == id)?.value ?? 0)), values = BalanceStatistics.Distribution(group.SelectMany(p => p.finalState.recordBest.Where(x => x.id == id).Select(x => x.value))) });
                    foreach (var id in group.SelectMany(p => p.qualificationDays).Select(x => x.id).Distinct())
                        result.rules.Add(new BalanceRuleSummary { kind = "Qualification acquisition day", id = id, population = population, n = group.Count(), values = BalanceStatistics.Distribution(group.SelectMany(p => p.qualificationDays.Where(x => x.id == id).Select(x => x.value))) });
                    foreach (var id in group.SelectMany(p => p.modifierActiveDays).Select(x => x.id).Distinct())
                        result.rules.Add(new BalanceRuleSummary { kind = "Modifier active game days", id = id, population = population, n = group.Count(), values = BalanceStatistics.Distribution(group.Select(p => p.modifierActiveDays.Find(x => x.id == id)?.value ?? 0)) });
                }
                void Add(string population, string ending, int day, IEnumerable<StateSample> samples)
                {
                    var list = samples.ToList();
                    result.distributions.Add(new BalanceDistribution { population = population, endingId = ending, day = day, resource = "money", distribution = BalanceStatistics.Distribution(list.Select(x => x.money)) });
                    foreach (var stat in r.manifest.initial.stats)
                        result.distributions.Add(new BalanceDistribution { population = population, endingId = ending, day = day, resource = stat.id,
                            distribution = BalanceStatistics.Distribution(list.Select(x => x.stats.Find(s => s.id == stat.id).value)) });
                }
                foreach (var day in r.samples.GroupBy(s => s.day)) Add("Reached this day; no interpolation", "", day.Key, day);
                foreach (var group in r.plays.Where(p => p.finalState != null).GroupBy(p => new { terminal = Terminal(p), p.endingId }))
                    Add(group.Key.terminal ? "Final" : "Last observation at interruption", group.Key.endingId, -1,
                        group.Select(p => new StateSample { money = p.finalState.money, stats = p.finalState.stats }));
            }
            return result;
        }
    }
}
