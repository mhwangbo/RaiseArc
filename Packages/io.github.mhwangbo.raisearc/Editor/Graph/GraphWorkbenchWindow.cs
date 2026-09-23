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

namespace PrincessStudio.Editor.Graph
{
    public sealed partial class GraphWorkbenchWindow : EditorWindow
    {
        [SerializeField] private GameProjectAsset asset;
        [SerializeField] private string eventId = "", selectedId = "", locale = "", query = "", panel = "Problems", mode = "Flow";
        [SerializeField] private string draftJson = "";
        [SerializeField] private bool graphSearch;
        [SerializeField] private PresentationSkin previewSkin;
        private GraphLayoutAsset layout;
        private AuthoringService api;
        private ProjectDefinition project, inspectorProject;
        private ContentIndex index;
        private AuthoringChangeSet changes;
        private ChangeSetPreview preview;
        private FlowCanvas canvas;
        private VisualElement center, inspector, diagnostics;
        private Label status, breadcrumb;
        private ToolbarSearchField search;
        private ListView explorer;
        private ToolbarToggle flowScope;
        private DropdownField explorerFilter;
        private string explorerKind = "All content";
        private readonly Dictionary<string, ToolbarToggle> panelTabs = new Dictionary<string, ToolbarToggle>();
        private readonly List<ContentEntry> rows = new List<ContentEntry>();
        private readonly List<string> history = new List<string>();
        private int historyIndex = -1;
        private bool inspectorDirty;
        private Label saveStatus;
        private Button saveButton;
        private double pollAt;
        private StateData preset;
        private ExecutionTrace trace;
        private string traceStartId = "";
        private ScrollView traceHost;
        private RaiseArc.Editor.RaiseArcTracePlayback tracePlayback;
        private readonly List<string> traceChoices = new List<string>();
        private readonly UnityProjectCodec codec = new UnityProjectCodec();
        private string clipboard = "";
        [MenuItem("Window/RaiseArc/Open Graph Workbench")]
        public static void OpenMenu() => Open(Selection.activeObject as GameProjectAsset);
        [MenuItem("Window/RaiseArc/그래프 워크벤치 (한국어)")]
        public static void OpenKorean() { StudioText.Language = "ko"; OpenMenu(); GetWindow<GraphWorkbenchWindow>().CreateGUI(); }
        public static void Open(GameProjectAsset project)
        {
            var window = GetWindow<GraphWorkbenchWindow>(); window.titleContent = new GUIContent("RaiseArc · Workbench"); window.minSize = new Vector2(1080, 680);
            window.asset = project; window.Show(); window.Reload();
        }
        private void OnEnable() { Undo.undoRedoPerformed += OnUndo; EditorApplication.update += Poll; }
        private void OnDisable() { Undo.undoRedoPerformed -= OnUndo; EditorApplication.update -= Poll; }
        private void OnUndo()
        {
            RaiseArc.Editor.RaiseArcVoiceField.Sync(asset);
            if (asset == null || api == null || asset.Revision != api.Revision || string.IsNullOrEmpty(draftJson)) { Reload(); return; }
            Safe(() => { changes = JsonUtility.FromJson<AuthoringChangeSet>(draftJson); preview = api.PreviewChangeSet(changes); project = preview.project; inspectorDirty = false; RefreshAll(); });
        }
        private void Poll()
        {
            if (EditorApplication.timeSinceStartup < pollAt) return; pollAt = EditorApplication.timeSinceStartup + .4;
            if (asset != null && api != null && asset.Revision != api.Revision)
            {
                if (inspectorDirty || changes.edits.Count > 0) Notify("Revision conflict: project changed externally. Draft retained; discard and reload before applying.");
                else Reload();
            }
        }
        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); root.AddToClassList("workbench");
            StudioText.ApplyFont(root);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.mhwangbo.raisearc/Editor/Graph/Workbench.uss"); if (sheet != null) root.styleSheets.Add(sheet);
            var masthead = new VisualElement(); masthead.AddToClassList("workbench-masthead");
            var brand = new Label("RaiseArc"); brand.AddToClassList("workbench-brand"); masthead.Add(brand);
            var subtitle = new Label(StudioText.T("Graph Workbench")); subtitle.AddToClassList("workbench-product"); masthead.Add(subtitle); root.Add(masthead);
            saveStatus = new Label { name = "save-status" }; saveStatus.style.flexGrow = 1; masthead.Add(saveStatus);
            saveButton = B("Save", ApplyChanges); saveButton.name = "save-content";
            saveButton.tooltip = StudioText.T("Save all content edits, including the fields you are editing. Ctrl/Cmd+S."); masthead.Add(saveButton);
            var toolbar = new Toolbar(); toolbar.AddToClassList("workbench-toolbar"); root.Add(toolbar);
            toolbar.Add(StudioText.LanguagePicker(CreateGUI, LeaveInspector));
            var projectPicker = new ObjectField { objectType = typeof(GameProjectAsset), value = asset, allowSceneObjects = false }; projectPicker.style.width = 200;
            projectPicker.RegisterValueChangedCallback(e => { if (!LeaveInspector()) { projectPicker.SetValueWithoutNotify(asset); return; } if (changes != null && changes.edits.Count > 0 && !StudioText.Dialog("Pending changes", "Discard the current change set and switch projects?", "Discard", "Cancel")) { projectPicker.SetValueWithoutNotify(asset); return; } asset = e.newValue as GameProjectAsset; preset = null; trace = null; traceChoices.Clear(); history.Clear(); selectedId = eventId = ""; Reload(); }); toolbar.Add(projectPicker);
            toolbar.Add(B("‹", () => NavigateHistory(-1))); toolbar.Add(B("›", () => NavigateHistory(1)));
            search = new ToolbarSearchField { value = query }; search.style.width = 230; search.RegisterValueChangedCallback(e => { query = e.newValue; RefreshExplorer(); }); toolbar.Add(search);
            search.tooltip = "Ctrl/Cmd+P: all content · Ctrl/Cmd+F: current flow";
            flowScope = new ToolbarToggle { text = StudioText.T("This flow"), value = graphSearch }; flowScope.RegisterValueChangedCallback(e => { graphSearch = e.newValue; RefreshExplorer(); }); toolbar.Add(flowScope);
            toolbar.Add(B("New event", CreateEvent));
            toolbar.Add(B("Favorite", ToggleFavorite)); toolbar.Add(B("Recent", () => ShowSaved(layout?.recent))); toolbar.Add(B("Favorites", () => ShowSaved(layout?.favorites)));
            toolbar.Add(B("Classic editor", () => { if (asset != null) StudioWindow.OpenProject(asset); }));
            breadcrumb = new Label(StudioText.T("RaiseArc")); breadcrumb.AddToClassList("workbench-muted"); root.Add(breadcrumb);
            var vertical = new TwoPaneSplitView(1, 160, TwoPaneSplitViewOrientation.Vertical) { viewDataKey = "workbench-bottom" }; vertical.style.flexGrow = 1; vertical.style.minHeight = 0; root.Add(vertical);
            var body = new TwoPaneSplitView(0, 235, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = "workbench-explorer" }; vertical.Add(body);
            var left = new VisualElement(); left.AddToClassList("workbench-panel"); body.Add(left); left.Add(new Label(StudioText.T("CONTENT EXPLORER")));
            left.Add(new Label(StudioText.T("Click to open. Add nodes with + Node in an event flow.")) { name = "explorer-help" });
            var filters = new List<string> { "All content", "Event flows", "Properties only" };
            explorerFilter = StudioText.Dropdown(filters, filters.IndexOf(explorerKind));
            explorerFilter.RegisterValueChangedCallback(e => { explorerKind = e.newValue; RefreshExplorer(); }); left.Add(explorerFilter);
            explorer = new ListView { name = "content-explorer", itemsSource = rows, fixedItemHeight = 52, selectionType = SelectionType.Single, reorderable = false };
            explorer.makeItem = () =>
            {
                var row = new VisualElement(); row.AddToClassList("explorer-link");
                row.Add(new Label { name = "content-name" }); row.Add(new Label { name = "content-action" }); return row;
            };
            explorer.bindItem = (row, i) =>
            {
                var entry = rows[i]; var owner = OwningEvent(entry.id);
                row.Q<Label>("content-name").text = entry.text.Length > 0 ? entry.text : entry.id;
                row.Q<Label>("content-action").text = StudioText.T(owner.Length > 0 ? "Open flow" : "Edit properties") + " · " + StudioText.T(entry.kind.Replace("Definition", ""));
                row.tooltip = entry.id + "\n" + StudioText.T("Click to open; items cannot be dragged onto the graph.");
            };
            explorer.style.flexGrow = 1; explorer.selectionChanged += items => { var entry = items.OfType<ContentEntry>().FirstOrDefault(); if (entry != null) Navigate(entry.id); }; left.Add(explorer);
            var rightSplit = new TwoPaneSplitView(1, 340, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = "workbench-inspector" }; body.Add(rightSplit);
            center = new VisualElement(); center.style.flexGrow = 1; rightSplit.Add(center);
            inspector = new ScrollView(); inspector.AddToClassList("workbench-panel"); rightSplit.Add(inspector);
            var bottom = new VisualElement(); vertical.Add(bottom);
            var tabs = new Toolbar(); tabs.AddToClassList("diagnostic-tabs"); bottom.Add(tabs); panelTabs.Clear();
            foreach (var name in new[] { "Problems", "References", "Trace", "Changes", "Localization" })
            {
                var tab = new ToolbarToggle { name = "tab-" + name, text = StudioText.T(name) }; tab.AddToClassList("diagnostic-tab");
                tab.RegisterValueChangedCallback(e => { if (e.newValue) { panel = name; RefreshDiagnostics(); } else tab.SetValueWithoutNotify(panel == name); });
                panelTabs.Add(name, tab); tabs.Add(tab);
            }
            diagnostics = new ScrollView(); diagnostics.style.flexGrow = 1; bottom.Add(diagnostics);
            status = new Label(StudioText.T("Ready")); status.AddToClassList("workbench-muted"); root.Add(status);
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!(e.ctrlKey || e.commandKey) || e.keyCode != KeyCode.S) return;
                ApplyChanges(); e.StopImmediatePropagation(); e.PreventDefault();
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!(e.ctrlKey || e.commandKey)) return;
                if (e.keyCode == KeyCode.P) { graphSearch = false; flowScope.SetValueWithoutNotify(false); RefreshExplorer(); search.Focus(); e.StopPropagation(); }
                if (e.keyCode == KeyCode.F && !string.IsNullOrEmpty(eventId)) { graphSearch = true; flowScope.SetValueWithoutNotify(true); RefreshExplorer(); search.Focus(); e.StopPropagation(); }
                if (IsTextEditing(e.target as VisualElement)) return;
                if (e.keyCode == KeyCode.C) { CopyNodes(); e.StopPropagation(); }
                if (e.keyCode == KeyCode.V) { PasteNodes(); e.StopPropagation(); }
            });
            if (api == null) Reload(); else RefreshAll();
        }
        private void Reload()
        {
            trace = null; tracePlayback?.Stop();
            if (center == null) return;
            if (asset == null) { center.Clear(); center.Add(new HelpBox(StudioText.T("Select a GameProjectAsset to start."), HelpBoxMessageType.Info)); RefreshSaveStatus(); return; }
            api = new AuthoringService(asset.Read(), codec, asset.CreateExtensions()); changes = api.BeginChangeSet(); preview = null; project = api.Snapshot();
            draftJson = JsonUtility.ToJson(changes);
            if (!project.locales.Contains(locale)) locale = project.defaultLocale;
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
            if (!AssetDatabase.IsValidFolder("Assets/PrincessStudioWorkbench"))
                AssetDatabase.CreateFolder("Assets", "PrincessStudioWorkbench");
            if (!AssetDatabase.IsValidFolder("Assets/PrincessStudioWorkbench/Layouts"))
                AssetDatabase.CreateFolder("Assets/PrincessStudioWorkbench", "Layouts");
            var path = "Assets/PrincessStudioWorkbench/Layouts/" + guid + ".asset";
            layout = AssetDatabase.LoadAssetAtPath<GraphLayoutAsset>(path);
            if (layout == null)
            {
                var templatePath = Path.ChangeExtension(AssetDatabase.GetAssetPath(asset), ".layout.asset");
                var template = AssetDatabase.LoadAssetAtPath<GraphLayoutAsset>(templatePath);
                layout = template == null ? CreateInstance<GraphLayoutAsset>() : Instantiate(template);
                AssetDatabase.CreateAsset(layout, path);
            }
            index = new ContentIndex(project, locale, extensions: asset.CreateExtensions(project));
            if (index.Find(selectedId) == null && selectedId != EventSequence.End && !selectedId.EndsWith(":terminal", StringComparison.Ordinal))
                selectedId = project.events.FirstOrDefault()?.id ?? "";
            if (index.Find(selectedId) != null) eventId = OwningEvent(selectedId);
            else if (!project.events.Exists(e => e.id == eventId)) eventId = selectedId = project.events.FirstOrDefault()?.id ?? "";
            inspectorDirty = false; RefreshAll(); Notify("Revision " + api.Revision + " · content and layout are stored separately");
        }
        private void RefreshAll()
        {
            trace = null; tracePlayback?.Stop();
            index = new ContentIndex(project, locale, extensions: asset.CreateExtensions(project)); RefreshGraph(); RefreshExplorer(); RefreshInspector(); RefreshDiagnostics(); RefreshSaveStatus();
            breadcrumb.text = StudioText.T("RaiseArc  ›  " + project.id + "  ›  " + eventId + "  ›  " + selectedId);
        }
        private void RefreshExplorer()
        {
            if (index == null || explorer == null) return;
            if (string.IsNullOrEmpty(eventId)) graphSearch = false;
            flowScope.SetValueWithoutNotify(graphSearch); flowScope.SetEnabled(!string.IsNullOrEmpty(eventId));
            rows.Clear(); rows.AddRange(index.Search(query, locale).Where(e => e.kind != "Condition" && e.kind != "Effect"
                && (!graphSearch || index.Within(e.id, eventId))
                && (explorerKind != "Event flows" || index.Find(e.id) is EventDefinition)
                && (explorerKind != "Properties only" || OwningEvent(e.id).Length == 0))); explorer.Rebuild();
            SyncExplorerSelection();
            canvas?.Highlight(string.IsNullOrWhiteSpace(query) ? new[] { selectedId } : rows.Select(x => x.id));
        }
        private void SyncExplorerSelection()
        {
            var row = rows.FindIndex(x => x.id == selectedId);
            explorer?.SetSelectionWithoutNotify(row < 0 ? Array.Empty<int>() : new[] { row });
        }
        private string OwningEvent(string id)
        {
            while (!string.IsNullOrEmpty(id))
            {
                if (index.Find(id) is EventDefinition) return id;
                id = index.Owner(id);
            }
            return "";
        }
        private void Navigate(string id, bool record = true)
        {
            if (!LeaveInspector()) { SyncExplorerSelection(); return; }
            selectedId = id;
            var previousEvent = eventId;
            if (id != EventSequence.End && !id.EndsWith(":terminal", StringComparison.Ordinal)) eventId = OwningEvent(id);
            if (previousEvent != eventId) { trace = null; preset = null; traceChoices.Clear(); }
            if (record)
            {
                if (historyIndex + 1 < history.Count) history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
                history.Add(id); historyIndex = history.Count - 1;
                layout.recent.Remove(id); layout.recent.Insert(0, id); if (layout.recent.Count > 30) layout.recent.RemoveAt(30); SaveLayout();
            }
            focusAfterRebuild = id;
            RefreshGraph(); RefreshExplorer(); RefreshInspector(); RefreshDiagnostics();
            breadcrumb.text = StudioText.T("RaiseArc  ›  " + project.id + "  ›  " + eventId + "  ›  " + id);
        }
        private void NavigateHistory(int delta) { var next = historyIndex + delta; if (next >= 0 && next < history.Count) { historyIndex = next; Navigate(history[next], false); } }
        private void ToggleFavorite() { if (layout == null) return; Undo.RecordObject(layout, "Favorite content"); if (!layout.favorites.Remove(selectedId)) layout.favorites.Add(selectedId); SaveLayout(); }
        private void ShowSaved(List<string> ids) { if (ids == null) return; var menu = new GenericMenu(); foreach (var id in ids) { var entry = id; menu.AddItem(new GUIContent(id), false, () => Navigate(entry)); } menu.ShowAsContext(); }
        private void SaveLayout() { EditorUtility.SetDirty(layout); AssetDatabase.SaveAssetIfDirty(layout); }
        private static Button B(string text, Action action) => new Button(action) { text = StudioText.T(text )};
        private void Notify(string text) { if (status != null) status.text = StudioText.T(text); }
        private void RefreshSaveStatus()
        {
            var dirty = inspectorDirty || (changes != null && changes.edits.Count > 0);
            if (saveStatus != null) saveStatus.text = StudioText.T(asset == null ? "No project selected" : dirty ? "Unsaved content · Save before closing" : "Content saved · Layout saves automatically");
            saveButton?.SetEnabled(asset != null && dirty);
        }
        private void MarkInspectorDirty() { inspectorDirty = true; RefreshSaveStatus(); }
        private void Safe(Action action) { try { action(); } catch (Exception e) { Notify(e.Message); Debug.LogWarning("RaiseArc Workbench: " + e.Message); } }
        private void Stage(GraphEdit edit) => StageMany(new[] { edit });
        private void StageMany(IEnumerable<GraphEdit> edits)
        {
            Safe(() =>
            {
                var list = edits.ToList();
                if (inspectorDirty && list.Any(edit => edit.operation != "ReplaceProject") && !LeaveInspector()) return;
                var candidate = JsonUtility.FromJson<AuthoringChangeSet>(JsonUtility.ToJson(changes)); candidate.edits.AddRange(list);
                var result = api.PreviewChangeSet(candidate);
                Undo.RecordObject(this, "Edit workbench draft");
                foreach (var previous in project.events.Where(e => e.choices.Count > 0))
                {
                    var nextEvent = result.project.events.Find(e => e.id == previous.id);
                    if (nextEvent == null || nextEvent.choices.Count > 0) continue;
                    var converted = nextEvent.presentation.Find(s => s.choices.Any(c => c.id == previous.choices[0].id));
                    if (converted == null) continue;
                    var oldId = previous.id + ":terminal";
                    var point = layout.nodes.Find(n => n.eventId == previous.id && n.nodeId == oldId);
                    if (point != null)
                    {
                        Undo.RecordObject(layout, "Keep converted choice layout");
                        var placement = layout.Get(previous.id, converted.id, 0);
                        placement.x = point.x; placement.y = point.y; placement.color = point.color;
                        placement.note = point.note; placement.group = point.group; placement.collapsed = point.collapsed;
                        placement.closedDetails = new List<string>(point.closedDetails); placement.openDetails = new List<string>(point.openDetails);
                        SaveLayout();
                    }
                    if (selectedId == oldId) selectedId = converted.id;
                }
                changes = candidate; preview = result; project = preview.project; draftJson = JsonUtility.ToJson(changes);
                inspectorDirty = false; RefreshAll(); Notify("Preview updated. Content is not saved yet. Click Save or press Ctrl/Cmd+S.");
            });
        }
        private void ApplyChanges()
        {
            Safe(() =>
            {
                if (!LeaveInspector()) return;
                if (asset == null || changes == null || changes.edits.Count == 0) return;
                var current = new AuthoringService(asset.Read(), codec, asset.CreateExtensions());
                var review = current.PreviewChangeSet(changes);
                if (review.validation.HasErrors) { panel = "Problems"; RefreshDiagnostics(); Notify("Not saved. Fix the errors in Problems, then Save again. Your edits are retained."); return; }
                current.CommitChangeSet(changes, review.token, api.Revision);
                Undo.RecordObject(asset, "Apply Graph Workbench changes"); asset.Write(current.Snapshot()); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
                RaiseArc.Editor.RaiseArcVoiceField.Sync(asset);
                Reload(); Notify("Saved to project asset. Undo/Redo is available.");
            });
        }
        private void CreateEvent()
        {
            if (api == null) return;
            var id = "event-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Stage(new GraphEdit { operation = "CreateEvent", eventDefinition = new EventDefinition { id = id, nameKey = "event." + id } }); eventId = id; Navigate(id);
        }
        private void CopyFlow() { if (project == null) return; clipboard = eventId; EditorGUIUtility.systemCopyBuffer = "PrincessFlow:" + eventId; Notify("Flow copied; paste duplicates it with new IDs."); }
        private void PasteFlow() { var value = EditorGUIUtility.systemCopyBuffer; if (!value.StartsWith("PrincessFlow:", StringComparison.Ordinal)) return; Stage(new GraphEdit { operation = "DuplicateFlow", eventId = value.Substring(13), nextId = Guid.NewGuid().ToString("N").Substring(0, 6) }); }
    }
}
