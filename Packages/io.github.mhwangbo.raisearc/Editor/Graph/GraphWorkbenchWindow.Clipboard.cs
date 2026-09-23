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
        [Serializable] private sealed class NodeClipboard
        {
            public List<PresentationStep> steps = new List<PresentationStep>();
            public List<TranslationEntry> translations = new List<TranslationEntry>();
        }
        private static bool IsTextEditing(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current is TextField || current is IntegerField || current is FloatField || current is ToolbarSearchField) return true;
            return false;
        }
        private void CopyNodes()
        {
            if (project == null || string.IsNullOrEmpty(eventId)) return;
            var graph = GraphProjection.Build(project, eventId, locale);
            var node = graph.nodes.Find(n => n.id == selectedId || n.stepIds.Contains(selectedId));
            if (node == null || node.stepIds.Count == 0) { Notify("Select an executable node; use Clipboard / Copy entire flow for Entry or End."); return; }
            var data = CopyableNodeData(eventId, node);
            EditorGUIUtility.systemCopyBuffer = "PrincessNodes:" + JsonUtility.ToJson(data);
            Notify("Copied " + data.steps.Count + " steps. Paste creates new identities and leaves external connections empty.");
        }
        private NodeClipboard CopyableNodeData(string sourceEvent, FlowNode node)
        {
            var source = project.events.Find(e => e.id == sourceEvent);
            var expanded = RaiseArc.Core.RaiseArcFlowReuse.Expand(project, source);
            var steps = expanded.presentation.Where(s => node.stepIds.Any(id => s.id == id || s.id.StartsWith(id + ".call.", StringComparison.Ordinal) || s.id.StartsWith(id + ".line.", StringComparison.Ordinal))).ToList();
            var keys = steps.Cast<Definition>().Concat(steps.SelectMany(s => s.choices)).SelectMany(d => new[] { d.nameKey, d.descriptionKey }).ToHashSet();
            return new NodeClipboard { steps = steps, translations = project.translations.Where(t => keys.Contains(t.key)).ToList() };
        }
        private void PasteNodes()
        {
            Safe(() =>
            {
                var text = EditorGUIUtility.systemCopyBuffer;
                if (!text.StartsWith("PrincessNodes:", StringComparison.Ordinal)) return;
                if (text.Length > 262144) throw new ArgumentException("Node clipboard exceeds 256 KiB.");
                var data = JsonUtility.FromJson<NodeClipboard>(text.Substring(14));
                PasteNodeData(data);
            });
        }
        private string PasteNodeData(NodeClipboard data)
        {
                if (data?.steps == null || data.steps.Count == 0 || data.steps.Count > 256) throw new ArgumentException("Clipboard requires 1–256 steps.");
                var map = data.steps.ToDictionary(s => s.id, _ => "step-" + Guid.NewGuid().ToString("N"));
                string Target(string id) => id != null && map.TryGetValue(id, out var mapped) ? mapped : "$disconnected";
                var edits = new List<GraphEdit>();
                var localization = new List<GraphEdit>();
                void CopyText(Definition definition)
                {
                    string Key(string old, string suffix)
                    {
                        if (string.IsNullOrEmpty(old)) return old;
                        var key = "copy." + definition.id + suffix;
                        foreach (var text in data.translations.Where(t => t.key == old))
                            localization.Add(new GraphEdit { operation = "AddLocalization", targetId = key, locale = text.locale, text = text.text });
                        return key;
                    }
                    definition.nameKey = Key(definition.nameKey, ".name");
                    definition.descriptionKey = Key(definition.descriptionKey, ".description");
                }
                for (var i = 0; i < data.steps.Count; i++)
                {
                    var step = data.steps[i]; step.id = map[step.id];
                    step.nextStepId = string.IsNullOrEmpty(step.nextStepId) && i + 1 < data.steps.Count ? map[data.steps[i + 1].id] : Target(step.nextStepId);
                    step.falseStepId = Target(step.falseStepId);
                    foreach (var c in step.conditions) c.id = "";
                    foreach (var f in step.effects) f.id = "";
                    foreach (var choice in step.choices)
                    {
                        choice.id = "choice-" + Guid.NewGuid().ToString("N");
                        CopyText(choice);
                        choice.nextStepId = string.IsNullOrEmpty(choice.nextStepId) ? step.nextStepId : Target(choice.nextStepId);
                        foreach (var c in choice.conditions) c.id = "";
                        foreach (var f in choice.effects) f.id = "";
                    }
                    CopyText(step);
                    edits.Add(new GraphEdit { operation = "CreateNode", eventId = eventId, node = step });
                }
                StageMany(edits.Concat(localization));
                return project.events.Find(e => e.id == eventId).presentation.Exists(s => s.id == data.steps[0].id) ? data.steps[0].id : null;
        }
    }
}
