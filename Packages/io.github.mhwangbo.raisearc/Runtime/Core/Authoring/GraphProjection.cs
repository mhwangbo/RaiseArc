using System;
using System.Collections.Generic;
using System.Linq;

namespace PrincessStudio.Core
{
    [Serializable] public sealed class FlowNode
    {
        public string id, kind, title;
        public List<string> dialogue = new List<string>();
        public List<string> stepIds = new List<string>();
        public int conditions, effects, missingTranslations, images, actors, lineCount;
    }
    [Serializable] public sealed class FlowEdge { public string sourceId, portId, targetId, label; }
    [Serializable] public sealed class GraphProjection
    {
        public string eventId;
        public int revision;
        public List<FlowNode> nodes = new List<FlowNode>();
        public List<FlowEdge> edges = new List<FlowEdge>();
        public const string EndId = "$end";
        public static GraphProjection Build(ProjectDefinition p, string eventId, string locale = null)
        {
            if (string.IsNullOrEmpty(locale)) locale = p.defaultLocale;
            var e = p.events.Find(x => x.id == eventId) ?? throw new ArgumentException("Unknown event.");
            var authored = e;
            e = RaiseArc.Core.RaiseArcFlowReuse.View(p, e, false);
            var index = new ContentIndex(p, locale);
            var result = new GraphProjection { eventId = eventId, revision = p.revision };
            result.nodes.Add(new FlowNode { id = e.id, kind = "Entry", title = index.Text(e.nameKey, locale ?? p.defaultLocale), conditions = e.conditions.Count, effects = e.effects.Count });
            var incoming = new Dictionary<string, int>();
            void Count(string id) { if (string.IsNullOrEmpty(id) || id == EndId) return; incoming.TryGetValue(id, out var n); incoming[id] = n + 1; }
            Count(EventSequence.Entry(e));
            foreach (var s in e.presentation)
            {
                if (s.kind == PresentationStepKind.Choice) foreach (var c in s.choices) Count(EventSequence.Next(e, s, c.nextStepId));
                else { Count(EventSequence.Next(e, s)); if (s.kind == PresentationStepKind.Condition) Count(EventSequence.Next(e, s, s.falseStepId)); }
            }
            var nodeForStep = new Dictionary<string, string>();
            FlowNode block = null; PresentationStep previous = null;
            foreach (var s in e.presentation)
            {
                var originalStep = authored.presentation.Find(step => step.id == s.id);
                var previousOriginal = previous == null ? null : authored.presentation.Find(step => step.id == previous.id);
                var group = string.IsNullOrEmpty(originalStep.sharedStepId) && string.IsNullOrEmpty(previousOriginal?.sharedStepId) && s.kind == PresentationStepKind.Dialogue && previous != null && previous.kind == PresentationStepKind.Dialogue && EventSequence.Next(e, previous) == s.id && incoming.TryGetValue(s.id, out var count) && count == 1;
                if (!group)
                {
                    var original = authored.presentation.Find(step => step.id == s.id);
                    block = new FlowNode { id = s.id, kind = !string.IsNullOrEmpty(original.sharedEventId) ? "Shared Flow" : s.kind == PresentationStepKind.Dialogue ? "Dialogue Block" : s.kind == PresentationStepKind.Image ? "Presentation" : s.kind.ToString(), title = index.Text(s.nameKey, locale ?? p.defaultLocale) };
                    if (!string.IsNullOrEmpty(original.sharedStepId)) block.title = "↗ " + block.title;
                    result.nodes.Add(block);
                }
                block.stepIds.Add(s.id); nodeForStep[s.id] = block.id;
                if (s.kind == PresentationStepKind.Dialogue)
                {
                    if (string.IsNullOrEmpty(originalStep.sharedStepId)) block.dialogue.Add(index.Text(s.nameKey, locale));
                    else
                    {
                        try { foreach (var line in RaiseArc.Core.RaiseArcFlowReuse.ContentSteps(p, originalStep.sharedStepId)) block.dialogue.Add(index.Text(line.nameKey, locale)); }
                        catch (ArgumentException) { block.dialogue.Add(index.Text(s.nameKey, locale)); }
                    }
                }
                try { block.lineCount += string.IsNullOrEmpty(originalStep.sharedStepId) ? 1 : RaiseArc.Core.RaiseArcFlowReuse.ContentSteps(p, originalStep.sharedStepId).Count; }
                catch (ArgumentException) { block.lineCount++; }
                block.conditions += s.conditions.Count + s.choices.Sum(c => c.conditions.Count);
                block.effects += s.effects.Count + s.choices.Sum(c => c.effects.Count);
                block.images += s.images.Count; block.actors = Math.Max(block.actors, s.actors.Count);
                foreach (var key in new[] { s.nameKey }.Concat(s.choices.Select(c => c.nameKey)))
                    if (!p.translations.Exists(t => t.key == key && t.locale == (locale ?? p.defaultLocale) && !string.IsNullOrWhiteSpace(t.text))) block.missingTranslations++;
                previous = s;
            }
            var terminalId = e.id + ":terminal";
            string Target(string id) => id == EndId ? EndId : string.IsNullOrEmpty(id) ? e.choices.Count > 0 ? terminalId : EndId : nodeForStep.TryGetValue(id, out var n) ? n : id;
            void Edge(string from, string port, string to, string label) => result.edges.Add(new FlowEdge { sourceId = from, portId = port, targetId = Target(to), label = label });
            Edge(e.id, "next", EventSequence.Entry(e), "Start");
            foreach (var node in result.nodes.Where(n => n.stepIds.Count > 0))
            {
                var s = e.presentation.Find(x => x.id == node.stepIds[node.stepIds.Count - 1]);
                if (s.kind == PresentationStepKind.Choice) foreach (var c in s.choices) Edge(node.id, c.id, EventSequence.Next(e, s, c.nextStepId), index.Text(c.nameKey, locale ?? p.defaultLocale));
                else { Edge(node.id, "next", EventSequence.Next(e, s), s.kind == PresentationStepKind.Condition ? "True" : "Next"); if (s.kind == PresentationStepKind.Condition) Edge(node.id, "false", EventSequence.Next(e, s, s.falseStepId), "False"); }
            }
            if (e.choices.Count > 0)
            {
                result.nodes.Add(new FlowNode { id = terminalId, kind = "Choice", title = "Terminal choices", conditions = e.choices.Sum(c => c.conditions.Count), effects = e.choices.Sum(c => c.effects.Count) });
                foreach (var c in e.choices) Edge(terminalId, c.id, EndId, index.Text(c.nameKey, locale ?? p.defaultLocale));
            }
            result.nodes.Add(new FlowNode { id = EndId, kind = "End", title = "Return to life simulation" });
            return result;
        }
    }
}
