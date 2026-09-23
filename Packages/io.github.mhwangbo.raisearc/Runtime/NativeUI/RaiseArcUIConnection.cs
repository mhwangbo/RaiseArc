using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using RaiseArc.Core;
using RaiseArc.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.UI
{
    // Owns UI selection and subscriptions, never gameplay state or layout.
    public sealed class RaiseArcUIConnection : IDisposable
    {
        private readonly RaiseArcGame game;
        private readonly VisualElement root;
        private readonly RaiseArcUIBindings bindings;
        private readonly List<Action> unsubscribe = new List<Action>();
        private readonly Dictionary<UIRepeat, Dictionary<string, VisualElement>> rows = new Dictionary<UIRepeat, Dictionary<string, VisualElement>>();
        private bool disposed;
        private int selectedSlot;
        private string notice = "";
        public IReadOnlyList<string> Errors { get; }
        public int SelectedSlot => selectedSlot;
        private RaiseArcSessionHost Host => game.Host;
        private string L(string en, string ko) => game.Locale == "ko" ? ko : en;
        private string PlanMessage(string message)
        {
            // Legacy plan validation carries both translations in one diagnostic.
            var separator = message.IndexOf(" / ", StringComparison.Ordinal);
            return separator < 0 ? message : L(message.Substring(0, separator), message.Substring(separator + 3));
        }
        private sealed class Item
        {
            public string id = "";
            public int slot = -1;
            public ActivityInfo activity;
            public ChoiceDefinition choice;
        }
        public RaiseArcUIConnection(RaiseArcGame game, VisualElement root, RaiseArcUIBindings bindings)
        {
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            game.Initialize();
            var errors = bindings.Validate(root);
            if (game.Host == null) errors.Add(game.Error);
            if (bindings.project != null && bindings.project != game.Project) errors.Add("Bindings and host refer to different Game Projects.");
            Errors = errors.AsReadOnly();
            if (errors.Count != 0) return;
            game.Changed += Refresh;
            Refresh();
        }
        public void Refresh()
        {
            if (disposed || Errors.Count != 0) return;
            foreach (var action in unsubscribe) action();
            unsubscribe.Clear();
            var plan = Host.Plan;
            selectedSlot = Mathf.Clamp(selectedSlot, 0, Math.Max(0, plan.capacity - 1));
            root.EnableInClassList("raisearc-event", Host.Wait == SessionWait.Event);
            root.EnableInClassList("raisearc-ended", Host.Wait == SessionWait.Ended);
            root.EnableInClassList("raisearc-confirmed", plan.confirmed);
            root.EnableInClassList("raisearc-running", game.IsRunningPlan);
            root.EnableInClassList("raisearc-choices", Host.CurrentChoices.Count != 0);
            Bind(root, bindings, new Item());
            foreach (var repeat in bindings.repeats) Repeat(repeat);
        }
        private void Repeat(UIRepeat repeat)
        {
            var container = root.Q(repeat.target);
            if (!rows.TryGetValue(repeat, out var retained)) rows[repeat] = retained = new Dictionary<string, VisualElement>();
            var items = new List<Item>();
            if (repeat.items == UIItems.Activities)
            {
                IEnumerable<ActivityInfo> activities = Host.Activities(string.IsNullOrEmpty(repeat.category) ? null : repeat.category);
                if (repeat.order == UIItemOrder.NameAscending) activities = activities.OrderBy(a => game.Text(a.NameKey), StringComparer.Ordinal);
                if (repeat.order == UIItemOrder.NameDescending) activities = activities.OrderByDescending(a => game.Text(a.NameKey), StringComparer.Ordinal);
                items.AddRange(activities.Select(a => new Item { id = a.Id, activity = a }));
            }
            else if (repeat.items == UIItems.PlanSlots)
                for (var i = 0; i < Host.Plan.capacity; i++) items.Add(new Item { id = i.ToString(), slot = i });
            else items.AddRange(Host.CurrentChoices.Select(c => new Item { id = c.id, choice = c }));
            var keys = new HashSet<string>(items.Select(i => i.id));
            foreach (var key in retained.Keys.Where(k => !keys.Contains(k)).ToArray()) { retained[key].RemoveFromHierarchy(); retained.Remove(key); }
            foreach (var item in items)
            {
                if (!retained.TryGetValue(item.id, out var row))
                {
                    row = repeat.template.document.CloneTree();
                    row.AddToClassList("raisearc-item"); row.userData = item.id; retained.Add(item.id, row);
                }
                container.Add(row);
                Bind(row, repeat.template, item);
                if (item.slot >= 0)
                {
                    var plan = Host.Plan; var owner = Host.PlanOwnerSlot(item.slot);
                    var current = plan.entries.OrderBy(e => e.slot).Skip(Host.ScheduleCursor).FirstOrDefault();
                    row.EnableInClassList("raisearc-selected", item.slot == selectedSlot);
                    row.EnableInClassList("raisearc-day-start", (plan.startTick + item.slot) % Host.PeriodsPerDay == 0);
                    row.EnableInClassList("raisearc-placed", owner >= 0);
                    row.EnableInClassList("raisearc-locked", plan.confirmed);
                    row.EnableInClassList("raisearc-current", plan.confirmed && current != null && owner == current.slot && Host.Wait != SessionWait.Ended);
                    row.EnableInClassList("raisearc-problem", owner >= 0 && current?.slot == owner &&
                        (Host.Wait == SessionWait.ActivityBlocked || Host.Wait == SessionWait.PlanInterrupted || Host.Wait == SessionWait.PeriodExceeded));
                }
            }
        }
        private void Bind(VisualElement scope, RaiseArcUIBindings map, Item item)
        {
            foreach (var c in map.connections)
            {
                var element = scope.Q(c.target);
                if (c.data == UIData.EventVisible || c.data == UIData.EndingVisible)
                    SetVisible(element, Host.Wait == (c.data == UIData.EventVisible ? SessionWait.Event : SessionWait.Ended));
                else if (element is Image image)
                {
                    var key = c.data == UIData.CharacterImage ? "screen.character" : c.data == UIData.BackgroundImage ? "screen.background" : item.activity?.ImageKey;
                    if (c.data != UIData.None) image.sprite = game.Image(key);
                }
                else if (element is ProgressBar gauge && c.data == UIData.Stat)
                {
                    var stat = game.Definition.stats.Find(s => s.id == c.contentId);
                    gauge.lowValue = stat.minimum; gauge.highValue = stat.maximum;
                    gauge.SetValueWithoutNotify(Host.State.Stats[c.contentId]);
                }
                else if (element is TextElement text && c.data != UIData.None) text.text = Value(c, item);
                if (c.command == UICommand.None || c.data == UIData.CommandReason) continue;
                var button = (Button)element;
                var problem = Problem(c, item);
                button.SetEnabled(problem.Length == 0); button.tooltip = problem;
                var version = game.Version;
                Action click = () =>
                {
                    if (disposed || version != game.Version) return;
                    try { notice = ""; Execute(c, item); }
                    catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is System.IO.IOException || e is UnauthorizedAccessException)
                    { notice = (c.command == UICommand.Save ? L("Save failed: ", "저장 실패: ") : c.command == UICommand.Load ? L("Load failed: ", "불러오기 실패: ") : "") + e.Message; }
                    Refresh();
                };
                button.clicked += click;
                unsubscribe.Add(() => button.clicked -= click);
            }
        }
        private static void SetVisible(VisualElement element, bool visible)
        {
            element.EnableInClassList("raisearc-hidden", !visible);
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private string Value(UIConnection c, Item item)
        {
            var state = Host.State; var plan = Host.Plan;
            switch (c.data)
            {
                case UIData.LocalizedText: return game.Text(c.contentId);
                case UIData.Money: return state.Money.ToString();
                case UIData.Stat: return state.Stats[c.contentId].ToString();
                case UIData.Day: return Math.Min(state.Day + 1, game.Definition.durationDays).ToString();
                case UIData.ElapsedDays: return state.Day.ToString();
                case UIData.DayPosition: return state.Period > 0
                    ? L("Day " + (state.Day + 1) + " in progress", (state.Day + 1) + "일 진행 중")
                    : state.Day == 0 ? L("Before day 1", "첫날 시작 전")
                    : L("Day " + state.Day + " complete", state.Day + "일 완료");
                case UIData.PlanTargetDate: return (plan.startTick / Host.PeriodsPerDay + 1).ToString();
                case UIData.Period: return Host.PeriodName(state.Period, game.Locale);
                case UIData.CharacterName: return game.Text(game.Definition.character.nameKey);
                case UIData.ActivityName: return game.Text(item.activity?.NameKey ?? "");
                case UIData.ActivityDescription: return game.Text(item.activity?.DescriptionKey ?? "");
                case UIData.ActivityCost: return item.activity?.CostPerCheck.ToString() ?? "";
                case UIData.ActivityDuration: return item.activity == null ? "" : Host.ActivityPeriods(item.id).ToString();
                case UIData.SlotDate: return ((plan.startTick + item.slot) / Host.PeriodsPerDay + 1).ToString();
                case UIData.SlotPeriod: return Host.PeriodName((plan.startTick + item.slot) % Host.PeriodsPerDay, game.Locale);
                case UIData.SlotActivity:
                    var owner = Host.PlanOwnerSlot(item.slot);
                    var entry = plan.entries.Find(e => e.slot == owner);
                    return entry == null ? L("Empty", "빈칸") : game.Text(game.Definition.activities.Find(a => a.id == entry.activityId).nameKey);
                case UIData.PlanStatus: return PlanStatus() + " " +
                    plan.entries.Sum(e => Host.ActivityPeriods(e.activityId)) + "/" + plan.capacity;
                case UIData.Dialogue: return game.Text(Host.CurrentStep?.nameKey ?? game.Definition.events.Find(e => e.id == state.PendingEventId)?.descriptionKey ?? "");
                case UIData.Speaker: return game.Text(game.Definition.actors.Find(a => a.id == Host.CurrentStep?.speakerActorId)?.nameKey ?? "");
                case UIData.ChoiceText: return game.Text(item.choice?.nameKey ?? "");
                case UIData.Ending: return game.Text(game.Definition.endings.Find(e => e.id == state.EndingId)?.nameKey ?? "");
                case UIData.CommandReason:
                    var reason = Problem(c, item);
                    return c.command == UICommand.ConfirmPlan && reason.Length == 0 ? L("Ready to confirm. No time or cost consumed yet.", "확정할 수 있습니다. 아직 시간과 비용은 소비되지 않았습니다.") : reason;
                case UIData.Status: return notice.Length != 0 ? notice : Status();
                default: return "";
            }
        }
        private string Status()
        {
            if (game.IsRunningPlan && Host.Wait == SessionWait.Paused) return L("Running the confirmed plan", "확정된 계획 진행 중");
            switch (Host.Wait)
            {
                case SessionWait.Event: return L("Waiting for dialogue / a choice", "대사·선택 입력 대기");
                case SessionWait.Ended: return L("Game ended", "게임 종료");
                case SessionWait.Module: return L("Waiting for the module", "외부 게임 완료 대기");
                case SessionWait.ActivityBlocked: return L("Activity blocked: ", "활동 실행 불가: ") + Host.BlockedActivity.Reason;
                case SessionWait.PeriodExceeded: return L("Duration exceeds the remaining time", "남은 시간보다 활동이 깁니다");
                case SessionWait.PlanInterrupted: return L("Activity interrupted; plan preserved", "활동 중단 · 계획 유지");
                default: return Host.Plan.confirmed ? PlanStatus() : L("Draft only. No time or cost consumed.", "편성 중 · 시간과 비용은 소비되지 않음");
            }
        }
        private string PlanStatus()
        {
            if (!Host.Plan.confirmed) return L("Draft", "편성 중");
            if (game.IsRunningPlan && Host.Wait == SessionWait.Paused) return L("Running the confirmed plan", "확정된 계획 진행 중");
            if (Host.Wait == SessionWait.Event) return L("Confirmed · Waiting for dialogue / a choice", "확정됨 · 대사·선택 입력 대기");
            if (Host.Wait == SessionWait.Module) return L("Confirmed · Waiting for the module", "확정됨 · 외부 게임 완료 대기");
            if (Host.Wait == SessionWait.ActivityBlocked || Host.Wait == SessionWait.PeriodExceeded || Host.Wait == SessionWait.PlanInterrupted || Host.Wait == SessionWait.Ended)
                return L("Confirmed · ", "확정됨 · ") + Status();
            if (Host.ScheduleCursor >= Host.Schedule.Count) return L("Plan complete", "계획 실행 완료");
            return Host.ScheduleCursor > 0
                ? L("In progress · Run next when ready", "실행 중 · 다음 활동 실행 대기")
                : L("Confirmed · Run next when ready", "확정됨 · 다음 활동을 실행하세요");
        }
        private string Problem(UIConnection c, Item item)
        {
            switch (c.command)
            {
                case UICommand.ConfirmPlan: return Host.Plan.confirmed ? PlanStatus() : PlanMessage(Host.PlanConfirmationProblem());
                case UICommand.PlaceActivity: return item.activity == null ? "An activity item is required." : PlanMessage(Host.PlanPlacementProblem(selectedSlot, item.id));
                case UICommand.ClearSlot: return PlanMessage(Host.PlanPlacementProblem(selectedSlot, ""));
                case UICommand.CopyNextDay: return PlanMessage(Host.PlanCopyProblem(selectedSlot / Host.PeriodsPerDay, selectedSlot / Host.PeriodsPerDay + 1));
                case UICommand.SelectSlot: return item.slot < 0 ? "A plan slot is required." : "";
                case UICommand.RunNext:
                case UICommand.RunPlan:
                    return game.IsRunningPlan || !Host.Plan.confirmed || Host.ScheduleCursor >= Host.Schedule.Count || Host.Wait != SessionWait.Paused
                        ? Status() : "";
                case UICommand.ContinueDialogue: return Host.Wait != SessionWait.Event || Host.CurrentChoices.Count != 0 ? L("No dialogue waiting to continue.", "진행할 대사가 없습니다.") : "";
                case UICommand.Choose: return item.choice == null || !Host.Choices.Contains(item.id) ? L("This choice is no longer available.", "현재 선택할 수 없는 선택지입니다.") : "";
                case UICommand.Save: return Host.CanSave ? "" : Host.SaveBlockedReason;
                case UICommand.NewPlan: return Host.Plan.confirmed ? Host.NewPlanProblem() : L("A draft is already open.", "초안이 이미 열려 있습니다.");
                default: return "";
            }
        }
        private void Execute(UIConnection c, Item item)
        {
            var problem = Problem(c, item);
            if (problem.Length != 0) throw new InvalidOperationException(problem);
            switch (c.command)
            {
                case UICommand.SelectSlot: selectedSlot = item.slot; break;
                case UICommand.PlaceActivity: Host.PlacePlanActivity(selectedSlot, item.id); break;
                case UICommand.ClearSlot: Host.PlacePlanActivity(selectedSlot, ""); break;
                case UICommand.CopyNextDay: Host.CopyPlanDay(selectedSlot / Host.PeriodsPerDay, selectedSlot / Host.PeriodsPerDay + 1); break;
                case UICommand.ConfirmPlan: Host.ConfirmPlan(); break;
                case UICommand.RunNext: Host.Resume(); break;
                case UICommand.RunPlan: game.RunPlan(); break;
                case UICommand.ContinueDialogue: Host.AdvanceDialogue(Host.State.PresentationStepId); break;
                case UICommand.Choose: Host.Choose(item.id); break;
                case UICommand.Save: game.Save(); notice = L("Saved", "저장됨"); break;
                case UICommand.Load: game.Load(); notice = L("Loaded", "불러옴"); break;
                case UICommand.Restart: game.Restart(); selectedSlot = 0; break;
                case UICommand.NewPlan: Host.NewPlan(); selectedSlot = 0; break;
                case UICommand.OpenPanel: SetVisible(root.Q(c.panel), true); break;
                case UICommand.ClosePanel: SetVisible(root.Q(c.panel), false); break;
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; game.Changed -= Refresh;
            foreach (var action in unsubscribe) action();
            unsubscribe.Clear();
            foreach (var table in rows.Values) foreach (var row in table.Values) row.RemoveFromHierarchy();
            rows.Clear();
        }
    }
}
