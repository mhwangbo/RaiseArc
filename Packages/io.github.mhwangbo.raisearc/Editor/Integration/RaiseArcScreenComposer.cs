using System;
using System.Linq;
using PrincessStudio.Unity;
using RaiseArc.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public sealed class ScreenComposer : EditorWindow
    {
        [SerializeField] private RaiseArcGameScreenSettings settings;
        private GameScreenDefinition draft;
        private int revision;
        private ScreenPart selected;
        private RaiseArcSessionHost host;
        private PlanningRehearsal plan;
        private readonly ComposedScreen screen = new ComposedScreen();
        private VisualElement canvas, inspector;
        private Label status;
        private bool korean = true, edit = true, dirty;

        [MenuItem("Window/RaiseArc/Screen composer")]
        public static void Open() => GetWindow<ScreenComposer>(typeof(SceneView)).Show();

        public static void OpenSettings(RaiseArcGameScreenSettings value)
        { var w = GetWindow<ScreenComposer>(typeof(SceneView)); if (w.dirty && w.settings != value) { w.Show(); if (w.status != null) w.status.text = "Save the layout before switching settings. / 설정을 바꾸기 전에 화면을 저장하세요."; return; } w.settings = value; w.CreateGUI(); w.Show(); }

        public void CreateGUI()
        {
            titleContent = new GUIContent("RaiseArc · Screen composer");
            minSize = new Vector2(1100, 650);
            rootVisualElement.Clear();
            var toolbar = new Toolbar(); rootVisualElement.Add(toolbar);
            var asset = new ObjectField("Screen settings") { objectType = typeof(RaiseArcGameScreenSettings), value = settings };
            asset.style.width = 340;
            asset.RegisterValueChangedCallback(e => { if (dirty) { asset.SetValueWithoutNotify(settings); status.text = "Save the layout before switching settings. / 설정을 바꾸기 전에 화면을 저장하세요."; return; } settings = e.newValue as RaiseArcGameScreenSettings; Reload(); }); toolbar.Add(asset);
            toolbar.Add(new ToolbarButton(Reload) { text = "Reload / 다시 읽기" });
            toolbar.Add(new ToolbarButton(Save) { text = "Save layout / 화면 저장" });
            var tools = new Toolbar(); rootVisualElement.Add(tools);
            tools.Add(new ToolbarButton(DuplicateLayout) { text = "Duplicate layout / 화면 복제" });
            tools.Add(new ToolbarButton(ApplyToScene) { text = "Apply to Scene / Scene에 적용" });
            tools.Add(new ToolbarButton(() => { if (settings != null) TimePlanEditor.Open(settings.Project); }) { text = "Time rules / 시간 규칙" });
            tools.Add(new ToolbarButton(() => { try { SimulationJobs.OpenPlan(settings.Project, host.Plan, host.Session.Capture()); status.text = "Opened Simulation Explorer / 시뮬레이션 탐색기를 열었습니다."; } catch (Exception ex) { status.text = ex.Message; } }) { text = "Analyze this plan / 이 계획 분석" });
            var locale = new ToolbarToggle { text = "한국어", value = korean };
            locale.RegisterValueChangedCallback(e => { korean = e.newValue; Render(); }); toolbar.Add(locale);
            var mode = new ToolbarToggle { text = "Edit layout / 배치 편집", value = edit };
            mode.RegisterValueChangedCallback(e => { edit = e.newValue; Render(); }); toolbar.Add(mode);
            status = new Label(); status.style.whiteSpace = WhiteSpace.Normal; status.style.paddingLeft = 8; rootVisualElement.Add(status);
            var body = new VisualElement(); body.style.flexDirection = FlexDirection.Row; body.style.flexGrow = 1; rootVisualElement.Add(body);
            canvas = new VisualElement(); canvas.style.flexGrow = 1; canvas.style.minWidth = 680; body.Add(canvas);
            inspector = new ScrollView(); inspector.style.width = 320; inspector.style.paddingLeft = inspector.style.paddingRight = 10; body.Add(inspector);
            if (dirty && draft != null) { Render(); Inspect(); }
            else Reload();
        }
        private void OnEnable() { Undo.undoRedoPerformed += Reload; }
        private void OnDisable() { Undo.undoRedoPerformed -= Reload; }
        private void Reload()
        {
            if (canvas == null) return;
            if (dirty) { status.text = "Save the layout before reloading. / 다시 읽기 전에 화면을 저장하세요."; return; }
            if (settings == null) { canvas.Clear(); inspector.Clear(); status.text = "Create a basic game screen first, then select its ScreenSettings asset. / 기본 게임 화면을 만든 뒤 ScreenSettings 에셋을 선택하세요."; return; }
            draft = settings.Read(); revision = settings.Revision; dirty = false;
            if (draft.parts.Count == 0) { draft.parts = ScreenComposition.WeeklyStarter(); dirty = true; }
            host = new RaiseArcSessionHost(settings.Project, System.IO.Path.Combine(Application.persistentDataPath, "RaiseArcLayoutRehearsals", settings.Project.Read().id), 1);
            plan = settings.Project.Read().time.periodNameKeys.Count == 0 ? new PlanningRehearsal(settings.Project.Read().activities.Select(a => a.id)) : null;
            if (plan != null) host.RegisterSaveParticipant(plan);
            selected = draft.parts.FirstOrDefault(); Render(); Inspect();
        }
        private void Save()
        {
            if (settings == null || draft == null) return;
            try
            {
                draft.compositionPreview = true;
                settings = GameScreenAuthoring.Save(settings.Project, settings.Skin, draft, revision, settings);
                revision = settings.Revision; dirty = false; Render();
            }
            catch (Exception e) { status.text = e.Message; }
        }
        private void Render()
        {
            if (canvas == null || draft == null || settings == null) return;
            var locale = korean ? "ko" : "en";
            var skin = settings.Skin;
            screen.Draw(canvas, draft, host, plan, locale,
                skin?.Find("screen.character", locale, "en"), skin?.Find("screen.background", locale, "en"),
                save: () => host.Save("draft"), load: () => host.Load("draft"),
                statName: id => host.Text(settings.Project.Read().stats.Find(s => s.id == id).nameKey, locale), skin: skin);
            status.text = (dirty ? "● Unsaved layout / 미저장 화면" : "Saved layout / 저장된 화면") + " · revision " + revision +
                (edit ? " · Drag parts; edit size at right. / 부품을 드래그하거나 오른쪽에서 크기를 바꾸세요." : " · Click a slot, then a card. / 칸을 누른 뒤 활동 카드를 누르세요.");
            if (!edit) return;
            var surface = canvas.Q("composition-canvas");
            foreach (var part in draft.parts)
            {
                var box = surface.Q(part.id);
                var handle = new VisualElement(); handle.style.position = Position.Absolute;
                handle.style.left = handle.style.top = handle.style.right = handle.style.bottom = 0;
                handle.style.borderLeftWidth = handle.style.borderRightWidth = handle.style.borderTopWidth = handle.style.borderBottomWidth = 1;
                var c = part == selected ? new Color(.85f, .69f, .39f) : new Color(1, 1, 1, .14f);
                handle.style.borderLeftColor = handle.style.borderRightColor = handle.style.borderTopColor = handle.style.borderBottomColor = c;
                box.Add(handle);
                Vector2 start = default; Rect before = default;
                handle.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; selected = part; start = e.position; before = part.bounds; handle.CapturePointer(e.pointerId); Inspect(); e.StopPropagation(); });
                handle.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (!handle.HasPointerCapture(e.pointerId)) return;
                    var delta = (Vector2)e.position - start;
                    part.bounds.x = Mathf.Clamp(before.x + delta.x / surface.resolvedStyle.width * 100, 0, 100 - part.bounds.width);
                    part.bounds.y = Mathf.Clamp(before.y + delta.y / surface.resolvedStyle.height * 100, 0, 100 - part.bounds.height);
                    box.style.left = Length.Percent(part.bounds.x); box.style.top = Length.Percent(part.bounds.y); dirty = true;
                });
                handle.RegisterCallback<PointerUpEvent>(e => { if (!handle.HasPointerCapture(e.pointerId)) return; handle.ReleasePointer(e.pointerId); Render(); Inspect(); });
            }
        }
        private void Inspect()
        {
            inspector.Clear();
            inspector.Add(new Label("PARTS / 화면 부품"));
            foreach (var part in draft.parts)
            {
                var p = part;
                inspector.Add(new Button(() => { selected = p; Render(); Inspect(); }) { text = (p == selected ? "● " : "") + (korean ? p.titleKo : p.title) });
            }
            var add = new EnumField("Add / 추가", ScreenPartKind.Panel); inspector.Add(add);
            inspector.Add(new Button(() => { selected = ScreenComposition.Part(Guid.NewGuid().ToString("N"), (ScreenPartKind)add.value, "New part", "새 부품", 30, 30, 30, 20); draft.parts.Add(selected); Changed(); Inspect(); }) { text = "+ Add part / 부품 추가" });
            if (selected == null) return;
            inspector.Add(new Label("SELECTED / 선택한 부품"));
            Text("Name EN", selected.title, v => selected.title = v);
            Text("이름 KO", selected.titleKo, v => selected.titleKo = v);
            Decimal("Left % / 왼쪽", selected.bounds.x, v => selected.bounds.x = v);
            Decimal("Top % / 위쪽", selected.bounds.y, v => selected.bounds.y = v);
            Decimal("Width % / 너비", selected.bounds.width, v => selected.bounds.width = v);
            Decimal("Height % / 높이", selected.bounds.height, v => selected.bounds.height = v);
            var fg = new ColorField("Text / 글자색") { value = selected.foreground };
            fg.RegisterValueChangedCallback(e => { selected.foreground = e.newValue; Changed(); }); inspector.Add(fg);
            var bg = new ColorField("Panel / 바탕색") { value = selected.background };
            bg.RegisterValueChangedCallback(e => { selected.background = e.newValue; Changed(); }); inspector.Add(bg);
            Number("Font / 글자 크기", selected.fontSize, n => selected.fontSize = n);
            Number("Padding / 안쪽 여백", selected.padding, n => selected.padding = n);
            if (selected.kind == ScreenPartKind.Activities)
            {
                var categories = settings.Project.Read().activities.Select(a => string.IsNullOrEmpty(a.categoryId) ? a.category.ToString() : a.categoryId).Distinct().ToList(); categories.Insert(0, "");
                var category = new DropdownField("Activity group / 활동 분류", categories, Math.Max(0, categories.IndexOf(selected.category)));
                category.RegisterValueChangedCallback(e => { selected.category = e.newValue; Changed(); }); inspector.Add(category);
            }
            if (selected.kind == ScreenPartKind.Activities || selected.kind == ScreenPartKind.Timetable) RepeatedStyle();
            if (selected.kind == ScreenPartKind.Button)
            {
                var action = new EnumField("Action / 버튼 동작", selected.action);
                action.RegisterValueChangedCallback(e => { selected.action = (ScreenButtonAction)e.newValue; Changed(); }); inspector.Add(action);
            }
            inspector.Add(new Button(() => { var copy = JsonUtility.FromJson<ScreenPart>(JsonUtility.ToJson(selected)); copy.id = Guid.NewGuid().ToString("N"); draft.parts.Add(copy); selected = copy; Changed(); Inspect(); }) { text = "Duplicate / 복제" });
            inspector.Add(new Button(() => { draft.parts.Remove(selected); draft.parts.Add(selected); Changed(); Inspect(); }) { text = "Bring to front / 맨 앞으로" });
            inspector.Add(new Button(() => { draft.parts.Remove(selected); selected = draft.parts.FirstOrDefault(); Changed(); Inspect(); }) { text = "Remove part / 부품 제거" });
        }

        private void DuplicateLayout()
        {
            if (settings == null || dirty) { status.text = "Save this layout before duplicating. / 먼저 화면을 저장하세요."; return; }
            var path = EditorUtility.SaveFilePanelInProject("Duplicate screen layout", settings.name + "-Alternative", "asset", "Same game, separate layout.");
            if (string.IsNullOrEmpty(path)) return;
            var copy = CreateInstance<RaiseArcGameScreenSettings>(); copy.Write(settings.Project, settings.Skin, settings.Read());
            AssetDatabase.CreateAsset(copy, path); Undo.RegisterCreatedObjectUndo(copy, "Duplicate RaiseArc screen"); AssetDatabase.SaveAssetIfDirty(copy);
            settings = copy; CreateGUI();
        }
        private void ApplyToScene()
        {
            try
            {
                if (dirty) throw new InvalidOperationException("Save the layout first. / 화면부터 저장하세요.");
                var controllers = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(c => c.GetType().FullName == "PrincessStudio.Samples.SampleGameController")
                    .Select(c => new SerializedObject(c)).Where(o => o.FindProperty("projectAsset").objectReferenceValue == settings.Project).ToArray();
                if (controllers.Length != 1) throw new InvalidOperationException("Open the game's scene with one game controller. / 이 게임 컨트롤러가 하나 있는 Scene을 여세요.");
                var serialized = controllers[0];
                serialized.FindProperty("screenSettings").objectReferenceValue = settings; serialized.ApplyModifiedProperties();
                var controller = (MonoBehaviour)serialized.targetObject;
                if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
                status.text = "Layout applied; game and plan unchanged. Save the Scene outside Play mode. / 계획을 유지한 채 적용했습니다. Play 종료 후 Scene에도 적용·저장하세요.";
            }
            catch (Exception ex) { status.text = ex.Message; }
        }
        private void RepeatedStyle()
        {
            var p = selected;
            inspector.Add(new Label(p.kind == ScreenPartKind.Activities ? "REPEATED CARD / 반복 카드" : "TIMETABLE CELL / 일정 칸"));
            inspector.Add(new Label(p.kind == ScreenPartKind.Activities ? "Data: activity fields · Click: place in selected slot" : "Data: host plan · Click: select time"));
            if (p.kind == ScreenPartKind.Activities)
            {
                Toggle("Horizontal list / 가로 목록", p.horizontalCards, v => p.horizontalCards = v);
                Toggle("Fields in a row / 내부 가로 배치", p.horizontalFields, v => p.horizontalFields = v);
                Number("Card width / 너비", p.cardWidth, v => p.cardWidth = v);
                Number("Card minimum height / 높이", p.cardHeight, v => p.cardHeight = v);
                Number("Image size / 이미지 크기", p.imageSize, v => p.imageSize = v);
                foreach (CardField field in Enum.GetValues(typeof(CardField)))
                {
                    var item = field;
                    Toggle(field + " / 표시", p.cardFields.Contains(field), v => { if (v) p.cardFields.Add(item); else p.cardFields.Remove(item); });
                }
                for (var i = 0; i < p.cardFields.Count; i++)
                {
                    var position = i;
                    var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                    row.Add(new Label((i + 1) + ". " + p.cardFields[i]));
                    var up = new Button(() => { var field = p.cardFields[position]; p.cardFields.RemoveAt(position); p.cardFields.Insert(position - 1, field); Changed(); Inspect(); }) { text = "↑" };
                    up.SetEnabled(i > 0); row.Add(up); inspector.Add(row);
                }
                Number("Card padding / 카드 여백", p.itemPadding, v => p.itemPadding = v);
            }
            else
            {
                Number("Cell height / 칸 높이", p.slotHeight, v => p.slotHeight = v);
                Color("Selected / 선택", p.selectedBackground, v => p.selectedBackground = v);
                Color("Selected text / 선택 글자", p.selectedText, v => p.selectedText = v);
                Color("Placed / 배치됨", p.placedBackground, v => p.placedBackground = v);
                Color("Locked / 잠김", p.lockedBackground, v => p.lockedBackground = v);
                Color("Current / 실행 위치", p.runningBackground, v => p.runningBackground = v);
                Color("Problem / 문제", p.problemBackground, v => p.problemBackground = v);
            }
            Color("Item background / 기본 바탕", p.itemBackground, v => p.itemBackground = v);
            Color("Item text / 기본 글자", p.itemText, v => p.itemText = v);
        }
        private void Toggle(string label, bool value, Action<bool> set)
        { var f = new Toggle(label) { value = value }; f.RegisterValueChangedCallback(e => { set(e.newValue); Changed(); }); inspector.Add(f); }
        private void Color(string label, Color value, Action<Color> set)
        { var f = new ColorField(label) { value = value }; f.RegisterValueChangedCallback(e => { set(e.newValue); Changed(); }); inspector.Add(f); }

        private void Changed() { dirty = true; Render(); }
        private void Text(string label, string value, Action<string> set)
        { var f = new TextField(label) { value = value }; f.RegisterValueChangedCallback(e => { set(e.newValue); Changed(); }); inspector.Add(f); }
        private void Decimal(string label, float value, Action<float> set)
        { var f = new FloatField(label) { value = value }; f.RegisterValueChangedCallback(e => { set(e.newValue); Changed(); }); inspector.Add(f); }
        private void Number(string label, int value, Action<int> set)
        { var f = new IntegerField(label) { value = value }; f.RegisterValueChangedCallback(e => { set(e.newValue); Changed(); }); inspector.Add(f); }
    }
}
