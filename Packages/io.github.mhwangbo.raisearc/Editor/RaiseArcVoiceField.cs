using System;
using System.Linq;
using System.Reflection;
using PrincessStudio.Core;
using PrincessStudio.Editor;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public static class RaiseArcVoiceField
    {
        public static void Sync(GameProjectAsset asset)
        {
            if (asset != null && asset.Read().events.Any(e => e.presentation.Any(s => !string.IsNullOrEmpty(s.voiceKey)))) StudioIntegrations.SyncLocalization?.Invoke(asset);
        }
        private static readonly Type AudioPreview = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        private static bool previewing;
        static RaiseArcVoiceField()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += _ => Stop();
        }
        private static void Stop()
        {
            if (!previewing) return;
            previewing = false;
            AudioPreview?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
        }

        public static void Draw(VisualElement parent, ProjectDefinition project, PresentationStep line, string initialLocale, Action changed)
        {
            if (line.kind != PresentationStepKind.Dialogue || !string.IsNullOrEmpty(line.sharedStepId)) return;
            var ko = StudioText.T("Save") != "Save";
            var box = new Foldout { text = ko ? "대사 보이스" : "Dialogue voice", value = true };
            parent.Add(box);
            var language = new DropdownField(ko ? "편집·미리 듣기 언어" : "Editing / preview language", project.locales, Math.Max(0, project.locales.IndexOf(initialLocale)));
            box.Add(language);
            var field = new ObjectField(ko ? "음성 파일" : "Voice clip") { objectType = typeof(AudioClip), allowSceneObjects = false };
            var status = new HelpBox("", HelpBoxMessageType.Info);
            void Assign(string guid)
            {
                var api = new AuthoringService(project, new UnityProjectCodec());
                api.SetDialogueVoice(line.id, language.value, guid, api.Revision);
                var updated = api.Snapshot();
                line.voiceKey = updated.events.SelectMany(e => e.presentation).First(s => s.id == line.id).voiceKey;
                project.localizedAssets = updated.localizedAssets;
                changed();
            }
            void Refresh()
            {
                var entry = project.localizedAssets.Find(a => a.key == line.voiceKey && a.locale == language.value);
                var clip = entry == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(entry.assetGuid));
                field.SetValueWithoutNotify(clip);
                status.text = entry == null ? (ko ? "이 언어는 무음입니다. 다른 언어로 대체하지 않습니다." : "Silent in this language. No language fallback.")
                    : clip == null ? (ko ? "음성 참조가 깨졌습니다. 파일을 다시 지정하거나 제거하세요." : "Broken voice reference. Assign a clip again or remove it.")
                    : (ko ? "대사 저장 시 게임용 음성 테이블도 동기화됩니다." : "Saving the dialogue also syncs its game voice tables.");
                status.messageType = entry != null && clip == null ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
            }
            field.RegisterValueChangedCallback(e =>
            {
                Stop();
                var clip = e.newValue as AudioClip;
                var path = clip == null ? "" : AssetDatabase.GetAssetPath(clip);
                if (path.Contains("/Resources/")) { EditorUtility.DisplayDialog("RaiseArc", "Move voice clips outside a Resources folder before assigning them.", "OK"); Refresh(); return; }
                try { Assign(clip == null ? "" : AssetDatabase.AssetPathToGUID(path)); }
                catch (Exception ex) { EditorUtility.DisplayDialog("RaiseArc", ex.Message, "OK"); }
                Refresh();
            });
            box.Add(field); box.Add(status);
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } }; box.Add(buttons);
            buttons.Add(new Button(() =>
            {
                Stop();
                var clip = field.value as AudioClip;
                if (clip == null) return;
                var play = AudioPreview?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
                if (play == null) { status.text = "Audio preview is unavailable in this Unity version."; return; }
                play.Invoke(null, new object[] { clip, 0, false });
                previewing = true;
            }) { text = ko ? "미리 듣기" : "Preview voice" });
            buttons.Add(new Button(Stop) { text = ko ? "정지" : "Stop" });
            buttons.Add(new Button(() =>
            {
                Stop();
                try { Assign(""); } catch (Exception ex) { EditorUtility.DisplayDialog("RaiseArc", ex.Message, "OK"); }
                Refresh();
            }) { text = ko ? "이 언어 음성 제거" : "Remove voice in this language" });
            language.RegisterValueChangedCallback(_ => { Stop(); Refresh(); });
            box.RegisterCallback<DetachFromPanelEvent>(_ => Stop());
            Refresh();
        }
    }
}
