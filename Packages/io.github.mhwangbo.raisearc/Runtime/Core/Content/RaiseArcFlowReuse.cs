using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Core
{
    public static class RaiseArcFlowReuse
    {
        public static string ChoiceId(string instance, string source) => instance + ".shared." + source;

        internal static IReadOnlyList<PresentationStep> ContentSteps(ProjectDefinition project, string id)
        {
            var owner = project.events.Find(e => e.presentation.Exists(s => s.id == id));
            if (owner == null) throw new ArgumentException("Shared content is missing: " + id);
            var source = owner.presentation.Find(s => s.id == id);
            var result = new List<PresentationStep> { source };
            if (source.kind != PresentationStepKind.Dialogue) return result;
            var incoming = new Dictionary<string, int>();
            void Count(string target) { if (!string.IsNullOrEmpty(target)) { incoming.TryGetValue(target, out var count); incoming[target] = count + 1; } }
            Count(EventSequence.Entry(owner));
            foreach (var step in owner.presentation)
            {
                if (step.kind == PresentationStepKind.Choice) foreach (var choice in step.choices) Count(EventSequence.Next(owner, step, choice.nextStepId));
                else { Count(EventSequence.Next(owner, step)); if (step.kind == PresentationStepKind.Condition) Count(EventSequence.Next(owner, step, step.falseStepId)); }
            }
            for (var i = owner.presentation.IndexOf(source) + 1; i < owner.presentation.Count; i++)
            {
                var next = owner.presentation[i];
                if (next.kind != PresentationStepKind.Dialogue || !string.IsNullOrEmpty(next.sharedStepId) || !string.IsNullOrEmpty(next.sharedEventId) || EventSequence.Next(owner, result[result.Count - 1]) != next.id || !incoming.TryGetValue(next.id, out var count) || count != 1) break;
                result.Add(next);
            }
            return result;
        }

        internal static PresentationStep Resolve(ProjectDefinition project, PresentationStep instance)
        {
            if (string.IsNullOrEmpty(instance.sharedStepId)) return instance;
            if (!string.IsNullOrEmpty(instance.sharedEventId)) throw new ArgumentException("A node cannot share content and call a flow at the same time.");
            var source = project.events.SelectMany(e => e.presentation).FirstOrDefault(s => s.id == instance.sharedStepId);
            if (source == null) throw new ArgumentException("Shared content is missing: " + instance.sharedStepId);
            if (!string.IsNullOrEmpty(source.sharedStepId) || !string.IsNullOrEmpty(source.sharedEventId)) throw new ArgumentException("Select original content, not another shared instance.");
            var result = Copy(source, instance.id);
            result.nextStepId = instance.nextStepId; result.falseStepId = instance.falseStepId;
            result.choices = source.choices.Select(c => Copy(c, ChoiceId(instance.id, c.id),
                instance.choices.Find(local => local.id == ChoiceId(instance.id, c.id))?.nextStepId ?? instance.nextStepId)).ToList();
            return result;
        }

        internal static EventDefinition View(ProjectDefinition project, EventDefinition source, bool strict = true)
        {
            if (!source.presentation.Any(s => !string.IsNullOrEmpty(s.sharedStepId))) return source;
            var result = Copy(source);
            result.presentation = source.presentation.Select(step =>
            {
                try { return Resolve(project, step); }
                catch (ArgumentException) { if (strict) throw; return step; }
            }).ToList();
            return result;
        }

        // Call sites expand into deterministic step IDs, so save/restore uses the existing
        // presentation path and never replays entry effects when returning from a call.
        public static EventDefinition Expand(ProjectDefinition project, EventDefinition source, IDictionary<string, string> sourceIds = null)
        {
            if (!source.presentation.Any(s => !string.IsNullOrEmpty(s.sharedStepId) || !string.IsNullOrEmpty(s.sharedEventId))) return source;
            var result = Copy(source);
            result.presentation = new List<PresentationStep>();
            var active = new HashSet<string>();
            var ids = new HashSet<string>();
            void Add(PresentationStep step, string originalId)
            {
                if (result.presentation.Count >= 4096) throw new ArgumentException("Shared flows expand beyond 4096 steps.");
                if (!ids.Add(step.id)) throw new ArgumentException("Shared flow generated a duplicate step ID: " + step.id);
                result.presentation.Add(step);
                if (sourceIds != null)
                {
                    sourceIds[step.id] = originalId;
                    if (step.choices.Count > 0)
                    {
                        var originalChoices = project.events.SelectMany(e => e.presentation).FirstOrDefault(s => s.id == originalId)?.choices
                            ?? project.events.Find(e => e.id == originalId)?.choices;
                        if (originalChoices != null && originalChoices.Count == step.choices.Count)
                            for (var i = 0; i < step.choices.Count; i++) sourceIds[step.choices[i].id] = originalChoices[i].id;
                    }
                }
            }
            void Emit(EventDefinition flow, string prefix, string returnTo, int depth)
            {
                if (depth > 16 || !active.Add(flow.id)) throw new ArgumentException("Shared flow calls are recursive: " + flow.id);
                var view = View(project, flow);
                string Target(string target)
                {
                    if (target == EventSequence.End) return returnTo;
                    if (string.IsNullOrEmpty(target) || target == EventSequence.TerminalChoices)
                        return depth == 0 ? EventSequence.TerminalChoices : flow.choices.Count > 0 ? prefix + "terminal" : returnTo;
                    return prefix + target;
                }
                foreach (var raw in view.presentation)
                {
                    var original = flow.presentation.Find(s => s.id == raw.id);
                    var step = Copy(raw, prefix + raw.id);
                    step.nextStepId = Target(EventSequence.Next(view, raw));
                    step.falseStepId = Target(EventSequence.Next(view, raw, raw.falseStepId));
                    step.choices = raw.choices.Select(c => Copy(c, prefix + c.id, Target(EventSequence.Next(view, raw, c.nextStepId)))).ToList();
                    if (string.IsNullOrEmpty(original.sharedEventId))
                    {
                        var content = string.IsNullOrEmpty(original.sharedStepId) ? null : ContentSteps(project, original.sharedStepId);
                        if (content == null || content.Count == 1) { Add(step, string.IsNullOrEmpty(original.sharedStepId) ? raw.id : original.sharedStepId); continue; }
                        for (var i = 0; i < content.Count; i++)
                        {
                            var line = Copy(content[i], i == 0 ? step.id : step.id + ".line." + content[i].id);
                            line.nextStepId = i + 1 < content.Count ? step.id + ".line." + content[i + 1].id : step.nextStepId;
                            Add(line, content[i].id);
                        }
                        continue;
                    }
                    if (!string.IsNullOrEmpty(original.sharedStepId)) throw new ArgumentException("Choose either shared content or a shared flow.");
                    var called = project.events.Find(e => e.id == original.sharedEventId) ?? throw new ArgumentException("Shared flow is missing: " + original.sharedEventId);
                    var nested = step.id + ".call.";
                    var destination = step.nextStepId;
                    var calledEntry = EventSequence.Entry(called);
                    var enter = calledEntry == EventSequence.End ? destination : calledEntry.Length > 0 ? nested + calledEntry : called.choices.Count > 0 ? nested + "terminal" : destination;
                    Add(new PresentationStep { id = step.id, nameKey = called.nameKey, kind = PresentationStepKind.Condition,
                        conditions = called.conditions, nextStepId = nested + "effects", falseStepId = destination,
                        backgroundChange = StageChange.Keep, actorsChange = StageChange.Keep, imagesChange = StageChange.Keep }, original.id);
                    Add(new PresentationStep { id = nested + "effects", nameKey = called.nameKey, kind = PresentationStepKind.Effect,
                        effects = called.effects, nextStepId = enter,
                        backgroundChange = StageChange.Keep, actorsChange = StageChange.Keep, imagesChange = StageChange.Keep }, called.id);
                    Emit(called, nested, destination, depth + 1);
                }
                if (depth > 0 && flow.choices.Count > 0)
                    Add(new PresentationStep { id = prefix + "terminal", nameKey = flow.nameKey, kind = PresentationStepKind.Choice,
                        nextStepId = returnTo, choices = flow.choices.Select(c => Copy(c, prefix + c.id, returnTo)).ToList(),
                        backgroundChange = StageChange.Keep, actorsChange = StageChange.Keep, imagesChange = StageChange.Keep }, flow.id);
                active.Remove(flow.id);
            }
            Emit(source, "", EventSequence.End, 0);
            return result;
        }

        private static EventDefinition Copy(EventDefinition e) => new EventDefinition
        {
            id = e.id, nameKey = e.nameKey, descriptionKey = e.descriptionKey, tags = e.tags,
            entryStepId = e.entryStepId, priority = e.priority, once = e.once, callOnly = e.callOnly, conditions = e.conditions, effects = e.effects, choices = e.choices, triggerChance = e.triggerChance
        };
        private static ChoiceDefinition Copy(ChoiceDefinition c, string id, string next) => new ChoiceDefinition
        {
            id = id, nameKey = c.nameKey, descriptionKey = c.descriptionKey, tags = c.tags,
            conditions = c.conditions, effects = c.effects, nextStepId = next
        };
        private static PresentationStep Copy(PresentationStep s, string id) => new PresentationStep
        {
            id = id, nameKey = s.nameKey, descriptionKey = s.descriptionKey, tags = s.tags, kind = s.kind,
            nextStepId = s.nextStepId, falseStepId = s.falseStepId, conditions = s.conditions, effects = s.effects,
            backgroundChange = s.backgroundChange, actorsChange = s.actorsChange, imagesChange = s.imagesChange,
            speakerActorId = s.speakerActorId, backgroundKey = s.backgroundKey, voiceKey = s.voiceKey, actors = s.actors, images = s.images, choices = s.choices
        };
    }
}
