using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PrincessStudio.Core
{
    [Serializable]
    public sealed class IntEntry
    {
        public string id; public int value;
    }
    [Serializable]
    public sealed class StateData
    {
        public int day;
        public int period;
        public int periodsPerDay = 1;
        public int remainingActivityPeriods;
        public int money;
        public int revision;
        public string pendingActivityId = "";
        public int remainingActivityDays;
        public uint randomState;
        public List<IntEntry> activityExperience = new List<IntEntry>();
        public int rulesVersion = 1;
        public List<IntEntry> recordBest = new List<IntEntry>();
        public List<IntEntry> recordTotal = new List<IntEntry>();
        public List<IntEntry> recordAttempts = new List<IntEntry>();
        public List<IntEntry> recordPasses = new List<IntEntry>();
        public List<IntEntry> modifierExpiry = new List<IntEntry>();
        public string pendingEventId = "";
        public string presentationStepId = "";
        public List<string> presentationPath = new List<string>();
        public List<IntEntry> actorValues = new List<IntEntry>();
        public string endingId = "";
        public List<IntEntry> stats = new List<IntEntry>();
        public List<IntEntry> relationships = new List<IntEntry>();
        public List<IntEntry> items = new List<IntEntry>();
        public List<IntEntry> flags = new List<IntEntry>();
        public List<string> seenEvents = new List<string>();
    }

    /// <summary>Detached immutable view. Modules never receive the owned game state.</summary>
    public sealed class StateSnapshot
    {
        public string PendingActivityId { get; }
        public int RemainingActivityDays { get; }
        public int Period { get; }
        public int RemainingActivityPeriods { get; }
        public IReadOnlyDictionary<string, int> ActivityExperience { get; }
        public IReadOnlyDictionary<string, int> RecordBest { get; }
        public IReadOnlyDictionary<string, int> RecordTotal { get; }
        public IReadOnlyDictionary<string, int> RecordAttempts { get; }
        public IReadOnlyDictionary<string, int> RecordPasses { get; }
        public IReadOnlyDictionary<string, int> ModifierExpiry { get; }
        public string PresentationStepId { get; }
        private readonly StageState stage;
        public StageState CaptureStage() => stage.Copy();
        public IReadOnlyDictionary<string, int> ActorValues { get; }
        public IReadOnlyDictionary<string, int> ActorAges { get; }
        public int Day
        {
            get;
        }
        public int Money
        {
            get;
        }
        public int Age
        {
            get;
        }
        public int Revision
        {
            get;
        }
        public string PendingEventId
        {
            get;
        }
        public string EndingId
        {
            get;
        }
        public IReadOnlyDictionary<string, int> Stats
        {
            get;
        }
        public IReadOnlyDictionary<string, int> Relationships
        {
            get;
        }
        public IReadOnlyDictionary<string, int> Items
        {
            get;
        }
        public IReadOnlyDictionary<string, int> Flags
        {
            get;
        }
        internal StateSnapshot(GameState state, ProjectDefinition project)
        {
            PendingActivityId = state.PendingActivityId;
            RemainingActivityDays = state.RemainingActivityDays;
            Period = state.Period;
            RemainingActivityPeriods = state.RemainingActivityPeriods;
            ActivityExperience = ReadOnly(state.ActivityExperience);
            RecordBest = ReadOnly(state.RecordBest);
            RecordTotal = ReadOnly(state.RecordTotal);
            RecordAttempts = ReadOnly(state.RecordAttempts);
            RecordPasses = ReadOnly(state.RecordPasses);
            ModifierExpiry = ReadOnly(state.ModifierExpiry);
            Day = state.Day;
            Money = state.Money;
            Revision = state.Revision;
            Age = project.character.startingAge + Day / (project.daysPerMonth * project.monthsPerYear);
            PendingEventId = state.PendingEventId;
            PresentationStepId = state.PresentationStepId;
            stage = EventSequence.Resolve(project.events.Find(x => x.id == state.PendingEventId), state.PresentationPath, state.PresentationStepId);
            ActorValues = ReadOnly(state.ActorValues);
            var ages = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var actor in project.actors)
                ages.Add(actor.id, actor.subjectId == project.character.id ? Age : checked(actor.startingAge + (actor.fixedAge ? 0 : (Day + actor.birthdayOffset) / (project.daysPerMonth * project.monthsPerYear))));
            ActorAges = ReadOnly(ages);
            EndingId = state.EndingId;
            Stats = ReadOnly(state.Stats);
            Relationships = ReadOnly(state.Relationships);
            Items = ReadOnly(state.Items);
            Flags = ReadOnly(state.Flags);
        }
        private static IReadOnlyDictionary<string, int> ReadOnly(Dictionary<string, int> source) =>
            new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(source, StringComparer.Ordinal));
        public int Read(ValueKind kind, string id = "", string actorId = "")
        {
            if (!string.IsNullOrEmpty(actorId))
            {
                if (kind == ValueKind.Age) return Get(ActorAges, actorId);
                if (kind == ValueKind.Stat) return Get(ActorValues, ActorValueKey(actorId, id));
                throw new ArgumentException("Actor values support Age and Stat only.");
            }
            switch (kind)
            {
                case ValueKind.Day:
                    return Day;
                case ValueKind.Age:
                    return Age;
                case ValueKind.Money:
                    return Money;
                case ValueKind.Stat:
                    return Get(Stats, id);
                case ValueKind.Relationship:
                    return Get(Relationships, id);
                case ValueKind.Item:
                    return Get(Items, id);
                case ValueKind.Flag:
                    return Get(Flags, id);
                case ValueKind.RecordBest: return Recorded(RecordBest, id);
                case ValueKind.RecordTotal: return Recorded(RecordTotal, id);
                case ValueKind.RecordAttempts: return Recorded(RecordAttempts, id);
                case ValueKind.RecordPasses: return Recorded(RecordPasses, id);
                case ValueKind.ModifierDays: return Math.Max(0, Recorded(ModifierExpiry, id) - Day);
                default:
                    throw new ArgumentException("Custom values require an extension handler.");
            }
        }
        private static int Get(IReadOnlyDictionary<string, int> map, string id) => map.TryGetValue(id, out var value) ? value : throw new ArgumentException("Unknown value: " + id);
        private static int Recorded(IReadOnlyDictionary<string, int> map, string id) => map.TryGetValue(id, out var value) ? value : 0;
        public static string ActorValueKey(string actorId, string conditionId) => actorId + "/" + conditionId;
    }

    internal sealed class GameState
    {
        internal int Day, Money, Revision;
        internal int Period, PeriodsPerDay = 1, RemainingActivityPeriods;
        internal string PendingActivityId = "";
        internal int RemainingActivityDays;
        internal uint RandomState;
        internal Dictionary<string, int> ActivityExperience = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, int> RecordBest = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, int> RecordTotal = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, int> RecordAttempts = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, int> RecordPasses = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, int> ModifierExpiry = new Dictionary<string, int>(StringComparer.Ordinal);
        internal string PendingEventId = "", EndingId = "";
        internal string PresentationStepId = "";
        internal List<string> PresentationPath = new List<string>();
        internal Dictionary<string, int> ActorValues = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, int> Stats = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, int> Relationships = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, int> Items = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, int> Flags = new Dictionary<string, int>(StringComparer.Ordinal);
        internal HashSet<string> SeenEvents = new HashSet<string>(StringComparer.Ordinal);
        internal GameState Clone() => FromData(ToData());
        internal StateData ToData() => new StateData
        {
            day = Day,
            period = Period, periodsPerDay = PeriodsPerDay, remainingActivityPeriods = RemainingActivityPeriods,
            rulesVersion = PeriodsPerDay > 1 ? 2 : 1,
            money = Money,
            revision = Revision,
            pendingActivityId = PendingActivityId,
            remainingActivityDays = RemainingActivityDays,
            randomState = RandomState,
            activityExperience = Entries(ActivityExperience),
            recordBest = Entries(RecordBest), recordTotal = Entries(RecordTotal),
            recordAttempts = Entries(RecordAttempts), recordPasses = Entries(RecordPasses),
            modifierExpiry = Entries(ModifierExpiry),
            pendingEventId = PendingEventId,
            presentationStepId = PresentationStepId,
            presentationPath = new List<string>(PresentationPath),
            actorValues = Entries(ActorValues),
            endingId = EndingId,
            stats = Entries(Stats),
            relationships = Entries(Relationships),
            items = Entries(Items),
            flags = Entries(Flags),
            seenEvents = new List<string>(SeenEvents)
        };
        internal static GameState FromData(StateData data) => new GameState
        {
            Day = data.day,
            Period = data.period, PeriodsPerDay = data.periodsPerDay, RemainingActivityPeriods = data.remainingActivityPeriods,
            Money = data.money,
            Revision = data.revision,
            PendingActivityId = data.pendingActivityId ?? "",
            RemainingActivityDays = data.remainingActivityDays,
            RandomState = data.randomState,
            ActivityExperience = Map(data.activityExperience ?? new List<IntEntry>()),
            RecordBest = Map(data.recordBest ?? new List<IntEntry>()),
            RecordTotal = Map(data.recordTotal ?? new List<IntEntry>()),
            RecordAttempts = Map(data.recordAttempts ?? new List<IntEntry>()),
            RecordPasses = Map(data.recordPasses ?? new List<IntEntry>()),
            ModifierExpiry = Map(data.modifierExpiry ?? new List<IntEntry>()),
            PendingEventId = data.pendingEventId ?? "",
            PresentationStepId = data.presentationStepId ?? "",
            PresentationPath = new List<string>(data.presentationPath ?? new List<string>()),
            ActorValues = Map(data.actorValues ?? new List<IntEntry>()),
            EndingId = data.endingId ?? "",
            Stats = Map(data.stats),
            Relationships = Map(data.relationships),
            Items = Map(data.items),
            Flags = Map(data.flags),
            SeenEvents = new HashSet<string>(data.seenEvents, StringComparer.Ordinal)
        };
        private static List<IntEntry> Entries(Dictionary<string, int> map)
        {
            var result = new List<IntEntry>(map.Count);
            foreach (var pair in map)
                result.Add(new IntEntry { id = pair.Key, value = pair.Value });
            result.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return result;
        }
        private static Dictionary<string, int> Map(List<IntEntry> entries)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in entries)
                map.Add(e.id, e.value);
            return map;
        }
    }
}
