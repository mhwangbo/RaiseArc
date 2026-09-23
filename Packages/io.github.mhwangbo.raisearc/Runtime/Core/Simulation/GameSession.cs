using System;
using System.Collections.Generic;

namespace PrincessStudio.Core
{
    /// <summary>Single-threaded, turn-based state owner. Every action is an atomic transaction.</summary>
    public sealed partial class GameSession
    {
        private readonly ProjectDefinition project;
        private readonly RuleEngine rules;
        private readonly Dictionary<string, ActivityDefinition> activities = new Dictionary<string, ActivityDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ModuleDefinition> modules = new Dictionary<string, ModuleDefinition>(StringComparer.Ordinal);
        private readonly List<EventDefinition> events;
        private readonly List<EndingDefinition> endings;
        private GameState state;
        private StateSnapshot cachedSnapshot;
        private ModuleContext pendingModule;
        public StateSnapshot State => cachedSnapshot ?? (cachedSnapshot = new StateSnapshot(state, project));
        public bool HasPendingModule => pendingModule != null;
        internal bool PendingPresentationIsChoice
        {
            get
            {
                var currentEvent = events.Find(x => x.id == state.PendingEventId);
                var step = currentEvent?.presentation.Find(x => x.id == state.PresentationStepId);
                if (step == null) throw new InvalidOperationException("Pending presentation step is missing.");
                return step.kind == PresentationStepKind.Choice;
            }
        }
        internal RaiseArc.Analysis.RuntimeObservations Observations { get => rules.Observations; set => rules.Observations = value; }

        /// <param name="definition">An owned, frozen copy. Use GameFactory to construct from mutable authoring data.</param>
        public GameSession(ProjectDefinition definition, ExtensionRegistry extensions = null) : this(definition, extensions, 0) { }
        public GameSession(ProjectDefinition definition, ExtensionRegistry extensions, uint randomSeed)
        {
            project = definition ?? throw new ArgumentNullException(nameof(definition));
            var report = ProjectValidator.Validate(project, extensions);
            if (report.HasErrors)
                throw new ArgumentException(report.Summary);
            project = project.WithEvents(project.events.ConvertAll(e => RaiseArc.Core.RaiseArcFlowReuse.Expand(project, e)));
            rules = new RuleEngine(project, extensions);
            foreach (var a in project.activities)
                activities.Add(a.id, a);
            foreach (var m in project.modules)
                modules.Add(m.id, m);
            events = new List<EventDefinition>(project.events);
            events.Sort((a, b) => a.priority != b.priority ? b.priority.CompareTo(a.priority) : string.CompareOrdinal(a.id, b.id));
            endings = new List<EndingDefinition>(project.endings);
            endings.Sort((a, b) => a.priority != b.priority ? b.priority.CompareTo(a.priority) : string.CompareOrdinal(a.id, b.id));
            state = new GameState { Money = project.startingMoney, RandomState = randomSeed, PeriodsPerDay = project.time.PeriodsPerDay };
            foreach (var actor in project.actors)
                foreach (var condition in actor.conditions)
                    state.ActorValues.Add(StateSnapshot.ActorValueKey(actor.id, condition.id), condition.initial);
            foreach (var s in project.stats)
                state.Stats.Add(s.id, s.initial);
            foreach (var n in project.npcs)
                state.Relationships.Add(n.id, n.initial);
            foreach (var i in project.items)
                state.Items.Add(i.id, 0);
            foreach (var f in project.flags)
                state.Flags.Add(f.id, f.initial ? 1 : 0);
        }

        public bool CanPerform(string activityId)
        {
            return DiagnoseActivity(activityId).Available;
        }
        public RaiseArc.Core.ActivityAvailability DiagnoseActivity(string activityId)
        {
            if (string.IsNullOrEmpty(activityId) || !activities.TryGetValue(activityId, out var a))
                return new RaiseArc.Core.ActivityAvailability(RaiseArc.Core.ActivityBlock.UnknownActivity);
            if (pendingModule != null) return new RaiseArc.Core.ActivityAvailability(RaiseArc.Core.ActivityBlock.ModulePending);
            if (state.PendingEventId.Length > 0) return new RaiseArc.Core.ActivityAvailability(RaiseArc.Core.ActivityBlock.EventPending);
            if (state.PendingActivityId.Length > 0) return new RaiseArc.Core.ActivityAvailability(RaiseArc.Core.ActivityBlock.ActivityPending);
            if (state.EndingId.Length > 0 || state.Day >= project.durationDays) return new RaiseArc.Core.ActivityAvailability(RaiseArc.Core.ActivityBlock.GameEnded);
            if (a.cost > state.Money) return new RaiseArc.Core.ActivityAvailability(RaiseArc.Core.ActivityBlock.InsufficientMoney, a.cost, state.Money);
            if (project.time.Duration(a) > (project.durationDays - state.Day) * state.PeriodsPerDay - state.Period)
                return new RaiseArc.Core.ActivityAvailability(RaiseArc.Core.ActivityBlock.InsufficientDays, project.time.Duration(a), (project.durationDays - state.Day) * state.PeriodsPerDay - state.Period);
            if (!rules.Matches(State, a.conditions)) return new RaiseArc.Core.ActivityAvailability(RaiseArc.Core.ActivityBlock.ConditionsNotMet);
            return new RaiseArc.Core.ActivityAvailability(RaiseArc.Core.ActivityBlock.None);
        }
        public void PerformActivity(string activityId)
        {
            RequireActivity(activityId);
            var activity = activities[activityId];
            if (activity.moduleId.Length > 0)
                throw new InvalidOperationException("Use BeginModule for external activities.");
            var next = state.Clone();
            if (activity.periods > 0)
            {
                PayAndApply(next, activity);
                next.PendingActivityId = activity.id;
                next.RemainingActivityPeriods = activity.periods;
            }
            else if (ActivityCheckFrequencyFor(activity.id) == RaiseArc.Core.ActivityCheckFrequency.OncePerDay)
            {
                next.PendingActivityId = activity.id;
                next.RemainingActivityDays = activity.days;
            }
            else
            {
                PayAndApply(next, activity);
                next = Advance(next, activity.days);
            }
            Commit(next);
        }
        public RaiseArc.Core.ActivityCheckFrequency ActivityCheckFrequencyFor(string activityId)
        {
            if (!activities.TryGetValue(activityId, out var activity)) throw new ArgumentException("Unknown activity: " + activityId);
            return activity.checkFrequency == RaiseArc.Core.ActivityCheckFrequency.ProjectDefault
                ? project.defaultActivityCheckFrequency : activity.checkFrequency;
        }
        public RaiseArc.Core.ActivityInfo GetActivityInfo(string activityId)
        {
            if (!activities.TryGetValue(activityId, out var activity)) throw new ArgumentException("Unknown activity: " + activityId);
            var level = ActivityLevelFor(state, activity);
            state.ActivityExperience.TryGetValue(activityId, out var experience);
            return new RaiseArc.Core.ActivityInfo {
                Id = activity.id, NameKey = activity.nameKey, DescriptionKey = activity.descriptionKey, ImageKey = activity.imageKey,
                CategoryId = string.IsNullOrEmpty(activity.categoryId) ? activity.category.ToString() : activity.categoryId,
                ModuleId = activity.moduleId,
                LevelId = level?.id ?? "", LevelNameKey = level?.nameKey ?? "", Level = level == null ? 0 : activity.progression.levels.IndexOf(level) + 1,
                Experience = experience, SuccessPercent = ActivitySuccessPercent(state, activity, level), Days = activity.days,
                CostPerCheck = activity.cost, IncomePerSuccess = activity.income, Available = CanPerform(activityId), CheckFrequency = ActivityCheckFrequencyFor(activityId)
            };
        }
        public IReadOnlyList<RaiseArc.Core.ActivityInfo> GetActivities(string categoryId = null)
        {
            var result = new List<RaiseArc.Core.ActivityInfo>();
            foreach (var activity in project.activities)
            {
                var info = GetActivityInfo(activity.id);
                if (categoryId == null || info.CategoryId == categoryId) result.Add(info);
            }
            return result;
        }
        private GameState ContinueActivityDay(GameState next)
        {
            var activity = activities[next.PendingActivityId];
            if (next.Day >= project.durationDays || next.Money < activity.cost || !rules.Matches(new StateSnapshot(next, project), activity.conditions))
            {
                Observations?.Record("ActivityProgress", activity.id, "Interrupted", next.Day);
                next.PendingActivityId = "";
                next.RemainingActivityDays = 0;
                return next;
            }
            PayAndApply(next, activity);
            if (--next.RemainingActivityDays == 0) next.PendingActivityId = "";
            return Advance(next, 1);
        }
        public void PerformSchedule(IReadOnlyList<string> activityIds, int periodDays)
        {
            if (periodDays < 1 || activityIds == null || activityIds.Count > 366)
                throw new ArgumentException("Invalid schedule.");
            var total = 0;
            foreach (var id in activityIds)
            {
                if (!activities.TryGetValue(id, out var a))
                    throw new ArgumentException("Unknown activity: " + id);
                total = checked(total + a.days);
            }
            if (total > periodDays)
                throw new InvalidOperationException("Schedule exceeds its period.");
            // Schedules intentionally pause for choices/modules; already completed days remain committed.
            foreach (var id in activityIds)
            {
                if (!CanPerform(id) || activities[id].moduleId.Length > 0)
                    break;
                PerformActivity(id);
            }
        }
        public ModuleContext BeginModule(string activityId)
        {
            RequireActivity(activityId);
            var a = activities[activityId];
            if (!modules.ContainsKey(a.moduleId))
                throw new InvalidOperationException("Activity has no registered module definition.");
            pendingModule = new ModuleContext(a.moduleId, a.id, State);
            return pendingModule;
        }
        public void CancelModule(string sessionId)
        {
            if (pendingModule == null || pendingModule.SessionId != sessionId)
                throw new ModuleResultException("Stale module session.");
            pendingModule = null;
        }
        public void CompleteModule(ModuleResult result, IResultMapper mapper = null)
        {
            if (pendingModule == null)
                throw new ModuleResultException("No module is pending.");
            if (result == null || result.sessionId != pendingModule.SessionId)
                throw new ModuleResultException("Stale module result.");
            if (mapper != null)
                result = mapper.Map(pendingModule, result);
            var spec = modules[pendingModule.ModuleId];
            ValidateModuleResult(pendingModule, spec, result);
            var next = state.Clone();
            PayAndApply(next, activities[pendingModule.ActivityId]);
            rules.Apply(next, result.effects, false);
            next = Advance(next, result.elapsedDays);
            Commit(next);
            pendingModule = null;
        }
        private void ValidateModuleResult(ModuleContext context, ModuleDefinition spec, ModuleResult result)
        {
            if (result == null || result.sessionId != context.SessionId || state.Revision != context.State.Revision)
                throw new ModuleResultException("Invalid module session or revision.");
            if (result.elapsedDays < 1 || result.elapsedDays > spec.maximumDays || (long)result.elapsedDays * state.PeriodsPerDay > (long)(project.durationDays - state.Day) * state.PeriodsPerDay - state.Period)
                throw new ModuleResultException("Invalid elapsed time.");
            if (result.effects == null || result.effects.Count > 128)
                throw new ModuleResultException("Invalid effect count.");
            if (!string.IsNullOrEmpty(result.messageKey) && !project.translations.Exists(t => t.key == result.messageKey))
                throw new ModuleResultException("Unknown result localization key.");
            var totals = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var e in result.effects)
            {
                if (e != null && e.randomRange) throw new ModuleResultException("Modules must return resolved numeric effects, not random ranges.");
                if (e != null && !string.IsNullOrEmpty(e.actorId)) throw new ModuleResultException("Actor state is not an allowed module capability.");
                if (e == null || e.operation != EffectOperation.Add && !(e.kind == ValueKind.Flag && e.operation == EffectOperation.Set))
                    throw new ModuleResultException("Module effects must be deltas or flag assignments.");
                bool allowed;
                switch (e.kind)
                {
                    case ValueKind.Stat:
                        allowed = spec.allowedStats.Contains(e.target);
                        break;
                    case ValueKind.Item:
                        allowed = spec.allowedItems.Contains(e.target);
                        break;
                    case ValueKind.Relationship:
                        allowed = spec.allowedNpcs.Contains(e.target);
                        break;
                    case ValueKind.Flag:
                        allowed = spec.allowedFlags.Contains(e.target) && (e.value == 0 || e.value == 1);
                        break;
                    default:
                        allowed = false;
                        break;
                }
                if (!allowed)
                    throw new ModuleResultException("Module has no capability for " + e.kind + ":" + e.target);
                var key = e.kind + ":" + e.target;
                totals.TryGetValue(key, out var total);
                total += Math.Abs((long)e.value);
                totals[key] = total;
                if (total > spec.maximumAbsoluteDelta)
                    throw new ModuleResultException("Aggregate result exceeds module budget.");
            }
        }
        public IReadOnlyList<string> AvailableChoices()
        {
            var result = new List<string>();
            var e = events.Find(x => x.id == state.PendingEventId);
            var step = e?.presentation.Find(x => x.id == state.PresentationStepId);
            if (step != null && step.kind != PresentationStepKind.Choice) return result;
            if (e != null)
                foreach (var c in step == null ? e.choices : step.choices)
                    if (rules.Matches(State, c.conditions))
                        result.Add(c.id);
            return result;
        }
        public bool CanChoose(string choiceId)
        {
            if (pendingModule != null) return false;
            var e = events.Find(x => x.id == state.PendingEventId);
            var step = e?.presentation.Find(x => x.id == state.PresentationStepId);
            if (e == null || step != null && step.kind != PresentationStepKind.Choice) return false;
            var choice = (step == null ? e.choices : step.choices).Find(x => x.id == choiceId);
            return choice != null && rules.Matches(State, choice.conditions);
        }
        public void Choose(string choiceId)
        {
            if (pendingModule != null)
                throw new InvalidOperationException("Module is active.");
            var e = events.Find(x => x.id == state.PendingEventId) ?? throw new InvalidOperationException("No pending event.");
            var step = e.presentation.Find(x => x.id == state.PresentationStepId);
            if (step != null && step.kind != PresentationStepKind.Choice) throw new InvalidOperationException("Advance the dialogue before choosing.");
            var choice = (step == null ? e.choices : step.choices).Find(x => x.id == choiceId) ?? throw new ArgumentException("Unknown choice.");
            if (!rules.Matches(State, choice.conditions))
                throw new InvalidOperationException("Choice conditions not met.");
            var next = state.Clone();
            rules.Apply(next, choice.effects);
            if (step == null) MovePresentation(next, e, EventSequence.End, choice.id, "Choice");
            else MovePresentation(next, e, EventSequence.Next(e, step, choice.nextStepId), choice.id, "Choice");
            Commit(next, choiceId);
        }
        public string CurrentGrowthId()
        {
            GrowthDefinition best = null;
            var snapshot = State;
            foreach (var g in project.growth)
                if (snapshot.Age >= g.minimumAge && rules.Matches(snapshot, g.conditions) &&
                    (best == null || g.priority > best.priority || g.priority == best.priority && string.CompareOrdinal(g.id, best.id) < 0))
                    best = g;
            return best?.id ?? "";
        }
        public StateData Capture()
        {
            if (pendingModule != null)
                throw new InvalidOperationException("Finish or cancel the module before saving.");
            return state.ToData();
        }
        public void Restore(StateData data)
        {
            if (data != null && (data.rulesVersion < 0 || data.rulesVersion > 2)) throw new ArgumentException("Unsupported saved rules version.");
            if (pendingModule != null)
                throw new InvalidOperationException("Module is active.");
            var next = GameState.FromData(data ?? throw new ArgumentNullException(nameof(data)));
            ValidateState(next);
            state = next;
            cachedSnapshot = null;
        }
        private void ValidateState(GameState value)
        {
            if (value.Day < 0 || value.Day > project.durationDays || value.Money < 0 || value.Revision < 0)
                throw new ArgumentException("Invalid saved calendar or money.");
            if (value.PeriodsPerDay != project.time.PeriodsPerDay || value.Period < 0 || value.Period >= value.PeriodsPerDay || value.Day == project.durationDays && value.Period != 0)
                throw new ArgumentException("Saved time periods do not match this game's rules. No automatic conversion is applied.");
            Check(value.Stats, project.stats, s => s.minimum, s => s.maximum);
            Check(value.Relationships, project.npcs, s => s.minimum, s => s.maximum);
            Check(value.Items, project.items, s => 0, s => s.maximum);
            Check(value.Flags, project.flags, s => 0, s => 1);
            RaiseArc.Core.ReusableRules.ValidateState(project, value.ToData());
            ValidatePresentationState(value);
            foreach (var pair in value.ActivityExperience)
                if (pair.Value < 0 || !activities.ContainsKey(pair.Key)) throw new ArgumentException("Invalid saved activity experience.");
            if (value.RemainingActivityDays == 0 && value.RemainingActivityPeriods == 0 && value.PendingActivityId.Length > 0 || value.RemainingActivityDays < 0 || value.RemainingActivityPeriods < 0 || value.RemainingActivityDays > 0 && value.RemainingActivityPeriods > 0)
                throw new ArgumentException("Invalid saved activity progress.");
            if (value.RemainingActivityPeriods > 0 &&
                (!activities.TryGetValue(value.PendingActivityId, out var periodActivity) || periodActivity.periods <= value.RemainingActivityPeriods ||
                 value.RemainingActivityPeriods > (project.durationDays - value.Day) * value.PeriodsPerDay - value.Period || value.PendingEventId.Length == 0))
                throw new ArgumentException("Invalid saved period activity progress.");
            if (value.RemainingActivityDays > 0 &&
                (!activities.TryGetValue(value.PendingActivityId, out var activity) ||
                 ActivityCheckFrequencyFor(activity.id) != RaiseArc.Core.ActivityCheckFrequency.OncePerDay ||
                 activity.moduleId.Length > 0 || value.RemainingActivityDays >= activity.days ||
                 value.RemainingActivityDays > project.durationDays - value.Day || value.PendingEventId.Length == 0))
                throw new ArgumentException("Invalid saved activity progress.");
            if (value.PendingEventId.Length > 0 && !events.Exists(x => x.id == value.PendingEventId && (x.choices.Count > 0 || x.presentation.Count > 0)))
                throw new ArgumentException("Unknown pending event.");
            if (value.EndingId.Length > 0 && (!endings.Exists(x => x.id == value.EndingId) || value.Day < project.durationDays || value.PendingEventId.Length > 0))
                throw new ArgumentException("Invalid saved ending.");
            foreach (var id in value.SeenEvents)
                if (!events.Exists(x => x.id == id))
                    throw new ArgumentException("Unknown seen event.");
        }
        private static void Check<T>(Dictionary<string, int> map, List<T> definitions, Func<T, int> min, Func<T, int> max) where T : Definition
        {
            if (map.Count != definitions.Count)
                throw new ArgumentException("Save content does not match project.");
            foreach (var d in definitions)
                if (!map.TryGetValue(d.id, out var n) || n < min(d) || n > max(d))
                    throw new ArgumentException("Invalid saved value: " + d.id);
        }
        private void RequireActivity(string id)
        {
            var availability = DiagnoseActivity(id);
            if (!availability.Available)
                throw new InvalidOperationException("Activity unavailable: " + id + " (" + availability.Reason + ")");
        }
        private void PayAndApply(GameState next, ActivityDefinition activity)
        {
            var level = ActivityLevelFor(next, activity);
            var percent = ActivitySuccessPercent(next, activity, level);
            var roll = percent <= 0 ? 100 : percent >= 100 ? 0 : RaiseArc.Core.GameplayRandom.Inclusive(ref next.RandomState, 1, 100);
            var success = roll <= percent;
            if (activity.evaluation != null && activity.evaluation.enabled)
            {
                var evaluation = activity.evaluation;
                var score = next.Stats[evaluation.statId];
                success = success && score >= evaluation.passingScore;
                next.RecordAttempts.TryGetValue(activity.id, out var attempts);
                next.RecordBest.TryGetValue(activity.id, out var best);
                next.RecordTotal.TryGetValue(activity.id, out var total);
                next.RecordPasses.TryGetValue(activity.id, out var passes);
                next.RecordBest[activity.id] = attempts == 0 ? score : Math.Max(best, score);
                next.RecordTotal[activity.id] = checked(total + score);
                next.RecordAttempts[activity.id] = checked(attempts + 1);
                next.RecordPasses[activity.id] = checked(passes + (success ? 1 : 0));
                if (success && evaluation.qualificationFlagId.Length > 0)
                    rules.Apply(next, new[] { new EffectSpec { id = activity.id + ".qualification", kind = ValueKind.Flag,
                        target = evaluation.qualificationFlagId, operation = EffectOperation.Set, value = 1 } });
                Observations?.Entries.Add(new RaiseArc.Analysis.RuntimeObservation { kind = "Evaluation", id = activity.id,
                    day = next.Day, actual = score, expected = evaluation.passingScore, hasActual = true, outcome = success ? "Passed" : "Failed" });
            }
            var income = success ? activity.income : 0;
            Observations?.Entries.Add(new RaiseArc.Analysis.RuntimeObservation { kind = "ActivityResult", id = activity.id, day = next.Day,
                outcome = success ? "Success" : "Failure", actual = roll, expected = percent, hasActual = true });
            Observations?.Entries.Add(new RaiseArc.Analysis.RuntimeObservation { kind = "Economy", id = activity.id, day = next.Day, cost = activity.cost, income = income,
                hasDelta = true, before = next.Money, after = checked(next.Money - activity.cost + income), target = "Money",
                requestedDelta = (long)income - activity.cost, appliedDelta = (long)income - activity.cost });
            next.Money = checked(next.Money - activity.cost + income);
            rules.Apply(next, success ? activity.effects : activity.failureEffects, true, activity.id);
            if (success && level != null) rules.Apply(next, level.effects, true, activity.id);
            if (activity.progression != null && activity.progression.enabled)
            {
                next.ActivityExperience.TryGetValue(activity.id, out var experience);
                next.ActivityExperience[activity.id] = checked(experience + (success ? activity.progression.experienceOnSuccess : activity.progression.experienceOnFailure));
            }
        }
        private RaiseArc.Core.ActivityLevel ActivityLevelFor(GameState value, ActivityDefinition activity)
        {
            if (activity.progression == null || !activity.progression.enabled) return null;
            value.ActivityExperience.TryGetValue(activity.id, out var experience);
            var snapshot = new StateSnapshot(value, project);
            RaiseArc.Core.ActivityLevel selected = null;
            foreach (var level in activity.progression.levels)
                if (experience >= level.requiredExperience && rules.Matches(snapshot, level.conditions)) selected = level;
            return selected;
        }
        private int ActivitySuccessPercent(GameState value, ActivityDefinition activity, RaiseArc.Core.ActivityLevel level)
        {
            return ChancePercent(value, activity.successChance, level?.successBonusPercent ?? 0);
        }
        private int ChancePercent(GameState value, RaiseArc.Core.SuccessChance chance, int bonusPercent = 0)
        {
            if (chance == null || !chance.enabled) return 100;
            long percent = chance.basePercent + (long)bonusPercent;
            foreach (var bonus in chance.statBonuses)
                percent += Math.Max(0L, (long)value.Stats[bonus.statId] - bonus.threshold) * bonus.percentPerPoint;
            return (int)Math.Max(0L, Math.Min(100L, percent));
        }
        private GameState Advance(GameState next, int days, bool onlyPeriodEvents = false)
        {
            next.Day = checked(next.Day + days);
            foreach (var id in new List<string>(next.ModifierExpiry.Keys))
                if (next.ModifierExpiry[id] <= next.Day) next.ModifierExpiry.Remove(id);
            var current = new StateSnapshot(next, project);
            foreach (var e in events)
            {
                if (e.callOnly || onlyPeriodEvents && !e.evaluateEachPeriod || e.once && next.SeenEvents.Contains(e.id) || !rules.Matches(current, e.conditions))
                    continue;
                var chance = ChancePercent(next, e.triggerChance);
                var roll = chance <= 0 ? 100 : chance >= 100 ? 0 : RaiseArc.Core.GameplayRandom.Inclusive(ref next.RandomState, 1, 100);
                if (e.triggerChance?.enabled == true)
                    Observations?.Entries.Add(new RaiseArc.Analysis.RuntimeObservation { kind = "EventChance", id = e.id, day = next.Day,
                        expected = chance, actual = roll, hasActual = true, outcome = roll <= chance ? "Triggered" : "Skipped" });
                if (roll > chance) continue;
                Observations?.Record("EventEligibility", e.id, "Eligible", next.Day);
                var candidate = next.Clone();
                var observationStart = Observations?.Entries.Count ?? 0;
                rules.Apply(candidate, e.effects);
                var snapshot = new StateSnapshot(candidate, project);
                if (e.choices.Count > 0 && !e.choices.Exists(c => rules.Matches(snapshot, c.conditions)))
                {
                    if (Observations != null)
                        for (var i = observationStart; i < Observations.Entries.Count; i++)
                            if (Observations.Entries[i].kind == "Effect" || Observations.Entries[i].kind == "ValueChange") Observations.Entries[i].outcome = "Discarded";
                    continue;
                }
                next = candidate;
                Observations?.Record("Event", e.id, "Selected", next.Day);
                next.SeenEvents.Add(e.id);
                current = snapshot;
                if (e.choices.Count > 0 || e.presentation.Count > 0)
                {
                    next.PendingEventId = e.id;
                    next.PresentationPath.Clear();
                    MovePresentation(next, e, EventSequence.Entry(e));
                    break;
                }
                Observations?.Entries.Add(new RaiseArc.Analysis.RuntimeObservation { kind = "Edge", ownerId = e.id, id = e.id, relatedId = EventSequence.End, outcome = "Entry", day = next.Day });
            }
            ResolveEnding(next);
            return next;
        }
        private void ResolveEnding(GameState next)
        {
            if (next.Day < project.durationDays || next.PendingEventId.Length > 0)
                return;
            var snapshot = new StateSnapshot(next, project);
            foreach (var e in endings)
                if (rules.Matches(snapshot, e.conditions))
                {
                    next.EndingId = e.id;
                    Observations?.Record("Ending", e.id, "Selected", next.Day);
                    break;
                }
        }
        private void Commit(GameState next, string choiceId = null)
        {
            var frames = TraceEnabled ? new List<SequenceTraceFrame>() : null;
            if (TraceEnabled)
            {
                var frame = MakeTrace(state.PresentationStepId.Length > 0 ? state.PresentationStepId : next.PendingEventId, state, next);
                frame.choiceId = choiceId; frames.Add(frame);
            }
            while (true)
            {
                for (var budget = 0; next.PresentationStepId.Length > 0; budget++)
                {
                    var ev = events.Find(x => x.id == next.PendingEventId);
                    var step = ev.presentation.Find(x => x.id == next.PresentationStepId);
                    if (!IsAutomatic(step)) break;
                    if (budget >= 512) throw new InvalidOperationException("Automatic sequence budget exceeded.");
                    var before = TraceEnabled ? next.Clone() : null;
                    RunAutomaticStep(next, ev, step);
                    if (TraceEnabled) frames.Add(MakeTrace(step.id, before, next));
                }
                if (next.PendingEventId.Length > 0) break;
                if (next.RemainingActivityPeriods > 0)
                {
                    next.RemainingActivityPeriods--;
                    if (next.RemainingActivityPeriods == 0) next.PendingActivityId = "";
                    next.Period++;
                    if (next.Period == next.PeriodsPerDay) { next.Period = 0; next = Advance(next, 1); }
                    else next = Advance(next, 0, true);
                }
                else if (next.RemainingActivityDays > 0) next = ContinueActivityDay(next);
                else break;
            }
            next.Revision = checked(state.Revision + 1);
            state = next;
            cachedSnapshot = null;
            if (TraceEnabled) { traceFrames.AddRange(frames); if (traceFrames.Count > 1024) traceFrames.RemoveRange(0, traceFrames.Count - 1024); }
        }
    }
}
