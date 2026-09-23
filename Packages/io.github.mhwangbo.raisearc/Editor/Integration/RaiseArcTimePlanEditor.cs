using System;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public sealed class TimePlanEditor : EditorWindow
    {
        [SerializeField] private GameProjectAsset asset;
        private ProjectDefinition draft;
        private int revision;
        private Label status;
        public static void Open(GameProjectAsset project)
        { var w = GetWindow<TimePlanEditor>(); w.asset = project; w.titleContent = new GUIContent("RaiseArc · Time rules"); w.CreateGUI(); w.Show(); }
        private void OnEnable() => Undo.undoRedoPerformed += CreateGUI;
        private void OnDisable() => Undo.undoRedoPerformed -= CreateGUI;
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var field = new ObjectField("Game project / 게임") { objectType = typeof(GameProjectAsset), value = asset };
            field.RegisterValueChangedCallback(e => { asset = e.newValue as GameProjectAsset; CreateGUI(); }); rootVisualElement.Add(field);
            if (asset == null) return;
            draft = asset.Read(); revision = draft.revision;
            var body = new ScrollView(); body.style.flexGrow = 1; rootVisualElement.Add(body);
            body.Add(new HelpBox("Time rules change the game, not just the display. Start a new session after saving; old saves are not converted.\n시간 규칙은 실제 게임 규칙입니다. 저장 후 새 세션을 시작하세요. 기존 세이브는 변환하지 않습니다.", HelpBoxMessageType.Info));
            Number(body, "Planning days / 계획 일수", draft.time.planningDays, v => draft.time.planningDays = v);
            Number(body, "Game duration days / 게임 기간", draft.durationDays, v => draft.durationDays = v);
            body.Add(new Label("Named periods in order / 하루 시간대 순서 (비우면 기존 일 단위)"));
            var periods = new VisualElement(); body.Add(periods);
            void Periods()
            {
                periods.Clear();
                for (var i = 0; i < draft.time.periodNameKeys.Count; i++)
                {
                    var index = i; var key = draft.time.periodNameKeys[i];
                    var row = new VisualElement(); periods.Add(row);
                    row.Add(new Label((i + 1) + ". " + key));
                    foreach (var language in draft.locales)
                    {
                        var locale = language;
                        var entry = draft.translations.Find(t => t.key == key && t.locale == locale);
                        var text = new TextField(locale) { value = entry?.text ?? "" };
                        text.RegisterValueChangedCallback(e => {
                            var current = draft.translations.Find(t => t.key == key && t.locale == locale);
                            if (current == null) draft.translations.Add(new TranslationEntry { key = key, locale = locale, text = e.newValue });
                            else current.text = e.newValue;
                        }); row.Add(text);
                    }
                    var remove = new Button(() => { draft.time.periodNameKeys.RemoveAt(index); Periods(); }) { text = "Remove period / 시간대 제거" }; row.Add(remove);
                }
            }
            Periods();
            body.Add(new Button(() => { draft.time.periodNameKeys.Add("time." + Guid.NewGuid().ToString("N")); Periods(); }) { text = "Add period / 시간대 추가" });
            body.Add(new Label("Activity duration / 활동 소요 시간 · 0 periods keeps its day duration.\n칸 수 0이면 기존 일수·일별 판정을 유지합니다. 칸 활동은 활동당 한 번 판정합니다."));
            foreach (var activity in draft.activities)
            {
                var a = activity;
                var name = draft.translations.Find(t => t.key == a.nameKey && t.locale == "ko")?.text ?? a.nameKey;
                Number(body, name + " · periods / 칸", a.periods, v => a.periods = v);
            }
            foreach (var ev in draft.events)
            {
                var e = ev;
                var toggle = new Toggle((draft.translations.Find(t => t.key == e.nameKey && t.locale == "ko")?.text ?? e.nameKey) + " · check each period / 시간대마다 검사") { value = e.evaluateEachPeriod };
                toggle.RegisterValueChangedCallback(change => e.evaluateEachPeriod = change.newValue); body.Add(toggle);
            }
            status = new Label(); status.style.whiteSpace = WhiteSpace.Normal; rootVisualElement.Add(status);
            rootVisualElement.Add(new Button(() => {
                try { Save(asset, draft, revision); CreateGUI(); status.text = "Saved. Reload composer and start a new game session. / 저장됨. Composer를 다시 읽고 새 게임 세션을 시작하세요."; }
                catch (Exception ex) { status.text = ex.Message; }
            }) { text = "Apply time rules / 시간 규칙 적용" });
            rootVisualElement.Add(new Button(CreateGUI) { text = "Reload / 다시 읽기" });
        }
        public static void Save(GameProjectAsset asset, ProjectDefinition value, int expectedRevision)
        {
            var service = new AuthoringService(asset.Read(), new UnityProjectCodec(), asset.CreateExtensions());
            service.Replace(value, expectedRevision);
            Undo.RecordObject(asset, "Edit RaiseArc time rules"); asset.Write(service.Snapshot());
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
        }
        private static void Number(VisualElement root, string label, int value, Action<int> set)
        { var f = new IntegerField(label) { value = value }; f.RegisterValueChangedCallback(e => set(e.newValue)); root.Add(f); }
    }
}
