using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEngine;

namespace RaiseArc.Unity
{
    public enum SessionWait { Ready, Event, Module, Paused, ActivityBlocked, PeriodExceeded, Ended, PlanInterrupted }

    /// <summary>Own one per game, share it between views. Mutate through this host while a schedule is active.</summary>
    public sealed class RaiseArcSessionHost
    {
        private readonly ProjectDefinition project;
        private readonly ExtensionRegistry extensions;
        private readonly SaveStore saves;
        private readonly ScheduleSave schedule;
        private readonly PlanSave plan;
        private readonly List<EventDefinition> playbackEvents;
        private bool changing;
        private CancellationTokenSource runningModule;
        public GameSession Session { get; private set; }
        public StateSnapshot State => Session.State;
        public ModuleContext Module { get; private set; }
        public SessionWait Wait { get; private set; }
        public RaiseArc.Core.ActivityAvailability BlockedActivity { get; private set; }
        public int ScheduleCursor => schedule.data.cursor;
        public IReadOnlyList<string> Schedule => schedule.data.ids.AsReadOnly();
        public RaiseArc.Core.GamePlan Plan => plan.data.Copy();
        public int PeriodsPerDay => project.time.PeriodsPerDay;
        public string PeriodName(int index, string locale) => project.time.periodNameKeys.Count == 0 ? Text("ui.day", locale) : Text(project.time.periodNameKeys[index], locale);
        public int ActivityPeriods(string id) => project.time.Duration(project.activities.Find(a => a.id == id) ?? throw new ArgumentException("Unknown activity."));
        public int PlanOwnerSlot(int slot) => plan.data.OwnerSlot(project, slot);
        public bool CanSave => !Session.HasPendingModule && !changing;
        public string SaveBlockedReason => Session.HasPendingModule ? "Finish or cancel the module before saving." : changing ? "An action is in progress." : "";
        public event Action Changed;
        public RaiseArcSessionHost(GameProjectAsset asset, string saveDirectory, uint seed)
            : this(asset.Read(), saveDirectory, seed, asset.CreateExtensions()) { }

        public RaiseArcSessionHost(ProjectDefinition definition, string saveDirectory, uint seed, ExtensionRegistry extensions = null)
        {
            project = new UnityProjectCodec().Clone(definition);
            this.extensions = extensions;
            playbackEvents = project.events.Select(x => RaiseArc.Core.RaiseArcFlowReuse.Expand(project, x)).ToList();
            Session = GameFactory.Create(project, seed, extensions);
            schedule = new ScheduleSave(project, () => State);
            saves = new SaveStore(saveDirectory, project);
            saves.Register(schedule);
            plan = new PlanSave(project, () => State, () => schedule.data);
            plan.data = RaiseArc.Core.GamePlan.New(project, State);
            if (project.time.periodNameKeys.Count > 0) saves.Register(plan);
        }

        public IReadOnlyList<RaiseArc.Core.ActivityInfo> Activities(string category = null) => Session.GetActivities(category);
        public IReadOnlyList<string> Choices => Session.AvailableChoices();
        public PresentationStep CurrentStep
        {
            get
            {
                var step = playbackEvents.Find(x => x.id == State.PendingEventId)?.presentation.Find(x => x.id == State.PresentationStepId);
                return step == null ? null : JsonUtility.FromJson<PresentationStep>(JsonUtility.ToJson(step));
            }
        }
        public IReadOnlyList<ChoiceDefinition> CurrentChoices
        {
            get
            {
                var flow = playbackEvents.Find(x => x.id == State.PendingEventId);
                var choices = CurrentStep?.choices ?? flow?.choices ?? new List<ChoiceDefinition>();
                var available = new HashSet<string>(Session.AvailableChoices());
                return choices.Where(x => available.Contains(x.id)).Select(x => JsonUtility.FromJson<ChoiceDefinition>(JsonUtility.ToJson(x))).ToList();
            }
        }
        public string Text(string key, string locale = null)
        {
            var value = project.translations.Find(x => x.key == key && x.locale == (locale ?? project.defaultLocale));
            if (value == null || string.IsNullOrEmpty(value.text)) value = project.translations.Find(x => x.key == key && x.locale == project.fallbackLocale);
            return value == null || string.IsNullOrEmpty(value.text) ? key : value.text;
        }

        public void StartActivity(string id) => StartSchedule(new[] { id }, project.durationDays - State.Day);
        public void StartSchedule(IReadOnlyList<string> ids, int periodDays)
        {
            Change(() => {
                if (plan.data.confirmed) throw new InvalidOperationException("A confirmed plan owns progression. Resume that plan.");
                if (schedule.data.cursor < schedule.data.ids.Count || State.PendingEventId.Length > 0 || Session.HasPendingModule)
                    throw new InvalidOperationException("Finish or clear the current schedule before starting another.");
                var next = new ScheduleData { ids = ids == null ? null : new List<string>(ids), periodDays = periodDays, startDay = State.Day };
                schedule.Check(next);
                schedule.data = next;
                Pump();
            });
        }

        public void PlacePlanActivity(int slot, string id) => Change(() => { plan.data = plan.data.Place(project, slot, id); });
        public void CopyPlanDay(int sourceDay, int targetDay) => Change(() => { plan.data = CopiedPlan(sourceDay, targetDay); });
        private RaiseArc.Core.GamePlan CopiedPlan(int sourceDay, int targetDay)
        {
            var n = PeriodsPerDay; var next = plan.data.Copy();
            if (sourceDay < 0 || targetDay < 0 || (sourceDay + 1) * n > next.capacity || (targetDay + 1) * n > next.capacity) throw new ArgumentException("Day is outside this plan.");
            var source = next.entries.Where(e => e.slot >= sourceDay * n && e.slot < (sourceDay + 1) * n).ToList();
            if (next.entries.Any(e => e.slot < sourceDay * n && e.slot + ActivityPeriods(e.activityId) > sourceDay * n) || source.Any(e => e.slot + ActivityPeriods(e.activityId) > (sourceDay + 1) * n))
                throw new ArgumentException("Copy whole activities; this day includes an activity spanning another day.");
            if (next.entries.Any(e => e.slot < targetDay * n && e.slot + ActivityPeriods(e.activityId) > targetDay * n || e.slot < (targetDay + 1) * n && e.slot + ActivityPeriods(e.activityId) > (targetDay + 1) * n))
                throw new ArgumentException("The destination contains an activity spanning another day.");
            for (var i = 0; i < n; i++) next = next.Place(project, targetDay * n + i, "");
            foreach (var e in source) next = next.Place(project, targetDay * n + e.slot % n, e.activityId);
            return next;
        }
        public void ConfirmPlan() => Change(() =>
        {
            ValidatePlanConfirmation();
            var entries = plan.data.entries.OrderBy(e => e.slot).ToList();
            schedule.data = new ScheduleData { ids = entries.Select(e => e.activityId).ToList(), startDay = State.Day, startPeriod = State.Period,
                periodDays = project.time.planningDays, periodBudget = plan.data.capacity, periodPlan = true, paused = true };
            plan.data.confirmed = true; RefreshWait();
        });
        public string PlanPlacementProblem(int slot, string id) => PlanProblem(() => plan.data.Place(project, slot, id));
        public string PlanCopyProblem(int sourceDay, int targetDay) => PlanProblem(() => CopiedPlan(sourceDay, targetDay));
        public string PlanConfirmationProblem() => PlanProblem(ValidatePlanConfirmation);
        private static string PlanProblem(Action validate)
        {
            try { validate(); return ""; }
            catch (ArgumentException e) { return e.Message; }
            catch (InvalidOperationException e) { return e.Message; }
        }
        private void ValidatePlanConfirmation()
        {
            if (project.time.periodNameKeys.Count == 0) throw new InvalidOperationException("Configure named time periods before using a slot plan.");
            if (plan.data.confirmed || plan.data.capacity == 0 || project.time.Tick(State) != plan.data.startTick || State.PendingEventId.Length > 0 || Session.HasPendingModule || schedule.data.cursor < schedule.data.ids.Count)
                throw new InvalidOperationException("This draft cannot be confirmed from the current game state.");
            plan.data.Validate(project, true);
        }
        public string NewPlanProblem() => PlanProblem(ValidateNewPlan);
        private void ValidateNewPlan()
        {
            if (State.EndingId.Length > 0 || State.Day >= project.durationDays)
                throw new InvalidOperationException("The game has ended.");
            if (State.PendingEventId.Length > 0 || Session.HasPendingModule || State.PendingActivityId.Length > 0 || schedule.data.cursor < schedule.data.ids.Count)
                throw new InvalidOperationException("Finish the current plan first.");
        }
        public void NewPlan() => Change(() =>
        {
            ValidateNewPlan();
            plan.data = RaiseArc.Core.GamePlan.New(project, State); schedule.data = new ScheduleData(); RefreshWait();
        });

        public void Resume() => Change(() => { schedule.data.paused = false; Pump(); });
        public void AdvanceDialogue(string expectedStepId) => Change(() => { Session.AdvancePresentation(expectedStepId); Pump(); });
        public void Choose(string id) => Change(() => { Session.Choose(id); Pump(); });
        public void CompleteModule(ModuleResult result)
        {
            Change(() => {
                if (result != null && schedule.data.cursor < schedule.data.ids.Count && result.elapsedDays > RemainingPeriod)
                    throw new InvalidOperationException("Module result exceeds the remaining schedule period.");
                if (result != null && schedule.data.periodPlan && result.elapsedDays * PeriodsPerDay != ActivityPeriods(schedule.data.ids[schedule.data.cursor]))
                    throw new InvalidOperationException("A committed plan requires the module to return its reserved duration. Variable-duration module results are not supported in slot plans.");
                Session.CompleteModule(result);
                Module = null;
                schedule.data.started = true;
                Pump();
            });
        }
        public async Task RunModuleAsync(IGameModeModule implementation, CancellationToken cancellationToken)
        {
            var context = Module ?? throw new InvalidOperationException("No module is waiting.");
            if (implementation == null || implementation.Id != context.ModuleId) throw new ArgumentException("The implementation does not match the waiting module.");
            if (runningModule != null) throw new InvalidOperationException("A module implementation is already running.");
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runningModule = lifetime;
            try
            {
                var result = await implementation.ExecuteAsync(context, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                runningModule = null;
                CompleteModule(result);
            }
            catch
            {
                if (Module?.SessionId == context.SessionId) CancelModule(context.SessionId);
                throw;
            }
            finally { if (runningModule == lifetime) runningModule = null; lifetime.Dispose(); }
        }
        // Cancel consumes no time/cost and retains the current item. Resume explicitly retries it.
        public void CancelModule(string sessionId) => Change(() => {
            Session.CancelModule(sessionId); Module = null; schedule.data.paused = true; Wait = SessionWait.Paused;
            runningModule?.Cancel();
        });
        public void SkipCurrent() => Change(() => {
            if (schedule.data.periodPlan) throw new InvalidOperationException("Confirmed slots cannot be skipped or silently replaced.");
            if (Session.HasPendingModule || State.PendingEventId.Length > 0 || State.PendingActivityId.Length > 0 || schedule.data.started)
                throw new InvalidOperationException("Finish the current interaction before skipping.");
            if (schedule.data.cursor < schedule.data.ids.Count) schedule.data.cursor++;
            schedule.data.paused = false; Pump();
        });
        public void ClearSchedule() => Change(() => {
            if (schedule.data.periodPlan) throw new InvalidOperationException("Finish the confirmed plan before creating a new draft.");
            if (Session.HasPendingModule || State.PendingEventId.Length > 0 || State.PendingActivityId.Length > 0)
                throw new InvalidOperationException("Finish the current interaction before clearing the schedule.");
            schedule.data = new ScheduleData(); RefreshWait();
            plan.data = RaiseArc.Core.GamePlan.New(project, State);
        });
        public void Save(string slot)
        {
            if (!CanSave) throw new InvalidOperationException(SaveBlockedReason);
            saves.Save(slot, Session);
        }
        public void RegisterSaveParticipant(ISaveParticipant participant) => saves.Register(participant);
        /// <returns>True only when the backup file was used. False is a successful primary-file load.</returns>
        public bool Load(string slot)
        {
            var recovered = false;
            Change(() => { recovered = saves.Load(slot, Session); Module = null; schedule.data.paused = true; RefreshWait(); });
            return recovered;
        }
        public void Restart(uint seed) => Change(() => {
            Session = GameFactory.Create(project, seed, extensions); Module = null;
            schedule.data = new ScheduleData(); RefreshWait();
            plan.data = RaiseArc.Core.GamePlan.New(project, State);
            runningModule?.Cancel();
        });

        private int RemainingPeriod => schedule.data.periodDays - (State.Day - schedule.data.startDay);
        private void Pump()
        {
            while (true)
            {
                if (schedule.data.periodPlan && schedule.data.started && State.PendingEventId.Length == 0 && State.PendingActivityId.Length == 0 && !Session.HasPendingModule)
                {
                    var expected = schedule.data.startDay * PeriodsPerDay + schedule.data.startPeriod + schedule.data.ids.Take(schedule.data.cursor + 1).Sum(ActivityPeriods);
                    if (project.time.Tick(State) != expected) { Wait = SessionWait.PlanInterrupted; return; }
                    schedule.data.cursor++; schedule.data.started = false; schedule.data.paused = true;
                }
                RefreshWait();
                if (Wait != SessionWait.Ready) return;
                if (schedule.data.started) { schedule.data.cursor++; schedule.data.started = false; }
                if (schedule.data.cursor >= schedule.data.ids.Count) return;
                var id = schedule.data.ids[schedule.data.cursor];
                BlockedActivity = Session.DiagnoseActivity(id);
                if (!BlockedActivity.Available) { Wait = SessionWait.ActivityBlocked; return; }
                var activity = Session.GetActivityInfo(id);
                if (schedule.data.periodPlan ? ActivityPeriods(id) > schedule.data.periodBudget - (project.time.Tick(State) - schedule.data.startDay * PeriodsPerDay - schedule.data.startPeriod) : activity.Days > RemainingPeriod)
                { Wait = SessionWait.PeriodExceeded; return; }
                if (!string.IsNullOrEmpty(activity.ModuleId)) { Module = Session.BeginModule(id); Wait = SessionWait.Module; return; }
                Session.PerformActivity(id);
                schedule.data.started = true;
            }
        }
        private void RefreshWait()
        {
            BlockedActivity = default;
            Wait = Session.HasPendingModule ? SessionWait.Module : State.PendingEventId.Length > 0 ? SessionWait.Event :
                State.EndingId.Length > 0 || State.Day >= project.durationDays ? SessionWait.Ended :
                schedule.data.paused ? SessionWait.Paused : SessionWait.Ready;
        }
        private void Change(Action action)
        {
            if (changing) throw new InvalidOperationException("A session action is already in progress.");
            changing = true;
            try { action(); }
            finally { changing = false; Changed?.Invoke(); }
        }

        [Serializable] private sealed class ScheduleData
        {
            public List<string> ids = new List<string>();
            public int cursor, startDay, periodDays;
            public bool started, paused;
            public bool periodPlan;
            public int startPeriod, periodBudget;
        }
        private sealed class ScheduleSave : ISaveParticipant
        {
            private readonly ProjectDefinition project;
            private readonly Func<StateSnapshot> state;
            public ScheduleData data = new ScheduleData();
            public ScheduleSave(ProjectDefinition project, Func<StateSnapshot> state) { this.project = project; this.state = state; }
            public string Id => "raisearc-schedule";
            public int Version => project.time.periodNameKeys.Count > 0 ? 2 : 1;
            public string Capture() => JsonUtility.ToJson(data);
            public void Validate(int version, string payload)
            {
                if (version != Version) throw new ArgumentException("Unsupported schedule save version.");
                Check(JsonUtility.FromJson<ScheduleData>(payload));
            }
            public void Restore(int version, string payload)
            {
                Validate(version, payload);
                var next = JsonUtility.FromJson<ScheduleData>(payload);
                var current = state();
                if (next.ids.Count > 0 && (next.startDay > current.Day || current.Day - next.startDay > next.periodDays) ||
                    current.PendingActivityId.Length > 0 && (!next.started || next.cursor >= next.ids.Count || next.ids[next.cursor] != current.PendingActivityId))
                    throw new ArgumentException("Schedule and game state do not describe the same progress.");
                data = next;
            }
            public void Check(ScheduleData value)
            {
                if (value == null || value.ids == null || value.ids.Count > (value.periodPlan ? project.time.Capacity : 366) || value.cursor < 0 || value.cursor > value.ids.Count ||
                    value.startDay < 0 || value.startDay > project.durationDays || value.periodDays < 0 ||
                    value.ids.Count > 0 && value.periodDays < 1 || value.started && value.cursor == value.ids.Count)
                    throw new ArgumentException("Invalid saved schedule.");
                long days = 0;
                foreach (var id in value.ids)
                {
                    var activity = project.activities.Find(x => x.id == id) ?? throw new ArgumentException("Unknown scheduled activity: " + id);
                    days += activity.days;
                }
                if (value.periodPlan)
                {
                    if (value.startPeriod < 0 || value.startPeriod >= project.time.PeriodsPerDay || value.periodBudget < 1 || value.ids.Sum(id => project.time.Duration(project.activities.Find(a => a.id == id))) != value.periodBudget)
                        throw new ArgumentException("Invalid committed plan duration.");
                }
                else if (days > value.periodDays) throw new ArgumentException("Schedule exceeds its period.");
            }
        }
        private sealed class PlanSave : ISaveParticipant
        {
            private readonly ProjectDefinition project;
            private readonly Func<StateSnapshot> state;
            private readonly Func<ScheduleData> schedule;
            public RaiseArc.Core.GamePlan data;
            public PlanSave(ProjectDefinition p, Func<StateSnapshot> state, Func<ScheduleData> schedule) { project = p; this.state = state; this.schedule = schedule; }
            public string Id => "raisearc-game-plan";
            public int Version => 1;
            public string Capture() => JsonUtility.ToJson(data);
            public void Validate(int version, string payload)
            { if (version != Version) throw new ArgumentException("Unsupported game plan version."); JsonUtility.FromJson<RaiseArc.Core.GamePlan>(payload).Validate(project, false); }
            public void Restore(int version, string payload)
            {
                Validate(version, payload); var next = JsonUtility.FromJson<RaiseArc.Core.GamePlan>(payload);
                var saved = schedule();
                if (next.startTick > project.time.Tick(state()) || !next.confirmed && next.startTick != project.time.Tick(state()) ||
                    next.confirmed && (!saved.periodPlan || next.startTick != saved.startDay * project.time.PeriodsPerDay + saved.startPeriod || next.capacity != saved.periodBudget || !next.entries.OrderBy(e => e.slot).Select(e => e.activityId).SequenceEqual(saved.ids)))
                    throw new ArgumentException("Plan and schedule do not match.");
                data = next;
            }
        }
    }
}
