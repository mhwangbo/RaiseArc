using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor.Graph
{
    public interface IWorkbenchCanvas
    {
        VisualElement Element { get; }
        void FocusNode(string id);
        void Highlight(IEnumerable<string> ids);
    }
    public sealed class FlowCanvas : IWorkbenchCanvas
    {
        public VisualElement Element => viewport;
        private readonly VisualElement viewport = new VisualElement();
        private readonly VisualElement surface = new VisualElement();
        private readonly VisualElement world = new VisualElement();
        private readonly Dictionary<string, VisualElement> cards = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, VisualElement> inputs = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, VisualElement> outputs = new Dictionary<string, VisualElement>();
        private readonly GraphProjection graph;
        private readonly bool readOnly;
        private string traceNode, traceNext, traceChoice;
        private readonly RaiseArc.Editor.RaiseArcNodeDetails details;
        private readonly HashSet<string> highlighted = new HashSet<string>();
        private readonly GraphLayoutAsset layout;
        private readonly Action<string> selected;
        private readonly Action<FlowEdge, FlowEdge> changeConnection;
        private readonly Dictionary<VisualElement, string> inputNodes = new Dictionary<VisualElement, string>();
        private readonly Dictionary<VisualElement, FlowEdge> outputEdges = new Dictionary<VisualElement, FlowEdge>();
        private readonly Dictionary<string, VisualElement> inputEnds = new Dictionary<string, VisualElement>();
        private readonly HashSet<string> nodeIds;
        private readonly HashSet<string> selection = new HashSet<string>();
        private readonly Dictionary<string, NodeLayout> placements = new Dictionary<string, NodeLayout>();
        public IReadOnlyCollection<string> SelectedNodes => selection;
        private readonly Action beginMove, endMove;
        private FlowEdge pendingEdge;
        private VisualElement originPin;
        private string fixedTarget;
        private bool fromInput;
        private bool MovingOutput => fromInput;
        private VisualElement capturedPort;
        private int connectionPointer;
        private bool draggingConnection;
        private Vector2 connectionStart, connectionPosition;
        private Vector2 extent = new Vector2(3800, 3200);
        private Vector2 viewOffset;
        private readonly VisualElement miniMap = new VisualElement { name = "graph-minimap" };
        public float Zoom { get; private set; } = 1;
        public Vector2 ScrollOffset
        {
            get => viewOffset;
            set { viewOffset = value; world.style.translate = new Translate(-value.x, -value.y); surface.MarkDirtyRepaint(); miniMap.MarkDirtyRepaint(); }
        }
        public event Action<float> ZoomChanged;
        public event Action<Vector2> AddRequested;
        public event Action<Vector2, FlowEdge, FlowEdge, bool> ConnectionAddRequested;
        public FlowCanvas(GraphProjection graph, GraphLayoutAsset layout, Action<string> selected, Action<FlowEdge, FlowEdge> changeConnection, Action beginMove, Action endMove, bool readOnly = false,
            ProjectDefinition project = null, string locale = null, PrincessStudio.Unity.PresentationSkin skin = null, Action<string, string> edit = null)
        {
            this.readOnly = readOnly;
            this.graph = graph; this.layout = layout; this.selected = selected; this.changeConnection = changeConnection; this.beginMove = beginMove; this.endMove = endMove;
            if (project != null && !readOnly) details = new RaiseArc.Editor.RaiseArcNodeDetails(project, graph.eventId, locale, layout, skin, endMove, edit);
            nodeIds = new HashSet<string>(graph.nodes.Select(n => n.id));
            viewport.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape && originPin != null) { CancelConnection(); e.StopPropagation(); } }, TrickleDown.TrickleDown);
            viewport.RegisterCallback<DetachFromPanelEvent>(_ => CancelConnection());
            viewport.name = "flow-canvas"; viewport.style.flexGrow = 1;
            viewport.style.minHeight = 0;

            viewport.style.overflow = Overflow.Hidden;
            world.name = "flow-world";
            world.style.width = extent.x; world.style.height = extent.y;
            world.style.position = Position.Absolute;
            world.style.transformOrigin = new TransformOrigin(0, 0, 0);
            surface.style.width = extent.x; surface.style.height = extent.y;
            surface.style.flexShrink = 0; surface.style.backgroundColor = new Color(.075f, .085f, .11f);
            surface.Add(world); viewport.Add(surface);
            surface.generateVisualContent += context =>
            {
                var spacing = 80 * Zoom;
                while (spacing < 24) spacing *= 2;
                var painter = context.painter2D; painter.lineWidth = 1; painter.strokeColor = new Color(.16f, .18f, .22f);
                var size = surface.layout.size;
                var xStart = (-viewOffset.x % spacing + spacing) % spacing;
                var yStart = (-viewOffset.y % spacing + spacing) % spacing;
                for (var x = xStart; x < size.x; x += spacing) { painter.BeginPath(); painter.MoveTo(new Vector2(x, 0)); painter.LineTo(new Vector2(x, size.y)); painter.Stroke(); }
                for (var y = yStart; y < size.y; y += spacing) { painter.BeginPath(); painter.MoveTo(new Vector2(0, y)); painter.LineTo(new Vector2(size.x, y)); painter.Stroke(); }
            };
            viewport.RegisterCallback<GeometryChangedEvent>(e =>
            {
                surface.style.width = e.newRect.width; surface.style.height = e.newRect.height;
                miniMap.MarkDirtyRepaint();
            });
            SetupMiniMap();
            viewport.RegisterCallback<PointerUpEvent>(e =>
            {
                if (readOnly || e.button != 1 || !BlankCanvas(e.position)) return;
                AddRequested?.Invoke(world.WorldToLocal(e.position)); e.StopPropagation();
            });
            world.generateVisualContent += DrawEdges;
            var i = 0;
            foreach (var node in graph.nodes) AddNode(node, layout.Get(graph.eventId, node.id, i++));
            viewport.RegisterCallback<WheelEvent>(e =>
            {
                SetZoom(Zoom * Mathf.Pow(1.12f, -e.delta.y), viewport.WorldToLocal(e.mousePosition));
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            viewport.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 2) return;
                viewport.CapturePointer(e.pointerId); e.StopPropagation();
            }, TrickleDown.TrickleDown);
            viewport.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!viewport.HasPointerCapture(e.pointerId)) return;
                ScrollOffset -= (Vector2)e.deltaPosition; e.StopPropagation();
            });
            viewport.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 2 || !viewport.HasPointerCapture(e.pointerId)) return;
                viewport.ReleasePointer(e.pointerId); e.StopPropagation();
            });
            var marquee = new VisualElement { name = "selection-marquee", pickingMode = PickingMode.Ignore };
            marquee.style.position = Position.Absolute; marquee.style.display = DisplayStyle.None;
            marquee.style.backgroundColor = new Color(.25f, .65f, 1f, .2f);
            marquee.style.borderTopWidth = marquee.style.borderBottomWidth = marquee.style.borderLeftWidth = marquee.style.borderRightWidth = 1;
            marquee.style.borderTopColor = marquee.style.borderBottomColor = marquee.style.borderLeftColor = marquee.style.borderRightColor = new Color(.3f, .7f, 1f);
            world.Add(marquee);
            var selecting = false; var start = Vector2.zero; var baseline = new HashSet<string>();
            surface.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !BlankCanvas(e.position)) return;
                CancelConnection(); selecting = true; start = world.WorldToLocal(e.position);
                baseline = e.ctrlKey || e.commandKey || e.shiftKey ? new HashSet<string>(selection) : new HashSet<string>();
                selection.Clear(); selection.UnionWith(baseline); PaintSelection();
                surface.CapturePointer(e.pointerId); e.StopPropagation();
            });
            surface.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!selecting || !surface.HasPointerCapture(e.pointerId)) return;
                var end = world.WorldToLocal(e.position);
                var bounds = Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y), Mathf.Max(start.x, end.x), Mathf.Max(start.y, end.y));
                marquee.style.display = DisplayStyle.Flex; marquee.style.left = bounds.xMin; marquee.style.top = bounds.yMin; marquee.style.width = bounds.width; marquee.style.height = bounds.height;
                selection.Clear(); selection.UnionWith(baseline);
                foreach (var item in cards) if (bounds.Overlaps(item.Value.layout)) selection.Add(item.Key);
                PaintSelection(); e.StopPropagation();
            });
            surface.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!selecting || e.button != 0) return;
                selecting = false; surface.ReleasePointer(e.pointerId); marquee.style.display = DisplayStyle.None;
                e.StopPropagation();
            });
            surface.RegisterCallback<PointerCaptureOutEvent>(_ => { selecting = false; marquee.style.display = DisplayStyle.None; });
        }
        private Rect MiniMapBounds()
        {
            var nodes = NodeBounds(); var view = new Rect(ScrollOffset / Zoom, viewport.layout.size / Zoom);
            return Rect.MinMaxRect(Mathf.Min(nodes.xMin, view.xMin) - 100, Mathf.Min(nodes.yMin, view.yMin) - 100,
                Mathf.Max(nodes.xMax, view.xMax) + 100, Mathf.Max(nodes.yMax, view.yMax) + 100);
        }
        private void SetupMiniMap()
        {
            miniMap.style.position = Position.Absolute; miniMap.style.right = 12; miniMap.style.bottom = 12;
            miniMap.style.width = 180; miniMap.style.height = 120; miniMap.style.backgroundColor = new Color(.035f, .05f, .075f, .95f);
            miniMap.tooltip = StudioText.T("Minimap: click or drag to navigate");
            var label = new Label(StudioText.T("Minimap")) { pickingMode = PickingMode.Ignore }; miniMap.Add(label);
            viewport.Add(miniMap);
            miniMap.generateVisualContent += context =>
            {
                var bounds = MiniMapBounds();
                if (bounds.width <= 0 || bounds.height <= 0 || float.IsNaN(bounds.width)) return;
                var scale = Mathf.Min(164 / bounds.width, 92 / bounds.height);
                var painter = context.painter2D;
                void Box(Rect rect, Color color, bool fill)
                {
                    var min = (rect.min - bounds.min) * scale + new Vector2(8, 22); var size = rect.size * scale;
                    painter.BeginPath(); painter.MoveTo(min); painter.LineTo(min + new Vector2(size.x, 0));
                    painter.LineTo(min + size); painter.LineTo(min + new Vector2(0, size.y)); painter.ClosePath();
                    if (fill) { painter.fillColor = color; painter.Fill(); }
                    else { painter.strokeColor = color; painter.lineWidth = 1; painter.Stroke(); }
                }
                foreach (var item in cards) Box(item.Value.layout, selection.Contains(item.Key) ? new Color(.3f, .75f, 1f) : new Color(.4f, .5f, .6f), true);
                Box(new Rect(ScrollOffset / Zoom, viewport.layout.size / Zoom), new Color(1f, .85f, .4f), false);
            };
            Rect dragBounds = default;
            void Navigate(Vector2 position)
            {
                var scale = Mathf.Min(164 / dragBounds.width, 92 / dragBounds.height);
                if (scale <= 0 || float.IsNaN(scale)) return;
                var point = (miniMap.WorldToLocal(position) - new Vector2(8, 22)) / scale + dragBounds.min;
                ScrollOffset = point * Zoom - viewport.layout.size / 2;
            }
            miniMap.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragBounds = MiniMapBounds(); Navigate(e.position); miniMap.CapturePointer(e.pointerId); e.StopImmediatePropagation();
            });
            miniMap.RegisterCallback<PointerMoveEvent>(e => { if (miniMap.HasPointerCapture(e.pointerId)) { Navigate(e.position); e.StopPropagation(); } });
            miniMap.RegisterCallback<PointerUpEvent>(e => { if (miniMap.HasPointerCapture(e.pointerId)) { miniMap.ReleasePointer(e.pointerId); e.StopPropagation(); } });
        }
        private void PaintSelection()
        {
            miniMap.MarkDirtyRepaint();
            foreach (var item in cards)
            {
                item.Value.RemoveFromClassList("node-highlight");
                item.Value.EnableInClassList("node-selected", selection.Contains(item.Key));
            }
        }
        private void SelectNode(string id, bool toggle)
        {
            if (toggle) { if (!selection.Add(id)) selection.Remove(id); }
            else if (!selection.Contains(id)) { selection.Clear(); selection.Add(id); }
            PaintSelection();
            if (selection.Contains(id)) selected(id);
        }
        public void SetZoom(float value) => SetZoom(value, viewport.layout.size / 2);
        private void SetZoom(float value, Vector2 anchor)
        {
            value = Mathf.Clamp(value, .1f, 2f);
            var offset = (ScrollOffset + anchor) / Zoom * value - anchor;
            Zoom = value; world.style.scale = new Scale(new Vector3(Zoom, Zoom, 1));
            ResizeSurface(); ScrollOffset = offset; ZoomChanged?.Invoke(Zoom);
        }
        public void RestoreView(float zoom, Vector2 offset, Action restored)
        {
            Zoom = Mathf.Clamp(zoom, .1f, 2f);
            world.style.scale = new Scale(new Vector3(Zoom, Zoom, 1));
            ResizeSurface(); ScrollOffset = offset; restored?.Invoke(); ZoomChanged?.Invoke(Zoom);
        }
        private Rect NodeBounds()
        {
            var bounds = new Rect(); var first = true;
            foreach (var card in cards.Values)
            {
                var rect = card.layout;
                if (float.IsNaN(rect.width) || float.IsNaN(rect.height)) continue;
                bounds = first ? rect : Rect.MinMaxRect(Mathf.Min(bounds.xMin, rect.xMin), Mathf.Min(bounds.yMin, rect.yMin), Mathf.Max(bounds.xMax, rect.xMax), Mathf.Max(bounds.yMax, rect.yMax));
                first = false;
            }
            return bounds;
        }
        private void ResizeSurface()
        {
            var bounds = NodeBounds(); extent = new Vector2(Mathf.Max(3800, bounds.xMax + 200), Mathf.Max(3200, bounds.yMax + 200));
            world.style.width = extent.x; world.style.height = extent.y;
            miniMap.MarkDirtyRepaint();
        }
        public void FitAll()
        {
            var bounds = NodeBounds(); var viewportSize = viewport.layout.size;
            if (bounds.width <= 0 || bounds.height <= 0 || viewportSize.x <= 64 || viewportSize.y <= 64) return;
            SetZoom(Mathf.Min((viewportSize.x - 64) / bounds.width, (viewportSize.y - 64) / bounds.height));
            ScrollOffset = bounds.center * Zoom - viewportSize / 2;
        }
        private void AddNode(FlowNode node, NodeLayout placement)
        {
            placements[node.id] = placement;
            var card = new VisualElement { name = "node-" + node.id }; card.AddToClassList("flow-node");
            card.style.position = Position.Absolute; card.style.left = placement.x; card.style.top = placement.y; card.style.width = details == null ? layout.nodeWidth : Mathf.Max(320, layout.nodeWidth); card.style.backgroundColor = placement.color;
            card.AddToClassList("node-kind-" + node.kind.Replace(" ", "-"));
            var kind = new Label(StudioText.T(node.kind)); kind.AddToClassList("node-kind"); card.Add(kind);
            var titleText = node.kind == "End" || node.id.EndsWith(":terminal", StringComparison.Ordinal) ? StudioText.T(node.title) : node.title;
            var title = new Label(details != null && titleText.Length > 100 ? titleText.Substring(0, 100) + "…" : titleText) { tooltip = titleText }; title.AddToClassList("node-title"); card.Add(title);
            if (details == null && !placement.collapsed && !readOnly)
            {
                foreach (var line in node.dialogue.Take(3))
                {
                    var dialogue = new Label(line) { tooltip = line }; dialogue.AddToClassList("node-dialogue"); card.Add(dialogue);
                }
                if (node.dialogue.Count > 3) card.Add(new Label("+ " + (node.dialogue.Count - 3)) { tooltip = string.Join("\n", node.dialogue) });
                var counts = new Label(StudioText.Format("graph.node.counts", node.lineCount, node.conditions, node.effects)); counts.AddToClassList("node-summary"); card.Add(counts);
                var media = new Label(StudioText.Format("graph.node.media", node.actors, node.images, node.missingTranslations)); media.AddToClassList("node-summary"); card.Add(media);
                if (!string.IsNullOrEmpty(placement.note)) card.Add(new Label(placement.note));
            }
            if (node.kind != "Entry")
            {
                var incoming = graph.edges.Where(e => e.targetId == node.id).ToList();
                if (incoming.Count == 0 && !readOnly) AddInput(node.id, null, StudioText.T("Input"), card);
                for (var n = 0; n < incoming.Count; n++)
                {
                    var edge = incoming[n];
                    var source = graph.nodes.Find(item => item.id == edge.sourceId);
                    AddInput(node.id, edge, incoming.Count == 1 ? StudioText.T("Input") : (source.id.EndsWith(":terminal", StringComparison.Ordinal) ? StudioText.T(source.title) : source.title) + " · " + StudioText.T(edge.label), card);
                }
            }
            VisualElement Output(FlowEdge edge)
            {
                var row = new VisualElement(); row.AddToClassList("port-row"); row.AddToClassList("output-row");
                var label = edge.portId == "next" || edge.portId == "false" ? StudioText.T(edge.label) : edge.label;
                if (details != null)
                {
                    var target = graph.nodes.Find(n => n.id == edge.targetId);
                    label = (edge.portId == "false" ? StudioText.T("Not met") : node.kind == "Condition" ? StudioText.T("Met") : StudioText.T("Next")) + ": " +
                        (target == null ? StudioText.T("Unconnected") : target.kind == "End" ? StudioText.T("End") : target.id.EndsWith(":terminal", StringComparison.Ordinal) ? StudioText.T(target.title) : target.title);
                }
                row.Add(new Label(label));
                var pin = MakePin(node.id, edge, false, "output-" + node.id + "-" + edge.portId);
                pin.EnableInClassList("port-connected", nodeIds.Contains(edge.targetId));
                row.Add(pin); outputs[node.id + ":" + edge.portId] = pin; outputEdges[pin] = edge; return row;
            }
            var outgoing = graph.edges.Where(e => e.sourceId == node.id).ToList();
            if (details != null) details.Populate(node, placement, card, outgoing, Output);
            else foreach (var edge in outgoing) card.Add(Output(edge));
            title.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                SelectNode(node.id, e.ctrlKey || e.commandKey || e.shiftKey);
                if (selection.Contains(node.id)) { beginMove(); title.CapturePointer(e.pointerId); }
                e.StopPropagation();
            });
            title.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!title.HasPointerCapture(e.pointerId)) return;
                var dx = e.deltaPosition.x / Zoom;
                var dy = e.deltaPosition.y / Zoom;
                foreach (var id in selection)
                {
                    var point = placements[id]; point.x += dx; point.y += dy;
                    cards[id].style.left = point.x; cards[id].style.top = point.y;
                }
                world.MarkDirtyRepaint();
            });
            title.RegisterCallback<PointerUpEvent>(e => { if (title.HasPointerCapture(e.pointerId)) { title.ReleasePointer(e.pointerId); endMove(); } });
            title.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2) selected(node.id); });
            card.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                SelectNode(node.id, e.ctrlKey || e.commandKey || e.shiftKey); e.StopPropagation();
            });
            card.RegisterCallback<GeometryChangedEvent>(_ => { ResizeSurface(); world.MarkDirtyRepaint(); });
            world.Add(card); cards[node.id] = card;
        }
        private void AddInput(string node, FlowEdge edge, string label, VisualElement card)
        {
            var row = new VisualElement(); row.AddToClassList("port-row"); row.AddToClassList("input-row");
            var name = !inputs.ContainsKey(node) ? "input-" + node : edge == null ? "input-" + node + "-new" : "input-" + node + "-from-" + edge.sourceId + "-" + edge.portId;
            var pin = MakePin(node, edge, true, name); pin.EnableInClassList("port-connected", edge != null);
            row.Add(pin); row.Add(new Label(label) { tooltip = label }); card.Add(row);
            if (!inputs.ContainsKey(node)) inputs[node] = pin;
            inputNodes[pin] = node;
            if (edge != null) inputEnds[edge.sourceId + ":" + edge.portId] = pin;
        }
        private Button MakePin(string node, FlowEdge edge, bool input, string name)
        {
            Button pin = null;
            var hint = Connected(edge)
                ? input ? "Connected input: keep this input and drag to a different output. Drop on empty canvas to search. Close search to disconnect; Esc during drag cancels." : "Connected output: keep this output and drag to a different input. Drop on empty canvas to search. Close search to disconnect; Esc during drag cancels."
                : input ? "Empty input: drag to an output to create a connection." : "Empty output: drag to an input to create a connection.";
            pin = new Button(() => StartPin(pin, node, edge, input)) { name = name, tooltip = StudioText.T(hint) };
            pin.AddToClassList("connection-pin");
            if (readOnly) { pin.SetEnabled(false); return pin; }
            pin.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                e.StopImmediatePropagation();
                if (originPin != null && capturedPort == null && TargetPin(e.position) == pin) { FinishConnection(e.position, false); return; }
                StartPin(pin, node, edge, input); connectionStart = connectionPosition = e.position;
                capturedPort = pin; connectionPointer = e.pointerId; pin.Focus(); pin.CapturePointer(e.pointerId);
            }, TrickleDown.TrickleDown);
            pin.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (capturedPort != pin || e.pointerId != connectionPointer) return;
                connectionPosition = e.position; draggingConnection |= Vector2.Distance(connectionStart, connectionPosition) > 6;
                var destination = draggingConnection ? TargetPin(connectionPosition) : null;
                foreach (var item in inputNodes.Keys.Concat(outputEdges.Keys)) item.EnableInClassList("port-drop-target", item == destination);
                originPin.EnableInClassList("port-disconnecting", draggingConnection && Connected(pendingEdge) && BlankCanvas(connectionPosition));
                world.MarkDirtyRepaint(); e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            pin.RegisterCallback<PointerUpEvent>(e =>
            {
                if (capturedPort != pin || e.pointerId != connectionPointer || e.button != 0) return;
                e.StopImmediatePropagation();
                if (draggingConnection) FinishConnection(e.position, true);
                else ReleaseConnectionPointer();
            }, TrickleDown.TrickleDown);
            pin.RegisterCallback<PointerCancelEvent>(_ => CancelConnection());
            pin.RegisterCallback<PointerCaptureOutEvent>(_ => { if (capturedPort == pin) CancelConnection(); });
            return pin;
        }
        private bool Connected(FlowEdge edge) => edge != null && nodeIds.Contains(edge.targetId);
        private void StartPin(VisualElement pin, string node, FlowEdge edge, bool input)
        {
            if (originPin != null && capturedPort == null && TargetPin(pin.worldBound.center) == pin) { FinishConnection(pin.worldBound.center, false); return; }
            CancelConnection(); originPin = pin; pendingEdge = edge; fromInput = input; fixedTarget = node;
            pin.AddToClassList("port-armed"); selected(node);
        }
        private VisualElement TargetPin(Vector2 position)
        {
            if (!viewport.worldBound.Contains(position)) return null;
            return (MovingOutput ? (IEnumerable<VisualElement>)outputEdges.Keys : inputNodes.Keys).FirstOrDefault(pin => pin.enabledSelf && pin.worldBound.Contains(position));
        }
        private bool BlankCanvas(Vector2 position) => viewport.worldBound.Contains(position)
            && !miniMap.worldBound.Contains(position)
            && !cards.Values.Any(card => card.worldBound.Contains(position))
            && !inputNodes.Keys.Concat(outputEdges.Keys).Any(pin => pin.worldBound.Contains(position));
        private void FinishConnection(Vector2 position, bool allowDisconnect)
        {
            var pin = TargetPin(position); FlowEdge next = null;
            if (pin != null)
            {
                var source = MovingOutput ? outputEdges[pin] : pendingEdge;
                var target = MovingOutput ? Connected(pendingEdge) ? pendingEdge.targetId : fixedTarget : inputNodes[pin];
                next = new FlowEdge { sourceId = source.sourceId, portId = source.portId, targetId = target };
            }
            var previous = Connected(pendingEdge) ? pendingEdge : null;
            var openSearch = allowDisconnect && pin == null && BlankCanvas(position);
            var edge = pendingEdge;
            var movingOutput = MovingOutput;
            var retained = movingOutput ? new FlowEdge { targetId = previous != null ? previous.targetId : fixedTarget } : edge;
            var graphPosition = world.WorldToLocal(position);
            CancelConnection();
            if (next != null && (previous == null || previous.sourceId != next.sourceId || previous.portId != next.portId || previous.targetId != next.targetId)) changeConnection(previous, next);
            else if (openSearch) ConnectionAddRequested?.Invoke(graphPosition, previous, retained, movingOutput);
        }
        private void ReleaseConnectionPointer()
        {
            var port = capturedPort; capturedPort = null;
            if (port != null && port.HasPointerCapture(connectionPointer)) port.ReleasePointer(connectionPointer);
        }
        private void CancelConnection()
        {
            originPin = null; pendingEdge = null; draggingConnection = false; ReleaseConnectionPointer();
            foreach (var pin in inputNodes.Keys.Concat(outputEdges.Keys))
            {
                pin.RemoveFromClassList("port-armed"); pin.RemoveFromClassList("port-drop-target"); pin.RemoveFromClassList("port-disconnecting");
            }
            world.MarkDirtyRepaint();
        }
        public void ShowIssues(IEnumerable<ValidationIssue> issues, ContentIndex index)
        {
            foreach (var card in cards.Values)
            {
                card.Q("node-issues")?.RemoveFromHierarchy();
                card.RemoveFromClassList("node-error"); card.RemoveFromClassList("node-warning");
            }
            var nodeByTarget = new Dictionary<string, string>();
            foreach (var node in graph.nodes)
            {
                nodeByTarget[node.id] = node.id;
                foreach (var step in node.stepIds) nodeByTarget[step] = node.id;
            }
            foreach (var issue in issues.Where(i => i.severity != IssueSeverity.Info))
            {
                var target = issue.target;
                if (index.Find(target) is ChoiceDefinition && index.Owner(target) == graph.eventId && cards.ContainsKey(graph.eventId + ":terminal")) target = graph.eventId + ":terminal";
                while (!string.IsNullOrEmpty(target) && !nodeByTarget.ContainsKey(target)) target = index.Owner(target);
                if (string.IsNullOrEmpty(target)) continue;
                var card = cards[nodeByTarget[target]];
                var area = card.Q("node-issues");
                if (area == null) { area = new VisualElement { name = "node-issues" }; card.Add(area); }
                var error = issue.severity == IssueSeverity.Error;
                card.AddToClassList(error ? "node-error" : "node-warning");
                var message = new Label(StudioText.T(error ? "Error" : "Warning") + ": " + StudioText.T(issue.message)) { tooltip = StudioText.Issue(issue) };
                message.AddToClassList(error ? "node-error-message" : "node-warning-message"); area.Add(message);
                if (details != null)
                {
                    var targetId = issue.target;
                    var open = new Button(() => details.OpenIssue(targetId)) { text = StudioText.T("Edit"), tooltip = StudioText.Issue(issue) };
                    open.RegisterCallback<PointerDownEvent>(e => e.StopPropagation()); area.Add(open);
                }
            }
        }
        private void DrawEdges(MeshGenerationContext context)
        {
            var painter = context.painter2D; painter.lineWidth = 2; painter.strokeColor = new Color(.58f, .72f, .76f);
            foreach (var edge in graph.edges)
            {
                if (draggingConnection && edge == pendingEdge) continue;
                var key = edge.sourceId + ":" + edge.portId;
                if (!outputs.TryGetValue(key, out var source) || !inputEnds.TryGetValue(key, out var target)) continue;
                var active = edge.sourceId == traceNode && edge.targetId == traceNext && (string.IsNullOrEmpty(traceChoice) || edge.portId == traceChoice);
                var related = highlighted.Contains(edge.sourceId) || highlighted.Contains(edge.targetId) || selection.Contains(edge.sourceId) || selection.Contains(edge.targetId);
                painter.strokeColor = active ? new Color(.4f, 1, .65f) : related ? new Color(1, .82f, .45f) : new Color(.58f, .72f, .76f, highlighted.Count + selection.Count > 0 ? .35f : 1);
                painter.lineWidth = active ? 5 : related ? 3 : 2;
                DrawConnection(painter, world.WorldToLocal(source.worldBound.center), world.WorldToLocal(target.worldBound.center));
            }
            if (!draggingConnection || originPin == null) return;
            var anchorPin = originPin;
            var anchor = world.WorldToLocal(anchorPin.worldBound.center); var pointer = world.WorldToLocal(connectionPosition);
            painter.strokeColor = Connected(pendingEdge) && BlankCanvas(connectionPosition) ? new Color(1, .35f, .35f) : new Color(1, .8f, .35f);
            DrawConnection(painter, MovingOutput ? pointer : anchor, MovingOutput ? anchor : pointer);
        }
        private static void DrawConnection(Painter2D painter, Vector2 start, Vector2 end)
        {
            painter.BeginPath(); painter.MoveTo(start); painter.BezierCurveTo(start + Vector2.right * 70, end + Vector2.left * 70, end); painter.Stroke(); DrawArrow(painter, end);
        }
        private static void DrawArrow(Painter2D painter, Vector2 tip)
        {
            painter.BeginPath(); painter.MoveTo(tip + new Vector2(-8, -4)); painter.LineTo(tip); painter.LineTo(tip + new Vector2(-8, 4)); painter.Stroke();
        }
        public void FocusNode(string id)
        {
            if (!cards.ContainsKey(id)) id = graph.nodes.Find(n => n.stepIds.Contains(id))?.id ?? id;
            if (!cards.TryGetValue(id, out var node)) return;
            if (float.IsNaN(node.layout.width) || node.layout.width <= 0)
            {
                EventCallback<GeometryChangedEvent> ready = null;
                ready = _ => { node.UnregisterCallback(ready); FocusNode(id); };
                node.RegisterCallback(ready); return;
            }
            ScrollOffset = node.layout.center * Zoom - viewport.layout.size / 2;
            Highlight(new[] { id });
        }
        internal Vector2 NodeSize(string id) => cards.TryGetValue(id, out var card) && !float.IsNaN(card.layout.height) ? card.layout.size : new Vector2(320, 320);
        public void Highlight(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids);
            highlighted.Clear();
            foreach (var node in graph.nodes)
            {
                var active = set.Contains(node.id) || node.stepIds.Any(set.Contains);
                cards[node.id].EnableInClassList("node-highlight", active); if (active) highlighted.Add(node.id);
            }
            world.MarkDirtyRepaint();
        }
        public void ShowExecution(string nodeId, string nextId, string choiceId = null, IEnumerable<KeyValuePair<string, string>> notes = null)
        {
            string Card(string id) => graph.nodes.Find(n => n.id == id || n.stepIds.Contains(id))?.id ?? id;
            traceNode = Card(nodeId); traceNext = Card(nextId);
            traceChoice = choiceId; details?.ShowChoice(choiceId);
            foreach (var pair in cards) pair.Value.EnableInClassList("node-executing", pair.Key == traceNode);
            foreach (var card in cards.Values)
                foreach (var label in card.Query<Label>(className: "simulation-evidence").ToList()) label.RemoveFromHierarchy();
            if (notes != null)
                foreach (var group in notes.GroupBy(n => Card(n.Key)))
                    if (cards.TryGetValue(group.Key, out var card))
                    {
                        var label = new Label(string.Join("\n", group.Select(n => n.Value)));
                        label.AddToClassList("simulation-evidence"); card.Add(label);
                    }
            world.MarkDirtyRepaint();
        }
    }
}
