using System;
using System.Linq;
using RaiseArc.Unity;
using PrincessStudio.Core;
using PrincessStudio.Localization;
using PrincessStudio.Unity;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace PrincessStudio.Samples
{
    public sealed partial class SampleGameController
    {
        [SerializeField] private VisualTreeAsset screenTemplate;
        private void DrawTemplate()
        {
            screenTemplate.CloneTree(root);
            var shell = root.Q("game-shell");
            if (presentationSkin != null) { shell.style.backgroundColor = presentationSkin.background; shell.style.color = presentationSkin.foreground; }
            var state = session.State;
            var title = Label(project.translations.Exists(t => t.key == "ui.game.title") ? "ui.game.title" : project.character.nameKey); title.style.fontSize = 28;
            if (presentationSkin != null) title.style.color = presentationSkin.accent;
            root.Q("title-slot").Add(title);
            var language = new DropdownField(project.locales, Math.Max(0, project.locales.IndexOf(LocalizationSettings.SelectedLocale.Identifier.Code)));
            language.name = "locale-selector";
            language.RegisterValueChangedCallback(e => LocalizedLabelBinding.SelectLocale(e.newValue)); root.Q("language-slot").Add(language);
            var summary = new Label { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 24 } };
            bindings.Add(new LocalizedLabelBinding(summary, "Princess." + project.id, project.translations.Exists(t => t.key == "ui.screen.summary") ? "ui.screen.summary" : "ui.summary", state.Age, state.Day / (project.daysPerMonth * project.monthsPerYear) + 1, state.Day / project.daysPerMonth % project.monthsPerYear + 1, state.Day % project.daysPerMonth + 1, state.Money));
            root.Q("summary-slot").Add(summary);
            root.Q("summary-slot").Add(LabelWithProgress(state.Day));
            foreach (var activity in project.activities.Where(a => a.evaluation?.enabled == true))
            {
                var row = new VisualElement(); row.Add(Label(activity.nameKey));
                row.Add(new Label(Local("Best / total / attempts / passes: ", "최고 / 합계 / 시도 / 통과: ") +
                    state.Read(ValueKind.RecordBest, activity.id) + " / " + state.Read(ValueKind.RecordTotal, activity.id) + " / " +
                    state.Read(ValueKind.RecordAttempts, activity.id) + " / " + state.Read(ValueKind.RecordPasses, activity.id)));
                root.Q("summary-slot").Add(row);
            }
            foreach (var flag in project.flags.Where(f => f.permanent && state.Flags[f.id] == 1))
            {
                root.Q("summary-slot").Add(new Label(Local("Qualification acquired:", "취득한 자격:")));
                root.Q("summary-slot").Add(Label(flag.nameKey));
            }
            foreach (var modifier in project.modifiers.Where(m => state.Read(ValueKind.ModifierDays, m.id) > 0))
            {
                root.Q("summary-slot").Add(Label(modifier.nameKey));
                root.Q("summary-slot").Add(new Label(state.Read(ValueKind.ModifierDays, modifier.id) + Local(" game days remaining", "일 남음")));
            }
            foreach (var stat in project.stats)
            {
                if (screenDefinition.visibleStats.Count > 0 && !screenDefinition.visibleStats.Contains(stat.id)) continue;
                root.Q("stats-slot").Add(Label(stat.nameKey));
                root.Q("stats-slot").Add(new ProgressBar { lowValue = stat.minimum, highValue = stat.maximum, value = state.Stats[stat.id], title = state.Stats[stat.id].ToString() });
            }
            if (stage != null)
            {
                var locale = LocalizationSettings.SelectedLocale.Identifier.Code;
                stage.SetDefaultImages(presentationSkin.Find("screen.background", locale, project.fallbackLocale) ?? defaultBackground, presentationSkin.Find("screen.character", locale, project.fallbackLocale) ?? defaultPortrait, screenDefinition.portraitScale, screenDefinition.fillBackground);
                stage.Render(state, locale);
                stage.Element.style.height = StyleKeyword.Auto; stage.Element.style.flexGrow = 1;
                root.Q("stage-slot").Add(stage.Element);
            }
            var story = root.Q("story-slot"); var actions = root.Q("actions-slot");
            if (state.EndingId.Length > 0)
            {
                var ending = project.endings.Find(x => x.id == state.EndingId);
                story.Add(Label(ending.nameKey)); story.Add(Label(ending.descriptionKey));
            }
            else if (state.PendingEventId.Length > 0)
            {
                var e = playbackEvents.Find(x => x.id == state.PendingEventId);
                DrawEventSequence(story, actions, e);
            }
            else
            {
                story.Add(Label("ui.plan"));
                DrawActivities(actions);
            }
            if (!string.IsNullOrEmpty(message)) root.Q("status-slot").Add(Label(message));
            var toolbar = root.Q("toolbar-slot");
            var save = Action("ui.save", () => { host.Save("slot1"); message = "ui.saved"; Draw(); });
            save.SetEnabled(host.CanSave); toolbar.Add(save);
            if (!host.CanSave) toolbar.Add(new Label(Local("Finish or cancel the module before saving.", "외부 게임을 완료하거나 취소한 뒤 저장하세요.")));
            toolbar.Add(Action("ui.load", () => { voice.Stop(); host.Load("slot1"); message = "ui.loaded"; Draw(); }));
            toolbar.Add(Action("ui.restart", () => { voice.Stop(); plannedActivities.Clear(); host.Restart(1); message = ""; Draw(); }));
            if (screenDefinition.showVoiceControls) AddVoiceControls(toolbar);
            ApplyScreenLayout(shell);
        }

        private string Local(string en, string ko) => LocalizationSettings.SelectedLocale.Identifier.Code == "ko" ? ko : en;
        private string ActivityBlockedReason(string id)
        {
            var info = session.DiagnoseActivity(id);
            switch (info.Reason)
            {
                case RaiseArc.Core.ActivityBlock.InsufficientMoney: return Local("Not enough money", "돈이 부족합니다") + " (" + info.Actual + " / " + info.Required + ")";
                case RaiseArc.Core.ActivityBlock.InsufficientDays: return Local("Not enough days remain", "남은 기간이 부족합니다") + " (" + info.Actual + " / " + info.Required + ")";
                case RaiseArc.Core.ActivityBlock.ConditionsNotMet: return Local("Entry conditions are not met.", "활동 시작 조건을 충족하지 못했습니다.");
                case RaiseArc.Core.ActivityBlock.EventPending: return Local("Finish the current dialogue or choice first.", "진행 중인 대화·선택을 먼저 마치세요.");
                case RaiseArc.Core.ActivityBlock.ModulePending: return Local("Finish or cancel the current module.", "외부 게임을 완료하거나 취소하세요.");
                case RaiseArc.Core.ActivityBlock.ActivityPending: return Local("Finish the current activity first.", "진행 중인 활동을 먼저 마치세요.");
                case RaiseArc.Core.ActivityBlock.GameEnded: return Local("This game has ended.", "게임이 끝났습니다.");
                default: return Local("This activity is unavailable.", "이 활동을 실행할 수 없습니다.");
            }
        }
        private Button PlainButton(string en, string ko, Action callback) => Action(Local(en, ko), callback);
        private void DrawActivities(VisualElement parent)
        {
            if (host.Wait == SessionWait.Module)
            {
                parent.Add(new Label(Local("Waiting for the game module. Saving is unavailable until it finishes or is cancelled.", "외부 게임을 기다리고 있습니다. 완료하거나 취소하기 전에는 저장할 수 없습니다.")));
                parent.Add(PlainButton("Cancel module", "외부 게임 취소", () => host.CancelModule(host.Module.SessionId))); return;
            }
            if (host.ScheduleCursor < host.Schedule.Count)
            {
                parent.Add(new Label(Local("Schedule is waiting: ", "일정 대기: ") + host.Wait + " · " + (host.ScheduleCursor + 1) + "/" + host.Schedule.Count));
                parent.Add(PlainButton("Resume", "재개", () => host.Resume()));
                parent.Add(PlainButton("Skip current activity", "현재 활동 건너뛰기", () => host.SkipCurrent()));
                parent.Add(PlainButton("Clear remaining schedule", "남은 일정 비우기", () => host.ClearSchedule())); return;
            }
            var activities = project.activities.Where(a => screenDefinition.activityCategories.Count == 0 || screenDefinition.activityCategories.Contains(string.IsNullOrEmpty(a.categoryId) ? a.category.ToString() : a.categoryId)).ToList();
            if (activities.Count == 0) parent.Add(new Label(Local("No activities in the selected categories. Change the screen's category filter in Game setup.", "선택한 분류에 활동이 없습니다. 게임 만들기 창에서 화면의 활동 분류를 확인하세요.")));
            foreach (var activity in activities)
            {
                var card = new VisualElement(); card.style.minWidth = 180; card.style.flexGrow = 1; card.style.flexBasis = new Length(30, LengthUnit.Percent); card.style.marginRight = 8;
                var sprite = presentationSkin?.Find(activity.imageKey, LocalizationSettings.SelectedLocale.Identifier.Code, project.fallbackLocale);
                if (sprite != null) { var image = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit }; image.style.height = 56; card.Add(image); }
                card.Add(ActivityButton(activity));
                var daily = activity.checkFrequency == RaiseArc.Core.ActivityCheckFrequency.OncePerDay || activity.checkFrequency == RaiseArc.Core.ActivityCheckFrequency.ProjectDefault && project.defaultActivityCheckFrequency == RaiseArc.Core.ActivityCheckFrequency.OncePerDay;
                card.Add(new Label(activity.days + Local(" days · cost ", "일 · 비용 ") + activity.cost + (daily ? Local(" each day", " / 매일") : Local(" once per activity", " / 활동 전체 한 번"))));
                if (!session.GetActivityInfo(activity.id).Available) card.Add(new Label(Local("Cannot run now: ", "지금 실행 불가: ") + ActivityBlockedReason(activity.id)));
                parent.Add(card);
            }
            if (screenDefinition.progression != GameProgression.Schedule) return;
            var plan = new VisualElement(); plan.style.width = Length.Percent(100); parent.Add(plan);
            plan.Add(new Label(Local("Click activities to add them to your schedule. Nothing runs until you press Run schedule.", "활동을 누르면 일정에 추가됩니다. 일정 실행을 눌러야 진행됩니다.")));
            for (var i = 0; i < plannedActivities.Count; i++)
            {
                var index = i; var activity = project.activities.Find(a => a.id == plannedActivities[i]);
                plan.Add(PlainButton((i + 1) + ". " + host.Text(activity.nameKey, "en") + " × Remove", (i + 1) + ". " + host.Text(activity.nameKey, "ko") + " × 빼기", () => { plannedActivities.RemoveAt(index); Draw(); }));
            }
            var days = plannedActivities.Sum(id => project.activities.Find(a => a.id == id).days);
            plan.Add(new Label(Local("Planned days / remaining: ", "예정 일수 / 남은 기간: ") + days + " / " + (project.durationDays - host.State.Day)));
            var run = PlainButton("Run schedule", "일정 실행", () => { var ids = plannedActivities.ToArray(); host.StartSchedule(ids, project.durationDays - host.State.Day); plannedActivities.Clear(); Draw(); });
            run.SetEnabled(plannedActivities.Count > 0 && days <= project.durationDays - host.State.Day); plan.Add(run);
            if (days > project.durationDays - host.State.Day) plan.Add(new Label(Local("Remove activities until the schedule fits the remaining game period.", "일정이 남은 게임 기간에 맞도록 활동을 빼 주세요.")));
        }
        private void ApplyScreenLayout(VisualElement shell)
        {
            shell.style.fontSize = screenDefinition.fontSize;
            var paper = screenDefinition.theme == GameScreenTheme.Paper;
            shell.style.backgroundColor = paper ? new Color(.91f,.88f,.82f) : screenDefinition.theme == GameScreenTheme.Forest ? new Color(.08f,.16f,.14f) : new Color(.09f,.07f,.13f);
            shell.style.color = paper ? new Color(.15f,.13f,.2f) : new Color(.95f,.92f,.98f);
            var body = root.Q(className: "game-body"); var sidebar = root.Q(className: "game-sidebar");
            body.style.flexDirection = screenDefinition.layout == GameScreenLayout.SidebarRight ? FlexDirection.RowReverse : screenDefinition.layout == GameScreenLayout.Compact ? FlexDirection.Column : FlexDirection.Row;
            sidebar.style.width = screenDefinition.layout == GameScreenLayout.Compact ? new StyleLength(StyleKeyword.Auto) : new StyleLength(250);
            sidebar.style.maxHeight = screenDefinition.layout == GameScreenLayout.Compact ? new StyleLength(230) : new StyleLength(StyleKeyword.None);
            root.Q("stats-slot").style.display = screenDefinition.showStats ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("stage-slot").style.display = screenDefinition.showStage ? DisplayStyle.Flex : DisplayStyle.None;
            if (screenDefinition.statsFirst) root.Q("stats-slot").PlaceBehind(root.Q("summary-slot"));
            if (paper) { sidebar.style.backgroundColor = new Color(.85f,.81f,.74f); root.Q("story-slot").style.backgroundColor = new Color(.95f,.92f,.87f); }
        }

        private void AddVoiceControls(VisualElement parent)
        {
            var ko = LocalizationSettings.SelectedLocale.Identifier.Code == "ko";
            var volume = new Slider(ko ? "보이스 음량" : "Voice volume", 0, 1) { value = voice.Volume };
            volume.RegisterValueChangedCallback(e => voice.Volume = e.newValue); parent.Add(volume);
            var mute = new Toggle(ko ? "보이스 음소거" : "Mute voice") { value = voice.Muted };
            mute.RegisterValueChangedCallback(e => voice.Muted = e.newValue); parent.Add(mute);
        }

        private Label LabelWithProgress(int day)
        {
            var ko = LocalizationSettings.SelectedLocale.Identifier.Code == "ko";
            return new Label(ko ? $"경과 {day} / {project.durationDays}일" : $"Elapsed {day} / {project.durationDays} days");
        }

        private void DrawEventSequence(VisualElement story, VisualElement actions, EventDefinition e)
        {
            var step = e.presentation.Find(x => x.id == session.State.PresentationStepId);
            if (step == null) story.Add(Label(e.descriptionKey));
            else
            {
                if (step.kind != PresentationStepKind.Image)
                {
                    var speaker = project.actors.Find(x => x.id == step.speakerActorId);
                    if (speaker != null) story.Add(Label(speaker.nameKey));
                    story.Add(Label(step.nameKey));
                }
                else if (story != actions) story.style.display = DisplayStyle.None;
                if (step.kind != PresentationStepKind.Choice)
                    actions.Add(Action("ui.continue", () => { host.AdvanceDialogue(step.id); Draw(); }));
            }
            foreach (var id in session.AvailableChoices())
            {
                var choice = (step == null ? e.choices : step.choices).Find(x => x.id == id);
                actions.Add(Action(choice.nameKey, () => { host.Choose(id); Draw(); }));
            }
        }
    }
}
