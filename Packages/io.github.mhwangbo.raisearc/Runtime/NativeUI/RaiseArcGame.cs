using System;
using System.Collections;
using System.IO;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Localization;
using RaiseArc.Unity;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace RaiseArc.UI
{
    [DisallowMultipleComponent, RequireComponent(typeof(RaiseArcDialogueVoice))]
    public sealed class RaiseArcGame : MonoBehaviour
    {
        [SerializeField] private GameProjectAsset project;
        [SerializeField] private PresentationSkin skin;
        [SerializeField] private uint seed = 1;
        [SerializeField] private string saveSlot = "weekly-plan";
        [SerializeField, Min(0)] private float planFeedbackSeconds = 0.3f;
        private Coroutine planRun;
        private RaiseArcDialogueVoice voice;
        private ProjectDefinition definition;
        public GameProjectAsset Project => project;
        public PresentationSkin Skin => skin;
        public RaiseArcSessionHost Host { get; private set; }
        public long Version { get; private set; }
        public string Error { get; private set; } = "";
        public string Locale => LocalizationSettings.SelectedLocale?.Identifier.Code ?? definition?.defaultLocale ?? "en";
        internal ProjectDefinition Definition => definition;
        public event Action Changed;
        public bool IsRunningPlan => planRun != null;

        public void Configure(GameProjectAsset asset, PresentationSkin presentation)
        {
            if (Host != null) throw new InvalidOperationException("Configure the game before initializing its host.");
            project = asset; skin = presentation;
        }
        private void Awake() { if (Application.isPlaying) Initialize(); }
        public void Initialize()
        {
            if (Host != null) return;
            if (project == null) { Error = "Select a RaiseArc Game Project. / 게임 프로젝트를 선택하세요."; return; }
            definition = project.Read();
            Host = new RaiseArcSessionHost(project, Path.Combine(Application.persistentDataPath, "PrincessStudio", definition.id), seed);
            voice = GetComponent<RaiseArcDialogueVoice>();
            voice.Configure(definition);
            Host.Changed += Notify;
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            Error = ""; Notify();
        }
        private void LocaleChanged(Locale locale) => Notify();
        private void Notify()
        {
            Version++;
            voice?.Present(Host.Session);
            Changed?.Invoke();
        }
        public string Text(string key) => Host.Text(key, Locale);
        public Sprite Image(string key) => skin == null || string.IsNullOrEmpty(key) ? null : skin.Find(key, Locale, definition.fallbackLocale);
        public void Save() => Host.Save(saveSlot);
        public void RunPlan()
        {
            if (planRun != null) return;
            if (!Host.Plan.confirmed || Host.Wait != SessionWait.Paused || Host.ScheduleCursor >= Host.Schedule.Count)
                throw new InvalidOperationException("Confirm a plan before starting its continuous run.");
            planRun = StartCoroutine(ContinuePlan());
        }
        private IEnumerator ContinuePlan()
        {
            try
            {
                yield return null;
                while (Host.Wait == SessionWait.Paused && Host.ScheduleCursor < Host.Schedule.Count)
                {
                    Host.Resume();
                    if (Host.Wait != SessionWait.Paused || Host.ScheduleCursor >= Host.Schedule.Count) break;
                    if (planFeedbackSeconds > 0) yield return new WaitForSecondsRealtime(planFeedbackSeconds);
                    else yield return null;
                }
            }
            finally
            {
                if (planRun != null)
                {
                    planRun = null;
                    Changed?.Invoke();
                }
            }
        }
        public void StopPlanRun()
        {
            if (planRun == null) return;
            var running = planRun;
            planRun = null;
            StopCoroutine(running);
            Changed?.Invoke();
        }
        public void Load() { StopPlanRun(); voice?.Stop(); Host.Load(saveSlot); }
        public void Restart() { StopPlanRun(); voice?.Stop(); Host.Restart(seed); }
        private void OnDisable() => StopPlanRun();
        private void OnDestroy()
        {
            StopPlanRun();
            if (Host != null) Host.Changed -= Notify;
            LocalizationSettings.SelectedLocaleChanged -= LocaleChanged;
            voice?.Stop();
        }
    }
}
