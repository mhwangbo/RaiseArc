using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Analysis
{
    [Serializable]
    public sealed class EndingTestDefinition
    {
        public int formatVersion = 1;
        public string id = "", name = "", projectId = "", endingId = "";
        public string start = "new-game";
        public bool overrideMoney;
        public int money;
        public List<IntEntry> stats = new List<IntEntry>();
        public ExplorationSettings settings = new ExplorationSettings { mode = ExplorationMode.TargetSearch, endingGoal = true };

        public ExplorationSettings SearchSettings()
        {
            var value = settings.Copy();
            value.targetId = endingId; value.endingGoal = true;
            return value;
        }

        // Overrides are applied to a detached new-game definition, before GameSession establishes its invariants.
        public StateData CreateInitial(ProjectDefinition source, IProjectCodec codec)
        {
            if (formatVersion != 1 || start != "new-game") throw new ArgumentException("Unsupported ending test version or start mode.");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A test needs a stable ID and name.");
            if (source.id != projectId) throw new ArgumentException("Test belongs to another project.");
            if (!source.endings.Any(e => e.id == endingId)) throw new ArgumentException("Target ending is missing: " + endingId);
            if (settings.mode != ExplorationMode.TargetSearch && settings.mode != ExplorationMode.Exhaustive)
                throw new ArgumentException("Ending tests support fixed-seed target search or exhaustive reachability.");
            return AnalysisStartingConditions.Create(source, codec, overrideMoney, money, stats, settings.seed);
        }
    }

    public static class AnalysisStartingConditions
    {
        public static StateData Create(ProjectDefinition source, IProjectCodec codec, bool overrideMoney, int money, List<IntEntry> stats, int seed)
        {
            var copy = codec.Clone(source);
            if (overrideMoney)
            {
                if (money < 0) throw new ArgumentException("Starting money cannot be negative.");
                copy.startingMoney = money;
            }
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in stats)
            {
                var stat = copy.stats.Find(s => s.id == entry.id);
                if (stat == null || !used.Add(entry.id)) throw new ArgumentException("Missing or duplicated starting stat: " + entry.id);
                if (entry.value < stat.minimum || entry.value > stat.maximum) throw new ArgumentException("Starting stat is outside its bounds: " + entry.id);
                stat.initial = entry.value;
            }
            var initial = new GameSession(copy).Capture();
            initial.randomState = unchecked((uint)seed);
            return initial;
        }
    }

    public enum EndingTestVerdict { Searching, Reached, ReachedUnderModel, NotFound, UnreachableInScope, Unsupported, Cancelled, BudgetStopped, Error }

    [Serializable]
    public sealed class EndingPathReplay
    {
        public int formatVersion = 1, firstDifference = -1;
        public bool sameStates = true, goalReached;
        public string reason = "", endingId = "";
        public List<SimulationRecord> path = new List<SimulationRecord>();
    }
}
