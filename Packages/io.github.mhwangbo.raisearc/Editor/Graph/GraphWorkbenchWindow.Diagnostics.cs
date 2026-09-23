using System;
using System.Linq;
using PrincessStudio.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor.Graph
{
    public sealed partial class GraphWorkbenchWindow
    {
        private bool localizationAll = true;
        private void RefreshDiagnostics()
        {
            foreach (var tab in panelTabs)
            {
                tab.Value.SetValueWithoutNotify(tab.Key == panel);
                tab.Value.EnableInClassList("tab-selected", tab.Key == panel);
            }
            if (diagnostics == null || api == null) return; diagnostics.Clear();
            switch (panel)
            {
                case "Changes":
                    diagnostics.Add(new Label(StudioText.T("Draft · base revision " + changes.baseRevision + " · " + changes.edits.Count + " operations")));
                    for (var i = 0; i < changes.edits.Count; i++)
                    {
                        var index = i; var edit = changes.edits[i];
                        diagnostics.Add(B("Remove from proposal: " + edit.operation + " · " + edit.targetId + " " + edit.eventId, () => Safe(() =>
                        {
                            var candidate = JsonUtility.FromJson<AuthoringChangeSet>(JsonUtility.ToJson(changes)); candidate.edits.RemoveAt(index);
                            var result = api.PreviewChangeSet(candidate);
                            Undo.RecordObject(this, "Remove workbench proposal operation");
                            changes = candidate; draftJson = JsonUtility.ToJson(changes); preview = result; project = preview.project; RefreshAll();
                        })));
                    }
                    if (preview != null)
                    {
                        diagnostics.Add(new Label(StudioText.T("Added: " + string.Join(", ", preview.added) + "\nChanged: " + string.Join(", ", preview.changed) + "\nRemoved: " + string.Join(", ", preview.removed))));
                        foreach (var issue in preview.validation.issues.Take(30)) diagnostics.Add(B(StudioText.Issue(issue), () => Navigate(issue.target)));
                    }
                    diagnostics.Add(new HelpBox(StudioText.T("These content edits are not saved yet. Save writes them to the project asset."), HelpBoxMessageType.Info));
                    diagnostics.Add(B("Save", ApplyChanges));
                    diagnostics.Add(B("Discard all unsaved edits", () => { if (StudioText.Dialog("Discard unsaved edits?", "All content edits since the last save will be lost. Saved graph layout is kept.", "Discard", "Cancel")) { inspectorDirty = false; Reload(); } })); break;
                case "References":
                    foreach (var inbound in new[] { true, false })
                    {
                        diagnostics.Add(new Label(StudioText.T(inbound ? "Used by" : "Uses")));
                        foreach (var reference in index.References(selectedId, inbound))
                        {
                            var row = B(ReferenceCaption(reference), () => Navigate(inbound ? reference.sourceId : reference.targetId));
                            row.tooltip = reference.sourceId + " → " + reference.targetId + "\n" + reference.sourcePropertyPath;
                            row.AddToClassList("reference-row"); diagnostics.Add(row);
                        }
                    }
                    break;
                case "Localization":
                    var scope = new Toggle(StudioText.T("All content")) { value = localizationAll };
                    scope.RegisterValueChangedCallback(e => { localizationAll = e.newValue; RefreshDiagnostics(); }); diagnostics.Add(scope);
                    var translations = new AuthoringService(project, codec, asset.CreateExtensions(project)).GetLocalizationStatus(localizationAll ? "" : selectedId)
                        .Where(x => x.missing || x.draft || x.needsReview).GroupBy(x => x.locale + "|" + x.key).Select(g => g.First()).ToList();
                    diagnostics.Add(new Label(translations.Count + " · " + StudioText.T("Missing / draft / needs review (first 200)")));
                    foreach (var entry in translations.Take(200))
                        diagnostics.Add(B(entry.locale + " · " + StudioText.T(entry.missing ? "Missing" : entry.draft ? "Draft" : "Needs review") + " · " + entry.key + (entry.fallback ? " · fallback" : ""), () => Navigate(entry.sourceId)));
                    diagnostics.Add(new Label(StudioText.T("Edit all languages in the selected node's Localization section. Graph structure is shared across languages."))); break;
                case "Trace": DrawTrace(); break;
                default:
                    var report = ProjectValidator.Validate(project, asset.CreateExtensions(project));
                    if (report.issues.Count == 0) diagnostics.Add(new Label(StudioText.T("No content validation issues.")));
                    foreach (var issue in report.issues.Take(200)) diagnostics.Add(B(StudioText.Issue(issue), () => Navigate(issue.target)));
                    break;
            }
        }
        private string ReferenceCaption(ContentReference reference)
        {
            var source = index.Find(reference.sourceId) as Definition ?? index.Find(reference.ownerId) as Definition;
            var target = index.Find(reference.targetId) as Definition;
            var sourceName = source == null ? reference.sourceId : index.Text(source.nameKey, locale);
            var targetName = target == null ? reference.targetId : index.Text(target.nameKey, locale);
            if (reference.targetId.StartsWith("loc:", StringComparison.Ordinal)) targetName = index.Text(reference.targetId.Substring(4), locale);
            return StudioText.T(reference.relationType.ToString()) + "  ·  " + sourceName + "  →  " + targetName + "     [" + reference.sourcePropertyPath + "]";
        }
        private void PlaySelection()
        {
            if (string.IsNullOrEmpty(eventId)) { Notify("Select an event flow to run a trace."); return; }
            if (!LeaveInspector()) return;
            var node = GraphProjection.Build(project, eventId).nodes.Find(x => x.id == selectedId);
            traceStartId = node != null && node.stepIds.Count > 0 ? node.stepIds[0] : eventId;
            traceChoices.Clear(); RunTrace();
        }
        private void RunTrace()
        {
            Safe(() =>
            {
                var source = new AuthoringService(project, codec, asset.CreateExtensions(project));
                if (source.ValidateProject().HasErrors) throw new InvalidOperationException("Fix draft validation errors before running a trace.");
                if (preset == null) preset = new GameSession(project).Capture();
                trace = source.SimulateFromNode(eventId, string.IsNullOrEmpty(traceStartId) ? eventId : traceStartId, preset, traceChoices);
                panel = "Trace"; RefreshDiagnostics();
                RefreshTracePlayer();
            });
        }
        private void RefreshTracePlayer(int startFrame = 0)
        {
            tracePlayback?.Stop();
            if (traceHost == null) return;
            traceHost.Clear();
            var state = new Foldout { text = StudioText.T("Test state preset"), value = false }; traceHost.Add(state);
            DrawTestState(state);
            if (trace == null) { traceHost.Add(new Label(StudioText.T("Run from Start or selection. The current step, condition results and choices appear here and on the graph."))); return; }
            tracePlayback = new RaiseArc.Editor.RaiseArcTracePlayback(project, eventId, locale, trace, canvas,
                choice => { var completed = trace.frames.Count; traceChoices.Add(choice); RunTrace(); RefreshTracePlayer(completed); }, startFrame);
            traceHost.Add(tracePlayback.Element);
        }
        private void DrawTestState(VisualElement state)
        {
            if (preset == null) { Safe(() => preset = new GameSession(project).Capture()); if (preset == null) return; }
            state.Add(new Label(StudioText.T("Preview only. These values never change your game data or saved game.")));
            state.Add(B("Reset to initial state (no active event)", () => { preset = new GameSession(project).Capture(); trace = null; traceChoices.Clear(); RefreshTracePlayer(); }));
            state.Add(B("Clear event history", () => { preset.seenEvents.Clear(); preset.pendingEventId = preset.presentationStepId = preset.endingId = ""; preset.presentationPath.Clear(); trace = null; traceChoices.Clear(); RefreshTracePlayer(); }));
            state.Add(B("Save preset…", () => {
                var path = UnityEditor.EditorUtility.SaveFilePanel("Save test state", "", "RaiseArc-test-state", "json");
                if (path.Length > 0) Safe(() => System.IO.File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(preset, true)));
            }));
            state.Add(B("Load preset…", () => {
                var path = UnityEditor.EditorUtility.OpenFilePanel("Load test state", "", "json");
                if (path.Length > 0) Safe(() => {
                    var candidate = UnityEngine.JsonUtility.FromJson<StateData>(System.IO.File.ReadAllText(path));
                    var session = new GameSession(project); session.Restore(candidate); preset = session.Capture(); trace = null; traceChoices.Clear(); RefreshTracePlayer();
                });
            }));
            void Invalidate() { trace = null; traceChoices.Clear(); tracePlayback?.Stop(); tracePlayback?.Element.Clear(); canvas?.ShowExecution(null, null); Notify("Test state changed. Run again to see the result."); }
            void Number(string label, int value, Action<int> set) { var input = new IntegerField(label) { value = value }; input.RegisterValueChangedCallback(e => { set(e.newValue); Invalidate(); }); state.Add(input); }
            Number(StudioText.T("Elapsed days"), preset.day, x => preset.day = x); Number(StudioText.T("Money"), preset.money, x => preset.money = x);
            foreach (var group in new[] { preset.stats, preset.relationships, preset.items, preset.flags, preset.actorValues })
                foreach (var item in group) Number(index.Data.entries.Find(x => x.id == item.id)?.text ?? item.id, item.value, x => item.value = x);
            var history = new Foldout { text = StudioText.T("Seen events"), value = false }; state.Add(history);
            foreach (var e in project.events)
            {
                var toggle = new Toggle(index.Text(e.nameKey, locale)) { value = preset.seenEvents.Contains(e.id) };
                toggle.RegisterValueChangedCallback(change => { preset.seenEvents.Remove(e.id); if (change.newValue) preset.seenEvents.Add(e.id); Invalidate(); }); history.Add(toggle);
            }
        }
        private void DrawTrace()
        {
            if (string.IsNullOrEmpty(eventId)) { diagnostics.Add(new Label(StudioText.T("Select an event flow to run a trace."))); return; }
            diagnostics.Add(new Label(StudioText.T("Selected-node presets supply state after preceding steps. Entry starts evaluate triggers and apply entry effects; later-node starts do not replay earlier effects.")));
            diagnostics.Add(new Label(StudioText.T("Use the playback panel beneath the graph to step, play or choose. Detailed results are listed below.")));
            if (trace == null) return;
            var sources = new System.Collections.Generic.Dictionary<string, string>();
            var playback = RaiseArc.Core.RaiseArcFlowReuse.Expand(project, project.events.Find(e => e.id == eventId), sources);
            string Source(string id) => id != null && sources.TryGetValue(id, out var original) ? original : id;
            string Caption(string id) => index.Data.entries.Find(e => e.id == Source(id))?.text ?? id;
            diagnostics.Add(new Label(StudioText.T(trace.status + " " + trace.error)));
            foreach (var condition in trace.entryConditions) diagnostics.Add(new Label(StudioText.T((condition.passed ? "✓ " : "✕ ") + condition.target + " " + condition.comparison + " " + condition.expected + " · actual " + condition.actual + " " + condition.error)));
            foreach (var frame in trace.frames.Take(100))
            {
                var box = new Foldout { text = Caption(frame.nodeId) + " → " + Caption(frame.nextStepId), value = false }; diagnostics.Add(box);
                box.Add(B("Focus node", () => Navigate(Source(frame.nodeId))));
                box.Add(new Label(StudioText.T("Money " + frame.before.money + " → " + frame.after.money + " · Day " + frame.before.day + " → " + frame.after.day)));
                foreach (var group in new[] { (frame.before.stats, frame.after.stats), (frame.before.relationships, frame.after.relationships), (frame.before.items, frame.after.items), (frame.before.flags, frame.after.flags), (frame.before.actorValues, frame.after.actorValues) })
                    foreach (var after in group.Item2) { var before = group.Item1.Find(x => x.id == after.id); if (before != null && before.value != after.value) box.Add(new Label(StudioText.T(after.id + "  " + before.value + " → " + after.value))); }
                foreach (var c in frame.conditions) box.Add(new Label(StudioText.T((c.passed ? "✓ " : "✕ ") + c.target + " " + c.comparison + " " + c.expected + " · actual " + c.actual)));
            }
        }
    }
}
