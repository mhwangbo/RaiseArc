using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor.Graph
{
    public sealed partial class GraphWorkbenchWindow
    {
        private string canvasEventId = "";
        private float graphZoom = 1;
        private Vector2 graphScroll;
        private string focusAfterRebuild;
        private bool canvasViewReady;
        private void RefreshGraph()
        {
            tracePlayback?.Stop(); traceHost = null;
            var focus = focusAfterRebuild; focusAfterRebuild = null;
            if (canvas != null && canvasViewReady) { graphZoom = canvas.Zoom; graphScroll = canvas.ScrollOffset; }
            canvasViewReady = false;
            center.Clear(); canvas = null;
            if (project == null) return;
            if (mode == "Relationships") { DrawRelationshipGraph(); return; }
            if (string.IsNullOrEmpty(eventId)) { DrawPropertiesNavigation(); return; }
            Safe(() =>
            {
                var currentEvent = project.events.Find(item => item.id == eventId);
                center.Add(new Label(StudioText.T("Event flow") + " · " + index.Text(currentEvent.nameKey, locale)) { name = "graph-title" });
                var toolbar = new Toolbar(); toolbar.AddToClassList("workbench-toolbar"); center.Add(toolbar);
                var modePicker = new ToolbarMenu { text = StudioText.T(mode) };
                foreach (var item in new[] { "Flow", "Table", "Relationships" }) modePicker.menu.AppendAction(StudioText.T(item), _ => { mode = item; RefreshGraph(); });
                toolbar.Add(modePicker);
                var language = StudioText.Dropdown(project.locales, Math.Max(0, project.locales.IndexOf(locale))); language.style.width = 90;
                language.RegisterValueChangedCallback(e => { locale = e.newValue; RefreshAll(); }); toolbar.Add(language);
                var skin = new ObjectField { objectType = typeof(PrincessStudio.Unity.PresentationSkin), allowSceneObjects = false, value = previewSkin }; skin.style.width = 130; skin.tooltip = StudioText.T("Sprite resources used by the inspector");
                skin.RegisterValueChangedCallback(e => { previewSkin = e.newValue as PrincessStudio.Unity.PresentationSkin; if (!inspectorDirty) RefreshInspector(); RefreshGraph(); }); toolbar.Add(skin);
                var create = new ToolbarMenu { text = StudioText.T("+ Node" )};
                foreach (PresentationStepKind kind in Enum.GetValues(typeof(PresentationStepKind)))
                    create.menu.AppendAction(StudioText.T(kind == PresentationStepKind.Dialogue ? "Dialogue Block" : kind == PresentationStepKind.Image ? "Presentation" : kind.ToString()), _ => AddNode(kind));
                toolbar.Add(create);
                toolbar.Add(B("Find / add…", () => ShowNodeSearch(null)));
                var clipboardMenu = new ToolbarMenu { text = StudioText.T("Clipboard" )};
                clipboardMenu.menu.AppendAction(StudioText.T("Copy selected node / dialogue block"), _ => CopyNodes());
                clipboardMenu.menu.AppendAction(StudioText.T("Paste nodes"), _ => PasteNodes());
                clipboardMenu.menu.AppendAction(StudioText.T("Copy entire flow"), _ => CopyFlow());
                clipboardMenu.menu.AppendAction(StudioText.T("Paste flow"), _ => PasteFlow()); toolbar.Add(clipboardMenu);
                toolbar.Add(B("Focus selection", () => canvas?.FocusNode(selectedId)));
                toolbar.Add(B("Auto layout", AutoLayout)); toolbar.Add(B("Play from selection", PlaySelection));
                toolbar.Add(B("Play from Start", () => { if (!LeaveInspector()) return; traceStartId = eventId; traceChoices.Clear(); RunTrace(); }));
                var graph = GraphProjection.Build(project, eventId, locale);
                if (mode == "Table")
                {
                    var table = new MultiColumnListView { itemsSource = graph.nodes, fixedItemHeight = 30 }; table.style.flexGrow = 1;
                    table.columns.Add(new Column { title = StudioText.T("Kind"), width = 140, makeCell = () => new Label(), bindCell = (cell, i) => ((Label)cell).text = StudioText.T(graph.nodes[i].kind) });
                    table.columns.Add(new Column { title = StudioText.T("Content"), width = 330, makeCell = () => new Label(), bindCell = (cell, i) => ((Label)cell).text = graph.nodes[i].title });
                    table.selectionChanged += list => { var node = list.OfType<FlowNode>().FirstOrDefault(); if (node != null) Navigate(node.id); }; center.Add(table);
                }
                else
                {
                    var viewport = new Toolbar(); viewport.AddToClassList("workbench-toolbar"); center.Add(viewport);
                    viewport.Add(B("Zoom out", () => canvas.SetZoom(canvas.Zoom / 1.25f)));
                    var zoomLabel = new Label { name = "graph-zoom" }; viewport.Add(zoomLabel);
                    viewport.Add(B("Zoom in", () => canvas.SetZoom(canvas.Zoom * 1.25f)));
                    viewport.Add(B("100%", () => canvas.SetZoom(1)));
                    viewport.Add(B("Fit all", () => canvas.FitAll()));
                    canvas = new FlowCanvas(graph, layout, id => { if (!LeaveInspector()) return; selectedId = id; canvas?.Highlight(new[] { id }); SyncExplorerSelection(); RefreshInspector(); RefreshDiagnostics(); breadcrumb.text = StudioText.T("RaiseArc  ›  " + project.id + "  ›  " + eventId + "  ›  " + id); },
                        ChangeConnection, () => Undo.RecordObject(layout, "Move graph node"), SaveLayout, project: project, locale: locale, skin: previewSkin, edit: EditNodeDetail);
                    canvas.ShowIssues((preview?.validation ?? ProjectValidator.Validate(project, asset.CreateExtensions(project))).issues, index);
                    center.Add(canvas.Element); canvas.Highlight(new[] { selectedId });
            canvas.AddRequested += position => ShowNodeSearch(position);
            canvas.ConnectionAddRequested += ShowConnectionNodeSearch;
                    canvas.ZoomChanged += value => zoomLabel.text = Mathf.RoundToInt(value * 100) + "%";
                    var current = canvas;
                    var sameFlow = canvasEventId == eventId; canvasEventId = eventId;
                    EventCallback<GeometryChangedEvent> ready = null;
                    ready = _ =>
                    {
                        current.Element.UnregisterCallback(ready);
                        current.Element.schedule.Execute(() =>
                        {
                            if (canvas != current) return;
                            if (sameFlow) current.RestoreView(graphZoom, graphScroll, () => { if (canvas == current) canvasViewReady = true; });
                            else { current.FitAll(); canvasViewReady = true; }
                            if (!string.IsNullOrEmpty(focus)) current.FocusNode(focus);
                            zoomLabel.text = Mathf.RoundToInt(current.Zoom * 100) + "%";
                        });
                    };
                    current.Element.RegisterCallback(ready);
                }
                traceHost = new ScrollView { name = "graph-test-panel" }; traceHost.AddToClassList("trace-player"); center.Add(traceHost); RefreshTracePlayer();
                center.Add(new Label(StudioText.T("Keep grabbed handle · Output → input · Input → output · Empty canvas: search · Close search: disconnect")) { name = "graph-help" });
            });
        }
        private void ChangeConnection(FlowEdge previous, FlowEdge next)
        {
            var edits = new List<GraphEdit>();
            if (previous != null && (next == null || previous.sourceId != next.sourceId || previous.portId != next.portId))
                edits.Add(new GraphEdit { operation = "DisconnectNodes", eventId = eventId, targetId = previous.sourceId, portId = previous.portId });
            var existing = next == null ? null : GraphProjection.Build(project, eventId, locale).edges.Find(edge => edge.sourceId == next.sourceId && edge.portId == next.portId);
            // Disconnecting can regroup dialogue; do not reconnect an already-correct output by its old group ID.
            if (next != null && (existing == null || existing.targetId != next.targetId))
                edits.Add(new GraphEdit { operation = "ConnectNodes", eventId = eventId, targetId = next.sourceId, portId = next.portId, nextId = next.targetId });
            if (edits.Count > 0) StageMany(edits);
        }
        private void DrawPropertiesNavigation()
        {
            var content = new ScrollView { name = "properties-navigation" }; content.AddToClassList("properties-navigation"); center.Add(content);
            content.Add(B("Relationships", () => { mode = "Relationships"; RefreshGraph(); }));
            var entry = index.Data.entries.Find(item => item.id == selectedId);
            content.Add(new Label(entry == null ? StudioText.T("Select content") : entry.text) { name = "graph-title" });
            content.Add(new Label(StudioText.T("This content has no event flow.")));
            content.Add(new Label(StudioText.T("Edit its properties in the Inspector. References shows where it is used. Graphs show event execution, not every content type.")));
            content.Add(B("Show references", () => { panel = "References"; RefreshDiagnostics(); }));
            foreach (var id in index.References(selectedId).Select(reference => OwningEvent(reference.sourceId)).Where(id => id.Length > 0).Distinct().Take(12))
            {
                var e = project.events.Find(item => item.id == id);
                content.Add(B(StudioText.T("Open flow") + " · " + index.Text(e.nameKey, locale), () => Navigate(id)));
            }
            content.Add(B("Browse event flows", () =>
            {
                explorerKind = "Event flows"; explorerFilter.SetValueWithoutNotify(explorerKind);
                query = ""; search.SetValueWithoutNotify(""); graphSearch = false; RefreshExplorer();
            }));
        }
        private void DrawRelationshipGraph()
        {
            string OwnerCard(string id)
            {
                var entry = index.Data.entries.Find(x => x.id == id);
                while (entry != null && (entry.kind == "Condition" || entry.kind == "Effect" || string.IsNullOrWhiteSpace(entry.text)))
                {
                    var owner = index.Owner(id); if (string.IsNullOrEmpty(owner) || owner == id) break;
                    id = owner; entry = index.Data.entries.Find(x => x.id == id);
                }
                return id;
            }
            center.Add(new Label(StudioText.T("Relationships") + " · " + (index.Data.entries.Find(x => x.id == selectedId)?.text ?? selectedId)) { name = "graph-title" });
            var toolbar = new Toolbar(); center.Add(toolbar);
            toolbar.Add(B("Flow / properties", () => { mode = "Flow"; RefreshGraph(); }));
            toolbar.Add(B("Fit all", () => canvas?.FitAll()));
            center.Add(new Label(StudioText.T("Read-only references. Click a card to explore its neighbors. Lines show usage, not execution order.")));
            var references = index.References(selectedId).Concat(index.References(selectedId, false))
                .Where(r => r.relationType != ContentRelation.Localizes && r.targetId != EventSequence.End)
                .Select(r => new ContentReference { sourceId = OwnerCard(r.sourceId), targetId = OwnerCard(r.targetId), relationType = r.relationType })
                .GroupBy(r => r.sourceId + "|" + r.targetId + "|" + r.relationType).Select(g => g.First()).Take(60).ToList();
            var graph = new GraphProjection { eventId = "$references:" + selectedId };
            var ids = new[] { selectedId }.Concat(references.SelectMany(r => new[] { r.sourceId, r.targetId })).Distinct().ToList();
            var left = 0; var right = 0;
            foreach (var id in ids)
            {
                var entry = index.Data.entries.Find(x => x.id == id);
                graph.nodes.Add(new FlowNode { id = id, kind = entry?.kind ?? "Reference", title = string.IsNullOrWhiteSpace(entry?.text) ? id : entry.text });
                if (layout.nodes.Exists(n => n.eventId == graph.eventId && n.nodeId == id)) continue;
                var point = layout.Get(graph.eventId, id, 0);
                var inbound = references.Any(r => r.sourceId == id && (r.targetId == selectedId || index.Within(r.targetId, selectedId)));
                point.x = id == selectedId ? 420 : inbound ? 40 : 800;
                point.y = id == selectedId ? 140 : (inbound ? left++ : right++) * 290;
            }
            for (var i = 0; i < references.Count; i++)
            {
                var reference = references[i];
                graph.edges.Add(new FlowEdge { sourceId = reference.sourceId, targetId = reference.targetId, portId = "reference-" + i, label = StudioText.T(reference.relationType.ToString()) });
            }
            canvas = new FlowCanvas(graph, layout, id => Navigate(id), null,
                () => Undo.RecordObject(layout, "Move relationship card"), SaveLayout, true);
            center.Add(canvas.Element);
            var current = canvas;
            current.Element.schedule.Execute(() => { if (canvas == current) current.FitAll(); }).StartingIn(50);
            center.Add(new Label(StudioText.T("Up to 60 direct references. Select a neighbor to continue exploring.")));
        }
        private void AddNode(PresentationStepKind kind)
        {
            var id = "step-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var step = new PresentationStep { id = id, nameKey = "step." + id, kind = kind, nextStepId = "$disconnected", falseStepId = "$disconnected", backgroundChange = StageChange.Keep, actorsChange = StageChange.Keep, imagesChange = StageChange.Keep };
            if (kind == PresentationStepKind.Choice) step.choices.Add(new ChoiceDefinition { id = id + ".choice", nameKey = "choice." + id, nextStepId = "$disconnected" });
            Stage(new GraphEdit { operation = "CreateNode", eventId = eventId, node = step }); selectedId = id; RefreshInspector();
        }
        private void ShowNodeSearch(Vector2? position)
        {
            OpenNodeSearch(position, null, null, false);
        }
        private void ShowConnectionNodeSearch(Vector2 position, FlowEdge previous, FlowEdge retained, bool movingOutput)
        {
            OpenNodeSearch(position, id =>
            {
                var graph = GraphProjection.Build(project, eventId, locale);
                var output = graph.edges.FirstOrDefault(edge => edge.sourceId == id);
                var edits = new List<GraphEdit>();
                if (previous != null || movingOutput)
                {
                    if (output == null) return;
                    edits.Add(new GraphEdit { operation = "ConnectNodes", eventId = eventId, targetId = id, portId = output.portId, nextId = previous != null ? previous.targetId : retained.targetId });
                }
                if (previous != null || !movingOutput)
                {
                    var source = previous ?? retained;
                    edits.Add(new GraphEdit { operation = "ConnectNodes", eventId = eventId, targetId = source.sourceId, portId = source.portId, nextId = id });
                }
                StageMany(edits);
            }, () => { if (previous != null) ChangeConnection(previous, null); }, previous != null || movingOutput);
        }
        private void OpenNodeSearch(Vector2? position, Action<string> added, Action cancelled, bool requireOutput)
        {
            if (!LeaveInspector() || string.IsNullOrEmpty(eventId)) return;
            center.Q("node-search")?.RemoveFromHierarchy();
            var items = new System.Collections.Generic.List<(string title, string detail, Action add)>();
            foreach (PresentationStepKind kind in Enum.GetValues(typeof(PresentationStepKind)))
            {
                var label = StudioText.T(kind == PresentationStepKind.Dialogue ? "Dialogue Block" : kind == PresentationStepKind.Image ? "Presentation" : kind.ToString());
                items.Add((StudioText.T("New") + " · " + label, StudioText.T("Create a new node in this event"), () => { AddNode(kind); PlaceAddedNode(selectedId, position); }));
            }
            foreach (var source in project.events)
            {
                if (source.id != eventId)
                {
                    var sourceId = source.id;
                    items.Add((StudioText.T("Call shared flow") + " · " + index.Text(source.nameKey, locale), source.id,
                        () => AddSharedFlow(sourceId, position)));
                }
                var sourceGraph = GraphProjection.Build(project, source.id, locale);
                foreach (var node in sourceGraph.nodes.Where(n => n.stepIds.Count > 0 && (!requireOutput || sourceGraph.edges.Any(edge => edge.sourceId == n.id))))
                {
                    var owner = source.id; var id = node.id;
                    items.Add((StudioText.T("Copy") + " · " + node.title, index.Text(source.nameKey, locale) + " · " + StudioText.T(node.kind) + " · " + id,
                        () => CopyExistingNode(owner, id, position)));
                    if (node.stepIds.All(stepId => { var step = source.presentation.Find(s => s.id == stepId); return string.IsNullOrEmpty(step.sharedStepId) && string.IsNullOrEmpty(step.sharedEventId); }))
                        items.Add((StudioText.T("Share content") + " · " + node.title, index.Text(source.nameKey, locale) + " · " + id,
                            () => AddSharedContent(owner, id, position)));
                }
            }
            if (added == null) items.Insert(0, (StudioText.T("New reusable flow"), StudioText.T("Create a flow that runs only when called"), () =>
            {
                var id = "flow-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Stage(new GraphEdit { operation = "CreateEvent", eventDefinition = new EventDefinition { id = id, nameKey = "flow." + id, callOnly = true } });
                Navigate(id);
            }));
            if (added != null)
                items = items.Select(item => (item.title, item.detail, (Action)(() =>
                {
                    Undo.IncrementCurrentGroup();
                    var group = Undo.GetCurrentGroup();
                    var before = new HashSet<string>(project.events.Find(e => e.id == eventId).presentation.Select(step => step.id));
                    try
                    {
                        item.add();
                        if (!before.Contains(selectedId) && project.events.Find(e => e.id == eventId).presentation.Any(step => step.id == selectedId)) added(selectedId);
                    }
                    finally { Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); }
                }))).ToList();
            center.Add(new RaiseArc.Editor.Graph.RaiseArcNodeSearch(items, cancelled));
        }
        private void AddSharedFlow(string sourceEvent, Vector2? position)
        {
            if (!LeaveInspector()) return;
            var source = project.events.Find(e => e.id == sourceEvent);
            var id = "call-" + Guid.NewGuid().ToString("N");
            Stage(new GraphEdit { operation = "CreateNode", eventId = eventId, node = new PresentationStep
            { id = id, nameKey = source.nameKey, kind = PresentationStepKind.Effect, sharedEventId = sourceEvent, nextStepId = "$disconnected" } });
            PlaceAddedNode(id, position);
        }
        private void AddSharedContent(string sourceEvent, string nodeId, Vector2? position)
        {
            if (!LeaveInspector()) return;
            var source = project.events.Find(e => e.id == sourceEvent);
            var node = GraphProjection.Build(project, sourceEvent, locale).nodes.Find(n => n.id == nodeId);
            var step = source.presentation.Find(s => s.id == node.stepIds[0]);
            var id = "shared-" + Guid.NewGuid().ToString("N");
            Stage(new GraphEdit { operation = "CreateNode", eventId = eventId, node = new PresentationStep
            { id = id, kind = step.kind, nameKey = step.nameKey, sharedStepId = step.id, nextStepId = "$disconnected", falseStepId = "$disconnected" } });
            PlaceAddedNode(id, position);
        }
        private void PlaceAddedNode(string id, Vector2? position)
        {
            var graph = GraphProjection.Build(project, eventId, locale);
            if (!graph.nodes.Exists(n => n.id == id)) return;
            if (position.HasValue)
            {
                Undo.RecordObject(layout, "Place added node");
                var point = layout.Get(eventId, id, graph.nodes.FindIndex(n => n.id == id));
                point.x = position.Value.x; point.y = position.Value.y; SaveLayout();
            }
            selectedId = id;
            RefreshGraph(); SyncExplorerSelection(); RefreshInspector(); RefreshDiagnostics();
        }
        private void CopyExistingNode(string sourceEvent, string nodeId, Vector2? position)
        {
            Safe(() =>
            {
                if (!LeaveInspector()) return;
                var node = GraphProjection.Build(project, sourceEvent, locale).nodes.Find(n => n.id == nodeId);
                if (node == null || node.stepIds.Count == 0) return;
                var data = JsonUtility.FromJson<NodeClipboard>(JsonUtility.ToJson(CopyableNodeData(sourceEvent, node)));
                var first = PasteNodeData(data);
                if (first != null) PlaceAddedNode(first, position);
            });
        }
        private void AutoLayout()
        {
            if (layout == null || string.IsNullOrEmpty(eventId)) return;
            Safe(() =>
            {
                Undo.RecordObject(layout, "Auto layout event"); var graph = GraphProjection.Build(project, eventId);
                var depth = new System.Collections.Generic.Dictionary<string, int> { [eventId] = 0 };
                for (var pass = 0; pass < graph.nodes.Count; pass++)
                    foreach (var edge in graph.edges)
                        if (depth.TryGetValue(edge.sourceId, out var d) && edge.targetId != eventId && (!depth.TryGetValue(edge.targetId, out var old) || old < d + 1)) depth[edge.targetId] = Math.Min(graph.nodes.Count, d + 1);
                var lanes = new System.Collections.Generic.Dictionary<int, float>();
                var width = Math.Max(320, layout.nodeWidth);
                var index = 0;
                foreach (var node in graph.nodes)
                {
                    var d = depth.TryGetValue(node.id, out var value) ? value : 0; lanes.TryGetValue(d, out var lane);
                    lanes[d] = lane + Math.Max(160, canvas?.NodeSize(node.id).y ?? 320) + 60;
                    var position = layout.Get(eventId, node.id, index++); position.x = 30 + d * (width + 90); position.y = 35 + lane;
                }
                SaveLayout(); RefreshGraph();
            });
        }
    }
}
