using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Editor;
using PrincessStudio.Editor.Graph;
using UnityEditor;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    // Replays detached simulation frames; stepping never mutates the authored project.
    internal sealed class RaiseArcTracePlayback
    {
        public VisualElement Element { get; } = new VisualElement { name = "trace-playback" };
        private readonly ExecutionTrace trace;
        private readonly FlowCanvas canvas;
        private readonly ContentIndex index;
        private readonly EventDefinition flow;
        private readonly Dictionary<string, string> sources = new Dictionary<string, string>();
        private readonly string locale;
        private readonly Action<string> choose;
        private readonly VisualElement details = new VisualElement();
        private readonly Button play;
        private readonly IVisualElementScheduledItem timer;
        private readonly string eventId;
        private readonly EventDefinition authored;
        private int cursor;
        private bool playing;
        private double nextAt;
        private float seconds = 1;
        public int FrameIndex => cursor;

        public RaiseArcTracePlayback(ProjectDefinition project, string eventId, string locale, ExecutionTrace trace, FlowCanvas canvas, Action<string> choose, int startFrame = 0)
        {
            cursor = Math.Min(startFrame, trace.frames.Count);
            this.trace = trace; this.canvas = canvas; this.locale = locale; this.choose = choose; this.eventId = eventId;
            index = new ContentIndex(project, locale);
            authored = project.events.Find(e => e.id == eventId);
            flow = RaiseArc.Core.RaiseArcFlowReuse.Expand(project, authored, sources);
            if (trace.finalState != null && trace.finalState.pendingEventId.Length > 0)
            {
                var session = new GameSession(project); session.Restore(trace.finalState);
                availableIds.UnionWith(session.AvailableChoices());
            }
            var controls = new VisualElement(); controls.style.flexDirection = FlexDirection.Row; controls.style.flexWrap = Wrap.Wrap; Element.Add(controls);
            Button Add(string title, Action action) { var b = new Button(action) { text = StudioText.T(title) }; controls.Add(b); return b; }
            Add("Previous step", () => { Stop(); cursor = Math.Max(0, cursor - 1); Render(); });
            play = Add("Auto play", () => { playing = !playing; if (playing && cursor >= trace.frames.Count) { cursor = 0; Render(); } play.text = StudioText.T(playing ? "Pause" : "Auto play"); nextAt = EditorApplication.timeSinceStartup + seconds; if (playing) timer?.Resume(); else timer?.Pause(); });
            Add("Next step", Advance);
            var speed = new DropdownField(new List<string> { "0.25 s", "0.5 s", "1 s", "2 s" }, 2) { name = "trace-speed" };
            speed.tooltip = StudioText.T("Time per simulated step");
            speed.RegisterValueChangedCallback(e => { seconds = new[] { .25f, .5f, 1f, 2f }[speed.index]; nextAt = EditorApplication.timeSinceStartup + seconds; }); controls.Add(speed);
            Add("Focus current step", () => canvas?.FocusNode(CurrentSource()));
            Element.Add(details);
            timer = Element.schedule.Execute(() => { if (playing && EditorApplication.timeSinceStartup >= nextAt) { Step(); nextAt = EditorApplication.timeSinceStartup + seconds; } }).Every(50);
            timer.Pause();
            Element.RegisterCallback<DetachFromPanelEvent>(_ => Stop());
            Render();
        }
        public void Stop() { playing = false; timer?.Pause(); if (play != null) play.text = StudioText.T("Auto play"); }
        public void Advance() { Stop(); Step(); }
        private void Step() { if (cursor < trace.frames.Count) cursor++; else Stop(); Render(); }
        private string Source(string id) => id != null && sources.TryGetValue(id, out var source) ? source : id;
        private string GraphSource(string id)
        {
            if (string.IsNullOrEmpty(id) || authored.presentation.Any(s => s.id == id)) return id;
            var call = authored.presentation.FirstOrDefault(s =>
                (!string.IsNullOrEmpty(s.sharedEventId) && id.StartsWith(s.id + ".call.", StringComparison.Ordinal)) ||
                (!string.IsNullOrEmpty(s.sharedStepId) && id.StartsWith(s.id + ".line.", StringComparison.Ordinal)));
            return call?.id ?? Source(id);
        }
        private string FrameSource(SequenceTraceFrame frame) => !string.IsNullOrEmpty(frame.choiceId) && string.IsNullOrEmpty(frame.before.presentationStepId)
            ? frame.before.pendingEventId + ":terminal" : GraphSource(frame.nodeId);
        private string CurrentSource() => cursor < trace.frames.Count ? FrameSource(trace.frames[cursor]) :
            !string.IsNullOrEmpty(trace.finalState?.presentationStepId) ? GraphSource(trace.finalState.presentationStepId) :
            !string.IsNullOrEmpty(trace.finalState?.pendingEventId) ? trace.finalState.pendingEventId + ":terminal" : EventSequence.End;
        private string Caption(string id) => index.Data.entries.Find(e => e.id == Source(id))?.text ?? id ?? "";
        private void Line(string text, string css = null)
        {
            var label = new Label(text); if (css != null) label.AddToClassList(css); details.Add(label);
        }
        private void Conditions(IEnumerable<ConditionExplanation> conditions)
        {
            foreach (var c in conditions)
            {
                var parts = (c.target ?? "").Split(':'); var target = parts.Length > 1 ? Caption(parts[1]) : c.target;
                var comparator = c.comparison == "AtLeast" ? ">=" : c.comparison == "AtMost" ? "<=" : c.comparison == "Equal" ? "=" : StudioText.T(c.comparison ?? "");
                Line((string.IsNullOrEmpty(c.anyGroup) ? "" : "[OR " + c.anyGroup + "] ") + (c.passed ? "✓ " : "✕ ") + target + " " + comparator + " " + c.expected + " · " + StudioText.T("Actual") + " " + c.actual + (string.IsNullOrEmpty(c.error) ? "" : " · " + c.error));
            }
        }
        private void Render()
        {
            details.Clear();
            Line(StudioText.T("Simulation replay") + " · " + Math.Min(cursor + 1, trace.frames.Count) + " / " + trace.frames.Count);
            if (cursor < trace.frames.Count)
            {
                var frame = trace.frames[cursor];
                var next = GraphSource(frame.nextStepId);
                if (string.IsNullOrEmpty(next)) next = frame.after.pendingEventId.Length == 0 ? EventSequence.End : eventId + ":terminal";
                var chosen = Source(frame.choiceId);
                canvas?.ShowExecution(FrameSource(frame), next, chosen);
                if (!string.IsNullOrEmpty(chosen)) Line(StudioText.T("On choosing") + " · " + Caption(chosen), "trace-content");
                Line(Caption(frame.nodeId), "trace-content");
                Conditions(frame.conditions);
                if (frame.before.money != frame.after.money) Line(StudioText.T("Money") + " " + frame.before.money + " → " + frame.after.money);
                foreach (var group in new[] { (frame.before.stats, frame.after.stats), (frame.before.relationships, frame.after.relationships), (frame.before.items, frame.after.items), (frame.before.flags, frame.after.flags), (frame.before.actorValues, frame.after.actorValues) })
                    foreach (var after in group.Item2)
                    {
                        var before = group.Item1.Find(x => x.id == after.id)?.value ?? 0;
                        if (before != after.value) Line(Caption(after.id) + " " + before + " → " + after.value);
                    }
                Line(StudioText.T("Next") + " · " + (next == EventSequence.End ? StudioText.T("End") : Caption(next)));
                return;
            }
            Stop();
            if (trace.status.StartsWith("Waiting for choice", StringComparison.Ordinal))
            {
                Line(StudioText.T("Choose what happens next"), "trace-content");
                var node = trace.finalState.presentationStepId;
                canvas?.ShowExecution(string.IsNullOrEmpty(node) ? eventId + ":terminal" : GraphSource(node), null);
                var available = flow.presentation.Find(s => s.id == node)?.choices ?? flow.choices;
                // Available choices are evaluated by the same runtime session as the trace.
                foreach (var option in available.Where(c => availableIds.Contains(c.id)))
                {
                    var id = option.id;
                    details.Add(new Button(() => choose(id)) { text = index.Text(option.nameKey, locale), name = "trace-choice-" + id });
                }
            }
            else
            {
                canvas?.ShowExecution(trace.status == "Completed" ? EventSequence.End : eventId, null);
                Line(StudioText.T(trace.status) + (string.IsNullOrEmpty(trace.error) ? "" : " · " + trace.error), "trace-content");
                Conditions(trace.entryConditions);
            }
        }
        private readonly HashSet<string> availableIds = new HashSet<string>();
    }
}
