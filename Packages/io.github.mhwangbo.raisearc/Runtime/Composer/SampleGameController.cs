using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RaiseArc.Unity;
using System.Threading;
using PrincessStudio.Core;
using PrincessStudio.Localization;
using PrincessStudio.Unity;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace PrincessStudio.Samples
{
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class SampleGameController : MonoBehaviour
    {
        [SerializeField] private GameProjectAsset projectAsset;
        [SerializeField] private PresentationSkin presentationSkin;
        [SerializeField] private ActorViewProvider actorViewProvider;
        [SerializeField] private Sprite defaultBackground;
        [SerializeField] private Sprite defaultPortrait;
        private PresentationStage stage;
        private RaiseArc.Localization.RaiseArcDialogueVoice voice;
        private ProjectDefinition project;
        private List<EventDefinition> playbackEvents;
        private RaiseArcSessionHost host;
        private GameSession session => host?.Session;
        private bool runningModule;
        private int screenRevision = -1;
        private GameScreenDefinition screenDefinition = new GameScreenDefinition();
        private readonly List<string> plannedActivities = new List<string>();
        private PlanningRehearsal planningRehearsal;
        private RaiseArcGameScreenSettings renderedScreen;
        private readonly ComposedScreen composedScreen = new ComposedScreen();
        [SerializeField] private RaiseArcGameScreenSettings screenSettings;
        private readonly List<LocalizedLabelBinding> bindings = new List<LocalizedLabelBinding>();
        private LocalizedFontBinding fontBinding;
        private CancellationTokenSource lifetime;
        private VisualElement root;
        private string message = "";
        public void Configure(GameProjectAsset asset)
        {
            projectAsset = asset;
        }
        private async void Start()
        {
            lifetime = new CancellationTokenSource();
            try
            {
                if (projectAsset == null)
                    throw new InvalidOperationException("Sample requires a GameProjectAsset.");
                project = projectAsset.Read();
                if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
                voice = GetComponent<RaiseArc.Localization.RaiseArcDialogueVoice>() ?? gameObject.AddComponent<RaiseArc.Localization.RaiseArcDialogueVoice>();
                voice.Configure(project);
                playbackEvents = project.events.ConvertAll(e => RaiseArc.Core.RaiseArcFlowReuse.Expand(project, e));
                if (presentationSkin != null) stage = new PresentationStage(project, presentationSkin, actorViewProvider != null ? actorViewProvider.CreateView : (Func<VisualElement, string, IActorView>)null, defaultBackground, defaultPortrait);
                host = new RaiseArcSessionHost(projectAsset, Path.Combine(Application.persistentDataPath, "PrincessStudio", project.id), 1);
                if (screenSettings != null && screenSettings.Read().compositionPreview && project.time.periodNameKeys.Count == 0)
                {
                    planningRehearsal = new PlanningRehearsal(project.activities.Select(a => a.id));
                    host.RegisterSaveParticipant(planningRehearsal);
                }
                await LocalizationSettings.InitializationOperation.Task;
                if (lifetime.IsCancellationRequested)
                    return;
                root = GetComponent<UIDocument>().rootVisualElement;
                if (project.localizedAssets.Exists(a => a.key == "ui.font"))
                    fontBinding = new LocalizedFontBinding(root, "Princess." + project.id + ".Assets", "ui.font");
                host.Changed += OnHostChanged;
                Draw();
                LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            }
            catch (Exception e) { Debug.LogException(e, this); }
        }
        private void Draw()
        {
            if (root == null || session == null) return;
            if (screenSettings != null) { screenDefinition = screenSettings.Read(); screenRevision = screenSettings.Revision; renderedScreen = screenSettings; }
            voice?.Present(session);
            foreach (var b in bindings)
                b.Dispose();
            bindings.Clear();
            root.Clear();
            if (screenDefinition.compositionPreview)
            {
                composedScreen.Draw(root, screenDefinition, host, planningRehearsal, LocalizationSettings.SelectedLocale.Identifier.Code,
                    defaultPortrait, defaultBackground, save: () => host.Save(planningRehearsal == null ? "weekly-plan" : "planning-rehearsal"), load: () => host.Load(planningRehearsal == null ? "weekly-plan" : "planning-rehearsal"),
                    statName: id => host.Text(project.stats.Find(s => s.id == id).nameKey, LocalizationSettings.SelectedLocale.Identifier.Code),
                    skin: presentationSkin, eventBody: panel => {
                        if (host.Wait == SessionWait.Ended) { var ending = project.endings.Find(e => e.id == host.State.EndingId); panel.Add(Label(ending?.nameKey ?? "ui.ending")); if (ending != null) panel.Add(Label(ending.descriptionKey)); }
                        else { var ev = playbackEvents.Find(e => e.id == host.State.PendingEventId); if (ev != null) DrawEventSequence(panel, panel, ev); }
                    });
                return;
            }
            if (screenTemplate != null) { DrawTemplate(); return; }
            root.style.backgroundColor = new Color(0.075f, 0.067f, 0.11f);
            root.style.color = new Color(0.93f, 0.9f, 0.96f);
            root.style.paddingTop = 36;
            root.style.paddingLeft = 40;
            root.style.paddingRight = 40;
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            var title = Label(project.character.nameKey);
            title.style.fontSize = 34;
            title.style.color = new Color(0.94f, 0.82f, 0.63f);
            scroll.Add(title);
            var state = session.State;
            if (stage != null)
            {
                root.style.backgroundColor = presentationSkin.background;
                root.style.color = presentationSkin.foreground;
                title.style.color = presentationSkin.accent;
                stage.Render(state, LocalizationSettings.SelectedLocale.Identifier.Code);
                scroll.Add(stage.Element);
            }
            var summary = new Label();
            summary.style.whiteSpace = WhiteSpace.Normal;
            bindings.Add(new LocalizedLabelBinding(summary, "Princess." + project.id, "ui.summary", state.Age, state.Day / (project.daysPerMonth * project.monthsPerYear) + 1, state.Day / project.daysPerMonth % project.monthsPerYear + 1, state.Day % project.daysPerMonth + 1, state.Money));
            scroll.Add(summary);
            var languages = new DropdownField(project.locales, Math.Max(0, project.locales.IndexOf(LocalizationSettings.SelectedLocale.Identifier.Code)));
            languages.name = "locale-selector";
            languages.RegisterValueChangedCallback(e => LocalizedLabelBinding.SelectLocale(e.newValue));
            scroll.Add(languages);
            foreach (var stat in project.stats)
            {
                scroll.Add(Label(stat.nameKey));
                var bar = new ProgressBar { lowValue = stat.minimum, highValue = stat.maximum, value = state.Stats[stat.id], title = state.Stats[stat.id].ToString() };
                bar.style.marginBottom = 10;
                scroll.Add(bar);
            }
            if (message.Length > 0)
                scroll.Add(Label(message));
            if (state.EndingId.Length > 0)
            {
                var e = project.endings.Find(x => x.id == state.EndingId);
                scroll.Add(Label(e.nameKey));
                scroll.Add(Label(e.descriptionKey));
            }
            else if (state.PendingEventId.Length > 0)
            {
                var e = playbackEvents.Find(x => x.id == state.PendingEventId);
                DrawEventSequence(scroll, scroll, e);
            }
            else
            {
                scroll.Add(Label("ui.plan"));
                DrawActivities(scroll);
            }
            scroll.Add(Action("ui.save", () => { host.Save("slot1"); message = "ui.saved"; Draw(); }));
            scroll.Add(Action("ui.load", () => { voice.Stop(); host.Load("slot1"); message = "ui.loaded"; Draw(); }));
            scroll.Add(Action("ui.restart", () => { voice.Stop(); plannedActivities.Clear(); host.Restart(1); message = ""; Draw(); }));
            AddVoiceControls(scroll);
        }
        private Button ActivityButton(ActivityDefinition activity)
        {
            var info = session.GetActivityInfo(activity.id);
            var button = Action(info.NameKey, () => RunActivity(activity));
            button.SetEnabled(screenDefinition.progression == GameProgression.Schedule ? plannedActivities.Count < 256 : info.Available);
            if (!info.Available) button.tooltip = ActivityBlockedReason(activity.id);
            if (project.translations.Exists(t => t.key == info.DescriptionKey && !string.IsNullOrWhiteSpace(t.text))) button.Add(Label(info.DescriptionKey));
            if (activity.successChance?.enabled == true || activity.progression?.enabled == true)
            {
                var detail = new Label("Lv. " + info.Level + " · " + info.SuccessPercent + "%");
                detail.style.fontSize = 12; button.Add(detail);
                if (!string.IsNullOrEmpty(info.LevelNameKey)) button.Add(Label(info.LevelNameKey));
            }
            return button;
        }
        private void RunActivity(ActivityDefinition activity)
        {
            message = "";
            if (screenDefinition.progression == GameProgression.Schedule)
            { plannedActivities.Add(activity.id); Draw(); }
            else host.StartActivity(activity.id);
        }
        private async void OnHostChanged()
        {
            if (lifetime == null || lifetime.IsCancellationRequested) return;
            Draw();
            if (runningModule || host.Module == null) return;
            runningModule = true;
            try
            {
                while (host.Module != null && !lifetime.IsCancellationRequested)
                {
                    var module = project.modules.Find(m => m.id == host.Module.ModuleId);
                    if (module == null) throw new InvalidOperationException("Missing module definition.");
                    await host.RunModuleAsync(new CustomSceneAdapter(module.id, module.scenePath), lifetime.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e, this); message = e.Message; }
            finally { runningModule = false; if (!lifetime.IsCancellationRequested) Draw(); }
        }
        private Label Label(string key)
        {
            var label = new Label();
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 10;
            if (key == "ui.continue" && !project.translations.Exists(t => t.key == key))
            {
                label.text = LocalizationSettings.SelectedLocale.Identifier.Code == "ko" ? "계속" : "Continue";
                return label;
            }
            if (!project.translations.Exists(t => t.key == key)) { label.text = key; return label; }
            if (project.translations.Where(t => t.key == key).All(t => string.IsNullOrEmpty(t.text))) return label;
            bindings.Add(new LocalizedLabelBinding(label, "Princess." + project.id, key));
            return label;
        }
        private Button Action(string key, Action callback)
        {
            var button = new Button(() => { try { callback(); } catch (Exception e) { Debug.LogException(e, this); message = e.Message; Draw(); } });
            button.Add(Label(key));
            button.style.minHeight = 40;
            button.style.marginBottom = 7;
            return button;
        }
        private void Update()
        {
            if (screenSettings != null && (screenSettings != renderedScreen || screenSettings.Revision != screenRevision) && root != null) Draw();
        }
        private void OnDestroy()
        {
            if (host != null) host.Changed -= OnHostChanged;
            voice?.Stop();
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
            stage?.Dispose();
            lifetime?.Cancel();
            lifetime?.Dispose();
            fontBinding?.Dispose();
            foreach (var b in bindings)
                b.Dispose();
            bindings.Clear();
        }
        private void OnLocaleChanged(UnityEngine.Localization.Locale locale)
        {
            if (root != null && session != null) Draw();
        }
    }
}
