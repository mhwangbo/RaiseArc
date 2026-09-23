using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public static class StudioIntegrations
    {
        public static Action<GameProjectAsset> SyncLocalization;
        public static Action<GameProjectAsset> CreateBasicGameScreen;
    }
    public sealed partial class StudioWindow : EditorWindow
    {
        [SerializeField] private GameProjectAsset asset;
        [SerializeField] private string page = "Overview";
        [SerializeField] private string selectedId = "";
        private AuthoringService service;
        private VisualElement content;
        private Label status;
        private GameSession preview;
        [SerializeField] private string overviewDraftJson = "";
        private int previewDuration, previewRevision;
        private readonly UnityProjectCodec codec = new UnityProjectCodec();
        private static readonly string[] Pages = { "Overview", "Character", "Actors", "Appearance profiles", "Appearance rules", "Stage slots", "Game screen", "Stats & states", "Activities", "Schedule", "Events", "Relationships", "Items & flags", "Timed modifiers", "Endings", "Growth", "Modules", "Localization", "Test play", "Validation", "LLM commands" };

        [MenuItem("Window/RaiseArc/Open Studio")]
        public static void Open()
        {
            var window = GetWindow<StudioWindow>();
            window.titleContent = new GUIContent("RaiseArc");
            window.minSize = new Vector2(980, 640);
            window.Show();
        }
        [MenuItem("Window/RaiseArc/기본 편집기 (한국어)")]
        public static void OpenKoreanStudio() { StudioText.Language = "ko"; Open(); GetWindow<StudioWindow>().CreateGUI(); }
        public static StudioWindow OpenProject(GameProjectAsset project)
        {
            Open();
            var w = GetWindow<StudioWindow>();
            w.SetProject(project);
            w.Reload();
            return w;
        }
        public static StudioWindow OpenProject(GameProjectAsset project, string startPage)
        {
            var window = OpenProject(project); window.page = startPage; window.Render(); return window;
        }
        private void SetProject(GameProjectAsset project)
        {
            if (!AuthoringPipeServer.IsTarget(project)) AuthoringPipeServer.Stop();
            asset = project;
        }
        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            var dir = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            dir = Path.GetDirectoryName(dir).Replace('\\', '/');
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(dir + "/UI/Studio.uxml");
            if (tree == null)
            {
                root.Add(new HelpBox(StudioText.T("RaiseArc UI assets are missing."), HelpBoxMessageType.Error));
                return;
            }
            tree.CloneTree(root);
            StudioText.ApplyFont(root);
            root.Query<Label>().ForEach(label => label.text = StudioText.T(label.text));
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(dir + "/UI/Studio.uss");
            if (sheet != null)
                root.styleSheets.Add(sheet);
            content = root.Q("content");
            status = root.Q<Label>("status");
            var toolbar = root.Q("toolbar");
            toolbar.Add(StudioText.LanguagePicker(CreateGUI, () => LeaveOverview() && StudioText.Dialog(StudioText.T("Language"), StudioText.T("Apply changes before changing the editor language. Continue?"), StudioText.T("Continue"), StudioText.T("Cancel"))));
            var picker = new ObjectField { objectType = typeof(GameProjectAsset), allowSceneObjects = false, value = asset };
            picker.style.width = 250;
            picker.RegisterValueChangedCallback(e => { if (!LeaveOverview()) { picker.SetValueWithoutNotify(asset); return; } SetProject(e.newValue as GameProjectAsset); Reload(); });
            toolbar.Add(picker);
            toolbar.Add(Button("New project", NewProject));
            toolbar.Add(Button("Create editable example", OpenSample));
            toolbar.Add(Button("Import JSON", ImportJson));
            toolbar.Add(Button("Export JSON", ExportJson));
            toolbar.Add(Button("Graph Workbench", () =>
            {
                if (!LeaveOverview()) return;
                var type = Type.GetType("PrincessStudio.Editor.Graph.GraphWorkbenchWindow, PrincessStudio.Editor.Graph");
                if (type == null) { Notify("Graph Workbench editor adapter is not installed."); return; }
                type.GetMethod("Open", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { asset });
            }));
            var nav = root.Q("navigation");
            foreach (var name in Pages)
            {
                var item = Button(name, () => { if (!LeaveOverview()) return; page = name; selectedId = ""; Render(); });
                item.AddToClassList("nav-button");
                item.name = "nav-" + name;
                nav.Add(item);
            }
            Reload();
        }
        private void OnEnable()
        {
            Undo.undoRedoPerformed += ReloadAfterUndo;
        }
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= ReloadAfterUndo;
        }
        private void Reload()
        {
            if (content == null)
                return;
            service = asset == null ? null : new AuthoringService(asset.Read(), codec, asset.CreateExtensions());
            preview = null;
            Render();
        }
        private void ReloadAfterUndo() { RaiseArc.Editor.RaiseArcVoiceField.Sync(asset); Reload(); }
        private void Render()
        {
            if (content == null)
                return;
            content.Clear();
            foreach (var name in Pages)
                rootVisualElement.Q<Button>("nav-" + name)?.EnableInClassList("selected", name == page);
            if (service == null)
            {
                Heading("Your next story starts here", "Build characters, schedules and branching lives from one workspace.");
                content.Add(Button("Create a project", NewProject, true));
                content.Add(Button("Create editable example", OpenSample));
                return;
            }
            var project = service.Snapshot();
            Heading(page, "Project / " + project.id + "     •     Revision " + service.Revision);
            switch (page)
            {
                case "Game screen":
                    DrawGameScreen(project);
                    break;
                case "Overview":
                    DrawOverview(project);
                    break;
                case "Localization":
                    DrawLocalization(project);
                    break;
                case "Validation":
                    DrawValidation(project);
                    break;
                case "LLM commands":
                    DrawCommands();
                    break;
                case "Test play":
                    DrawPreview(project);
                    break;
                default:
                    DrawCatalog(project);
                    break;
            }
        }
        private void Heading(string title, string subtitle)
        {
            var h = new Label(StudioText.T(title));
            h.AddToClassList("section-title");
            content.Add(h);
            var s = new Label(StudioText.T(subtitle));
            s.AddToClassList("subtitle");
            content.Add(s);
        }
        private static Button Button(string text, Action action, bool primary = false)
        {
            var b = new Button { text = StudioText.T(text )};
            b.clicked += () =>
            {
                // Delayed fields must commit before an action snapshots or rebuilds the form.
                b.panel?.focusController?.focusedElement?.Blur();
                action();
            };
            if (primary)
                b.AddToClassList("primary");
            return b;
        }
        private static VisualElement Row()
        {
            var r = new VisualElement();
            r.AddToClassList("row");
            return r;
        }
        private void Notify(string message)
        {
            if (status != null)
                status.text = StudioText.T(message);
        }
        private void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e) { Notify(e.Message); Debug.LogWarning("[RaiseArc] " + e.Message); }
        }
        private void Mutate(Action action, Action onSaved = null)
        {
            Safe(() => { if (asset.Read().revision != service.Revision) throw new InvalidOperationException("Project changed externally. Refresh before applying this draft."); action(); Undo.RecordObject(asset, "Edit RaiseArc project"); asset.Write(service.Snapshot()); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); onSaved?.Invoke(); RaiseArc.Editor.RaiseArcVoiceField.Sync(asset); preview = null; Notify("Saved • revision " + service.Revision); Render(); });
        }
        private void NewProject()
        {
            if (!LeaveOverview()) return;
            var path = EditorUtility.SaveFilePanelInProject("Create RaiseArc project", "GameProject", "asset", "Choose where to save your game data.");
            if (path.Length == 0)
                return;
            SetProject(CreateInstance<GameProjectAsset>());
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            CreateGUI();
        }
        private void OpenSample()
        {
            if (!LeaveOverview()) return;
            Safe(() =>
            {
                var path = EditorUtility.SaveFilePanelInProject("Create editable example", "MyRaiseArcGame", "asset", "A separate editable project will be created.");
                if (path.Length == 0)
                    return;
                var created = CreateInstance<GameProjectAsset>();
                created.Write(RaiseArc.Editor.EndingTestExample.Definition());
                AssetDatabase.CreateAsset(created, path);
                AssetDatabase.SaveAssets();
                SetProject(created);
                CreateGUI();
            });
        }
        private void ImportJson()
        {
            if (!LeaveOverview()) return;
            if (service == null)
            {
                Notify("Create or select a project first.");
                return;
            }
            var path = EditorUtility.OpenFilePanel("Import project JSON", "", "json");
            if (path.Length > 0)
                Mutate(() => service.Replace(codec.FromJson(File.ReadAllText(path), asset.CreateExtensions()), service.Revision));
        }
        private void ExportJson()
        {
            if (service == null)
                return;
            var path = EditorUtility.SaveFilePanel("Export project JSON", "", asset.name + ".json", "json");
            if (path.Length > 0)
                Safe(() => { File.WriteAllText(path, codec.ToJson(service.Snapshot())); Notify("Exported " + path); });
        }
        private void DrawOverview(ProjectDefinition p)
        {
            if (!string.IsNullOrEmpty(overviewDraftJson)) p = codec.FromJson(overviewDraftJson, asset.CreateExtensions());
            var metrics = Row();
            Metric(metrics, p.activities.Count.ToString(), "ACTIVITIES");
            Metric(metrics, p.events.Count.ToString(), "STORY EVENTS");
            Metric(metrics, p.endings.Count.ToString(), "ENDINGS");
            Metric(metrics, p.locales.Count.ToString(), "LANGUAGES");
            content.Add(metrics);
            var card = new VisualElement();
            card.AddToClassList("card");
            card.Add(StyledLabel("A complete life, one decision at a time", "card-title"));
            card.Add(StyledLabel("Start with your character and stats. Build activities, connect story events, then explore the possible futures in Test play.", "subtitle"));
            var actions = Row();
            actions.Add(Button("Test your story", () => { if (!LeaveOverview()) return; page = "Test play"; Render(); }, true));
            actions.Add(Button("Check project", () => { if (!LeaveOverview()) return; page = "Validation"; Render(); }));
            card.Add(actions);
            content.Add(card);
            var settings = new ScrollView();
            settings.style.flexGrow = 1;
            content.Add(settings);
            var saved = new Label(); saved.style.whiteSpace = WhiteSpace.Normal; settings.Add(saved);
            void Draft()
            {
                overviewDraftJson = codec.ToJson(p); hasUnsavedChanges = true;
                saveChangesMessage = StudioText.T("Game settings have not been saved. Save before starting a new test.");
                saved.text = saveChangesMessage + " " + StudioText.T("Saved game duration (days)") + ": " + asset.Read().durationDays;
            }
            saved.text = StudioText.T("Saved game duration (days)") + ": " + asset.Read().durationDays;
            Text(settings, "Project ID", p.id, x => p.id = x);
            Number(settings, "Days per month", p.daysPerMonth, x => p.daysPerMonth = x);
            Number(settings, "Months per year", p.monthsPerYear, x => p.monthsPerYear = x);
            Number(settings, "Game duration (days)", p.durationDays, x => p.durationDays = x);
            Number(settings, "Starting money", p.startingMoney, x => p.startingMoney = x);
            var frequencies = new List<string> { StudioText.T("OncePerActivity"), StudioText.T("OncePerDay") };
            var frequency = new PopupField<string>(StudioText.T("Default activity check frequency"), frequencies,
                p.defaultActivityCheckFrequency == RaiseArc.Core.ActivityCheckFrequency.OncePerDay ? 1 : 0);
            frequency.RegisterValueChangedCallback(e => { p.defaultActivityCheckFrequency = frequencies.IndexOf(e.newValue) == 1
                ? RaiseArc.Core.ActivityCheckFrequency.OncePerDay : RaiseArc.Core.ActivityCheckFrequency.OncePerActivity; Draft(); });
            settings.Add(frequency);
            settings.RegisterCallback<ChangeEvent<int>>(_ => Draft());
            settings.RegisterCallback<ChangeEvent<string>>(_ => Draft());
            settings.Add(Button("Save game settings", SaveChanges, true));
            settings.Add(new HelpBox(StudioText.T("Save settings before leaving this page. Saving starts a fresh Test play session; an already running Game view needs Stop, then Play."), HelpBoxMessageType.Info));
        }
        private bool LeaveOverview()
        {
            if (string.IsNullOrEmpty(overviewDraftJson)) return true;
            var choice = EditorUtility.DisplayDialogComplex(StudioText.T("Unsaved game settings"), StudioText.T("Game settings have not been saved. Save before starting a new test."), StudioText.T("Save"), StudioText.T("Cancel"), StudioText.T("Discard"));
            if (choice == 0) SaveChanges();
            else if (choice == 2) DiscardChanges();
            return !hasUnsavedChanges;
        }
        public override void SaveChanges()
        {
            if (string.IsNullOrEmpty(overviewDraftJson)) { base.SaveChanges(); return; }
            var draft = codec.FromJson(overviewDraftJson, asset.CreateExtensions());
            Mutate(() => service.Replace(draft, draft.revision), () => { overviewDraftJson = ""; base.SaveChanges(); });
        }
        public override void DiscardChanges()
        {
            overviewDraftJson = ""; base.DiscardChanges(); Render();
        }
        private static Label StyledLabel(string text, string css)
        {
            var label = new Label(StudioText.T(text));
            label.AddToClassList(css);
            return label;
        }
        private static void Metric(VisualElement row, string value, string label)
        {
            var box = new VisualElement();
            box.AddToClassList("metric");
            box.Add(StyledLabel(value, "metric-value"));
            box.Add(StyledLabel(label, "metric-label"));
            row.Add(box);
        }
        private static void Text(VisualElement parent, string label, string value, Action<string> set, bool multiline = false)
        {
            var f = new TextField(StudioText.T(label)) { value = value ?? "", multiline = multiline, isDelayed = !multiline };
            f.RegisterValueChangedCallback(e => set(e.newValue));
            parent.Add(f);
        }
        private static void Number(VisualElement parent, string label, int value, Action<int> set)
        {
            var f = new IntegerField(StudioText.T(label)) { value = value, isDelayed = true };
            f.RegisterValueChangedCallback(e => set(e.newValue));
            parent.Add(f);
        }
        private static void Toggle(VisualElement parent, string label, bool value, Action<bool> set)
        {
            var f = new Toggle(StudioText.T(label)) { value = value };
            f.RegisterValueChangedCallback(e => set(e.newValue));
            parent.Add(f);
        }
        private static void EnumValue<T>(VisualElement parent, string label, T value, Action<T> set) where T : Enum
        {
            var f = StudioText.EnumField(label, value);
            f.RegisterValueChangedCallback(e => set((T)e.newValue));
            parent.Add(f);
        }
        private static void StringList(VisualElement parent, string label, List<string> list)
        {
            Text(parent, label, string.Join(", ", list), value => { list.Clear(); list.AddRange(value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)); });
        }
        private static string PreviewText(ProjectDefinition p, string key, string locale = null)
        {
            var t = p.translations.Find(x => x.key == key && x.locale == (locale ?? p.defaultLocale)) ?? p.translations.Find(x => x.key == key && x.locale == p.fallbackLocale);
            return t?.text ?? "[" + key + "]";
        }
    }
}
