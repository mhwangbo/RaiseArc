using System;
using System.Collections.Generic;
using PrincessStudio.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RaiseArc.Localization
{
    public enum DialogueVoiceState { Idle, Silent, Loading, Playing, Finished, Failed }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class RaiseArcDialogueVoice : MonoBehaviour
    {
        private ProjectDefinition project;
        private List<EventDefinition> events;
        private GameSession session;
        private int revision = -1, generation;
        private string locale = "";
        private AsyncOperationHandle<AudioClip> loading;
        private bool ownsHandle;
        private AudioSource source;
        public string LineId { get; private set; } = "";
        public string TextKey { get; private set; } = "";
        public string SpeakerActorId { get; private set; } = "";
        public DialogueVoiceState State { get; private set; }
        public AudioSource Source => source != null ? source : source = GetComponent<AudioSource>();
        public float Volume { get => Source.volume; set => Source.volume = Mathf.Clamp01(value); }
        public bool Muted { get => Source.mute; set => Source.mute = value; }
        public event Action<RaiseArcDialogueVoice> Changed;

        private void Awake() { Source.playOnAwake = false; Source.loop = false; Source.spatialBlend = 0; }
        private void OnEnable() => LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
        private void OnDisable() { LocalizationSettings.SelectedLocaleChanged -= LocaleChanged; Stop(); }
        private void LocaleChanged(Locale _) { if (session != null) Present(session); }

        public void Configure(ProjectDefinition definition)
        {
            Stop();
            project = definition ?? throw new ArgumentNullException(nameof(definition));
            events = project.events.ConvertAll(e => Core.RaiseArcFlowReuse.Expand(project, e));
        }

        // Call after presenting the current session state. Re-rendering that state is idempotent.
        public void Present(GameSession current)
        {
            if (!isActiveAndEnabled || project == null) return;
            var language = LocalizationSettings.SelectedLocale;
            var code = language == null ? project.defaultLocale : language.Identifier.Code;
            if (ReferenceEquals(session, current) && revision == current.State.Revision && locale == code) return;
            Stop(); session = current; revision = current.State.Revision; locale = code;
            var e = events.Find(x => x.id == current.State.PendingEventId);
            var line = e?.presentation.Find(x => x.id == current.State.PresentationStepId);
            if (line == null || line.kind != PresentationStepKind.Dialogue) return;
            LineId = line.id; TextKey = line.nameKey; SpeakerActorId = line.speakerActorId;
            var entry = project.localizedAssets.Find(x => x.key == line.voiceKey && x.locale == code);
            if (string.IsNullOrEmpty(line.voiceKey) || entry == null) { SetState(DialogueVoiceState.Silent); return; }
            if (language == null || string.IsNullOrEmpty(entry.assetGuid)) { SetState(DialogueVoiceState.Failed); return; }
            var request = generation;
            SetState(DialogueVoiceState.Loading);
            try
            {
                loading = LocalizationSettings.AssetDatabase.GetLocalizedAssetAsync<AudioClip>("Princess." + project.id + ".Assets", line.voiceKey, language, FallbackBehavior.DontUseFallback);
                Addressables.ResourceManager.Acquire(loading); ownsHandle = true;
                loading.Completed += op =>
                {
                    // A previous request may finish after a skip, load, locale change or scene exit.
                    if (this == null || request != generation || !isActiveAndEnabled) return;
                    if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null) { SetState(DialogueVoiceState.Failed); return; }
                    Source.clip = op.Result; Source.Play(); SetState(DialogueVoiceState.Playing);
                };
            }
            catch (Exception) { SetState(DialogueVoiceState.Failed); }
        }

        public void Stop()
        {
            ++generation;
            if (source != null) { source.Stop(); source.clip = null; }
            if (ownsHandle) { Addressables.Release(loading); ownsHandle = false; }
            session = null; revision = -1;
            LineId = TextKey = SpeakerActorId = "";
            SetState(DialogueVoiceState.Idle);
        }
        private void Update()
        {
            if (State == DialogueVoiceState.Playing && !Source.isPlaying && !AudioListener.pause) SetState(DialogueVoiceState.Finished);
        }
        private void SetState(DialogueVoiceState value) { State = value; Changed?.Invoke(this); }
    }
}
