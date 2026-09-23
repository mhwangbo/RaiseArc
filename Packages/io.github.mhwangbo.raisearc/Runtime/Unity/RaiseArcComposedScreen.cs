using System;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Unity
{
    public sealed class ComposedScreen
    {
        private int selectedSlot, problemSlot = -1;
        private string notice = "";
        public void Draw(VisualElement root, GameScreenDefinition definition, RaiseArcSessionHost host,
            PlanningRehearsal rehearsal, string locale, Sprite portrait, Sprite background,
            Action<ScreenPart> selectPart = null, Action save = null, Action load = null,
            Func<string, string> statName = null, PresentationSkin skin = null, Action<VisualElement> eventBody = null)
        {
            var ko = locale == "ko";
            string L(string en, string kr) => ko ? kr : en;
            string Title(ScreenPart p) => ko && !string.IsNullOrEmpty(p.titleKo) ? p.titleKo : p.title;
            var legacy = rehearsal != null;
            var plan = host.Plan;
            var count = legacy ? 3 : host.PeriodsPerDay;
            var capacity = legacy ? 21 : plan.capacity;
            var locked = !legacy && plan.confirmed;
            selectedSlot = Mathf.Clamp(selectedSlot, 0, Math.Max(0, capacity - 1));
            var activities = host.Activities();
            int Owner(int slot) => legacy ? (string.IsNullOrEmpty(rehearsal[slot]) ? -1 : slot) : host.PlanOwnerSlot(slot);
            string ActivityAt(int slot) => legacy ? rehearsal[slot] : plan.entries.Find(e => e.slot == Owner(slot))?.activityId ?? "";
            string Period(int n) => legacy ? (ko ? new[] { "오전", "오후", "저녁" } : new[] { "Morning", "Afternoon", "Evening" })[n] : host.PeriodName(n, locale);
            void Redraw() => Draw(root, definition, host, rehearsal, locale, portrait, background, selectPart, save, load, statName, skin, eventBody);
            void Run(Action action)
            {
                try { notice = ""; problemSlot = -1; action(); }
                catch (Exception ex) { notice = ex.Message; problemSlot = selectedSlot; }
                Redraw();
            }
            void Place(string id) { if (legacy) rehearsal.Place(selectedSlot, id); else host.PlacePlanActivity(selectedSlot, id); }
            root.Clear();
            root.style.paddingTop = root.style.paddingBottom = root.style.paddingLeft = root.style.paddingRight = 0;
            root.style.backgroundColor = new Color(.055f, .085f, .115f); root.style.color = Color.white;
            var canvas = new VisualElement { name = "composition-canvas" };
            canvas.style.flexGrow = 1; canvas.style.minHeight = 420; root.Add(canvas);
            foreach (var part in definition.parts)
            {
                var box = new VisualElement { name = part.id };
                box.style.position = Position.Absolute;
                box.style.left = Length.Percent(part.bounds.x); box.style.top = Length.Percent(part.bounds.y);
                box.style.width = Length.Percent(part.bounds.width); box.style.height = Length.Percent(part.bounds.height);
                box.style.backgroundColor = part.background; box.style.color = part.foreground; box.style.fontSize = part.fontSize;
                Pad(box, part.padding); Round(box, 8); box.style.overflow = Overflow.Hidden; canvas.Add(box);
                if (selectPart != null) box.RegisterCallback<PointerDownEvent>(e => { selectPart(part); e.StopPropagation(); }, TrickleDown.TrickleDown);
                if (part.kind != ScreenPartKind.Button)
                {
                    var title = Label(Title(part), part.fontSize); title.style.unityFontStyleAndWeight = FontStyle.Bold;
                    if (part.kind == ScreenPartKind.Character) { title.style.backgroundColor = new Color(.06f,.09f,.12f,.90f); Pad(title, 6); }
                    box.Add(title);
                }
                switch (part.kind)
                {
                    case ScreenPartKind.Character:
                        if (background != null) box.style.backgroundImage = new StyleBackground(background);
                        var actor = new Image { sprite = portrait, scaleMode = ScaleMode.ScaleToFit };
                        actor.style.flexGrow = 1; actor.style.minHeight = 0; box.Add(actor); break;
                    case ScreenPartKind.Stats:
                        var stats = new ScrollView(); stats.style.flexGrow = 1; box.Add(stats);
                        stats.Add(Label(L("Elapsed day ", "경과 일수 ") + host.State.Day + " · " + Period(legacy ? 0 : host.State.Period), part.fontSize));
                        stats.Add(Label(L("Money ", "소지금 ") + host.State.Money, part.fontSize));
                        foreach (var stat in host.State.Stats.OrderBy(s => statName?.Invoke(s.Key) ?? s.Key, StringComparer.Ordinal)) stats.Add(Label((statName?.Invoke(stat.Key) ?? stat.Key) + "   " + stat.Value, part.fontSize));
                        break;
                    case ScreenPartKind.Activities:
                        box.Add(Label(L("Select a slot, then choose an activity.", "칸을 선택한 뒤 활동을 누르세요."), 12));
                        var cards = new ScrollView(part.horizontalCards ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical);
                        cards.style.flexGrow = 1; cards.contentContainer.style.flexDirection = part.horizontalCards ? FlexDirection.Row : FlexDirection.Column; box.Add(cards);
                        foreach (var a in host.Activities(string.IsNullOrEmpty(part.category) ? null : part.category))
                        {
                            var card = Button("", () => Run(() => Place(a.Id)));
                            card.name = "activity-card-" + a.Id;
                            card.style.minHeight = part.cardHeight; card.style.flexShrink = 0;
                            if (part.horizontalCards) card.style.width = part.cardWidth;
                            card.style.flexDirection = part.horizontalFields ? FlexDirection.Row : FlexDirection.Column;
                            card.style.alignItems = Align.FlexStart;
                            card.style.backgroundColor = part.itemBackground; card.style.color = part.itemText; Pad(card, part.itemPadding);
                            card.SetEnabled(!locked && capacity > 0);
                            foreach (var field in part.cardFields)
                            {
                                if (field == CardField.Image)
                                {
                                    var sprite = skin?.Find(a.ImageKey, locale, "en");
                                    if (sprite != null) { var icon = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit }; icon.style.width = icon.style.height = part.imageSize; card.Add(icon); }
                                    continue;
                                }
                                var fieldText = field == CardField.Name ? host.Text(a.NameKey, locale) :
                                    field == CardField.Description ? (string.IsNullOrEmpty(a.DescriptionKey) ? "" : host.Text(a.DescriptionKey, locale)) :
                                    field == CardField.Cost ? L("Cost / check ", "판정당 비용 ") + a.CostPerCheck :
                                    host.ActivityPeriods(a.Id) + L(" time slot(s)", "칸");
                                if (string.IsNullOrWhiteSpace(fieldText) || field == CardField.Description && fieldText == a.DescriptionKey) continue;
                                var label = Label(fieldText, field == CardField.Name ? part.fontSize : Math.Max(12, part.fontSize - 2));
                                label.style.flexShrink = 1;
                                if (field == CardField.Name) label.style.unityFontStyleAndWeight = FontStyle.Bold;
                                card.Add(label);
                            }
                            cards.Add(card);
                        }
                        break;
                    case ScreenPartKind.Timetable:
                        var filled = Enumerable.Range(0, capacity).Count(n => Owner(n) >= 0);
                        box.Add(Label((locked ? L("CONFIRMED · locked", "확정 · 편집 잠김") : L("DRAFT", "초안")) + "  " + filled + "/" + capacity, 13));
                        box.Add(Label(L("Selected: day ", "선택: ") + ((plan.startTick + selectedSlot) / count + 1) + L(" · ", "일 · ") + Period((plan.startTick + selectedSlot) % count), 12));
                        var rows = new ScrollView(); rows.style.flexGrow = 1; box.Add(rows);
                        var header = new VisualElement(); header.style.flexDirection = FlexDirection.Row; rows.Add(header);
                        var spacer = Label("", 12); spacer.style.width = 28; header.Add(spacer);
                        for (var n = 0; n < count; n++) { var label = Label(Period(n), 12); label.style.flexGrow = 1; label.style.flexBasis = 0; header.Add(label); }
                        var firstDay = plan.startTick / count;
                        var startPeriod = legacy ? 0 : plan.startTick % count;
                        for (var day = 0; day < (capacity + startPeriod + count - 1) / count; day++)
                        {
                            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.minHeight = part.slotHeight; rows.Add(row);
                            var dayLabel = Label((firstDay + day + 1).ToString(), 12); dayLabel.style.width = 28; dayLabel.style.flexShrink = 0; dayLabel.style.alignSelf = Align.Center; row.Add(dayLabel);
                            for (var n = 0; n < count; n++)
                            {
                                var slot = day * count + n - startPeriod;
                                if (slot < 0 || slot >= capacity) { var empty = new VisualElement(); empty.style.flexGrow = 1; empty.style.flexBasis = 0; row.Add(empty); continue; }
                                var id = ActivityAt(slot); var info = activities.FirstOrDefault(a => a.Id == id);
                                var owner = Owner(slot);
                                var active = locked && host.ScheduleCursor < host.Schedule.Count && plan.entries.OrderBy(e => e.slot).ElementAt(host.ScheduleCursor).slot == owner;
                                var cell = Button(info == null ? "+" : (owner == slot ? "" : "↳ ") + host.Text(info.NameKey, locale), () => { selectedSlot = slot; Redraw(); });
                                cell.name = "plan-slot-" + slot;
                                cell.style.flexGrow = 1; cell.style.flexBasis = 0; cell.style.minWidth = 0; cell.style.minHeight = part.slotHeight - 6; cell.style.fontSize = part.fontSize; cell.style.height = part.slotHeight - 6; Pad(cell, 0);
                                if (slot == selectedSlot || active) cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                                var issue = slot == problemSlot || active && (host.Wait == SessionWait.ActivityBlocked || host.Wait == SessionWait.PlanInterrupted || host.Wait == SessionWait.PeriodExceeded);
                                cell.style.backgroundColor = issue ? part.problemBackground : active ? part.runningBackground : slot == selectedSlot ? part.selectedBackground : locked ? part.lockedBackground : info != null ? part.placedBackground : part.itemBackground;
                                cell.style.color = slot == selectedSlot && !issue && !active ? part.selectedText : part.itemText;
                                cell.tooltip = active ? L("Current plan item", "실행 중인 계획 항목") : locked ? L("Confirmed: cannot edit", "확정됨: 편집할 수 없음") : L("Select this time", "이 시간 선택");
                                row.Add(cell);
                            }
                        }
                        box.Add(Label(L("Selected · Placed · Current · Locked · Problem", "선택 · 배치됨 · 실행 위치 · 잠김 · 문제"), 11));
                        break;
                    case ScreenPartKind.Button:
                        var text = Title(part);
                        var actionKind = part.action;
                        if (actionKind == ScreenButtonAction.ConfirmPlan && locked)
                        {
                            actionKind = host.ScheduleCursor >= host.Schedule.Count ? ScreenButtonAction.NewPlan : ScreenButtonAction.ResumePlan;
                            text = actionKind == ScreenButtonAction.NewPlan ? L("New plan", "다음 계획") : L("Run next", "다음 활동 실행");
                        }
                        var action = Button(text, () => Run(() =>
                        {
                            switch (actionKind)
                            {
                                case ScreenButtonAction.SaveDraft: save?.Invoke(); notice = L("Game and plan saved.", "게임 상태와 계획을 저장했습니다."); break;
                                case ScreenButtonAction.LoadDraft: load?.Invoke(); notice = L("Game and plan restored.", "게임 상태와 계획을 복원했습니다."); break;
                                case ScreenButtonAction.RemoveSlot: Place(""); break;
                                case ScreenButtonAction.CopyDay:
                                    if (legacy) rehearsal.CopyDay(selectedSlot / count, selectedSlot / count + 1);
                                    else host.CopyPlanDay(selectedSlot / count, selectedSlot / count + 1); break;
                                case ScreenButtonAction.ConfirmPlan: host.ConfirmPlan(); break;
                                case ScreenButtonAction.ResumePlan: host.Resume(); break;
                                case ScreenButtonAction.NewPlan: host.NewPlan(); break;
                            }
                        }));
                        action.style.flexGrow = 1; action.style.whiteSpace = WhiteSpace.NoWrap;
                        action.tooltip = text;
                        action.SetEnabled(actionKind != ScreenButtonAction.None &&
                            !(legacy && actionKind == ScreenButtonAction.ConfirmPlan) &&
                            !(locked && (actionKind == ScreenButtonAction.RemoveSlot || actionKind == ScreenButtonAction.CopyDay)) &&
                            !(actionKind == ScreenButtonAction.SaveDraft && (save == null || !host.CanSave)) &&
                            !(actionKind == ScreenButtonAction.LoadDraft && load == null) &&
                            !(actionKind == ScreenButtonAction.ResumePlan && (host.Wait == SessionWait.Event || host.Wait == SessionWait.Module || host.Wait == SessionWait.Ended)) &&
                            !(actionKind == ScreenButtonAction.NewPlan && host.Wait == SessionWait.Ended));
                        box.Add(action); break;
                }
            }
            if (!legacy && (host.Wait == SessionWait.Event || host.Wait == SessionWait.Ended))
            {
                var overlay = new ScrollView { name = "plan-event" };
                overlay.style.position = Position.Absolute; overlay.style.left = Length.Percent(28); overlay.style.top = Length.Percent(18);
                overlay.style.width = Length.Percent(67); overlay.style.maxHeight = Length.Percent(70);
                overlay.style.backgroundColor = new Color(.10f,.16f,.20f); Pad(overlay, 20); Round(overlay, 8); canvas.Add(overlay);
                if (eventBody != null)
                {
                    eventBody(overlay);
                    overlay.Query<Label>().ForEach(l => { l.style.minHeight = 24; l.style.flexShrink = 0; l.style.fontSize = 16; l.style.color = Color.white; l.style.whiteSpace = WhiteSpace.Normal; });
                    overlay.Query<Button>().ForEach(b => { b.style.backgroundColor = new Color(.22f,.34f,.38f); b.style.color = Color.white; b.style.minHeight = 40; Pad(b, 8); Round(b, 5); });
                }
                else
                {
                    overlay.Add(Label(host.Wait == SessionWait.Ended ? host.State.EndingId : host.Text(host.State.PendingEventId, locale), 20));
                    foreach (var choice in host.CurrentChoices) overlay.Add(Button(host.Text(choice.nameKey, locale), () => Run(() => host.Choose(choice.id))));
                    if (host.Wait == SessionWait.Event && host.CurrentChoices.Count == 0) overlay.Add(Button(L("Continue", "계속"), () => Run(() => host.AdvanceDialogue(host.State.PresentationStepId))));
                }
            }
            var wait = host.Wait == SessionWait.Event ? L("Waiting for an event choice", "사건·대화 입력 대기") :
                host.Wait == SessionWait.Module ? L("Waiting for module", "모듈 완료 대기") :
                host.Wait == SessionWait.ActivityBlocked ? L("Current activity cannot run; plan is preserved", "현재 활동을 실행할 수 없습니다. 계획은 유지됩니다.") :
                host.Wait == SessionWait.PlanInterrupted ? L("Activity stopped early; plan is preserved", "활동이 중간에 중단됐습니다. 계획은 유지됩니다.") :
                host.Wait == SessionWait.PeriodExceeded ? L("Duration exceeds remaining plan time", "남은 계획 시간보다 활동이 깁니다.") :
                host.Wait == SessionWait.Ended ? L("Game ended", "게임 종료") :
                locked ? L("Confirmed · run the next activity", "확정됨 · 다음 활동을 실행하세요") : L("Draft · no time or money spent", "초안 · 시간과 비용은 아직 소비되지 않음");
            var status = Label((legacy ? L("DESIGN REHEARSAL · execution unavailable", "화면 리허설 · 실행 미연결") : wait) + (string.IsNullOrEmpty(notice) ? "" : "\n" + notice), 13);
            Pad(status, 6); status.style.height = 46; status.style.flexShrink = 0; root.Add(status);
        }
        private static Label Label(string text, int size)
        {
            var label = new Label(text); label.style.fontSize = size; label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = label.style.marginBottom = 0; label.style.paddingTop = label.style.paddingBottom = 0; return label;
        }
        private static void Pad(VisualElement e, int n) => e.style.paddingLeft = e.style.paddingRight = e.style.paddingTop = e.style.paddingBottom = n;
        private static void Round(VisualElement e, int n) => e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = n;
        private static Button Button(string text, Action action)
        {
            var b = new Button(action) { text = text }; b.style.whiteSpace = WhiteSpace.Normal;
            b.style.backgroundColor = new Color(.18f,.25f,.29f); b.style.color = Color.white;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            b.style.marginTop = b.style.marginBottom = b.style.marginLeft = b.style.marginRight = 3; Round(b, 5); return b;
        }
    }
}
