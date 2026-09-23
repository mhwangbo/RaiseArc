using System;
using System.Collections.Generic;

namespace PrincessStudio.Core
{
    public static partial class ProjectValidator
    {
        private static void ValidateEventSequence(ProjectDefinition p, ValidationReport r, EventDefinition e, ExtensionRegistry extensions)
        {
            e = RaiseArc.Core.RaiseArcFlowReuse.View(p, e, false);
            void Error(string id, string message) => r.Add(IssueSeverity.Error, "sequence.invalid", id, message);
            var nodes = new Dictionary<string, PresentationStep>(StringComparer.Ordinal);
            foreach (var step in e.presentation)
            {
                if (step == null || string.IsNullOrEmpty(step.id)) { Error(e.id, "Step and ID required."); continue; }
                if (!nodes.ContainsKey(step.id)) nodes.Add(step.id, step);
                if (!Enum.IsDefined(typeof(PresentationStepKind), step.kind) || !Enum.IsDefined(typeof(StageChange), step.backgroundChange) || !Enum.IsDefined(typeof(StageChange), step.actorsChange) || !Enum.IsDefined(typeof(StageChange), step.imagesChange))
                    Error(step.id, "Unknown step kind or stage operation.");
                if (step.images.Count > 32 || step.choices.Count > 32) Error(step.id, "Maximum 32 images and 32 choices per step.");
                Conditions(p, r, step.id, step.conditions, extensions);
                Effects(p, r, step.id, step.effects, extensions);
                if (step.kind != PresentationStepKind.Condition && step.conditions.Count > 0) Error(step.id, "Only Condition steps own branch conditions.");
                if (step.kind != PresentationStepKind.Effect && step.effects.Count > 0) Error(step.id, "Only Effect steps own effects.");
                if (step.kind == PresentationStepKind.Condition && string.IsNullOrEmpty(step.falseStepId)) Error(step.id, "Condition false output must connect to a step or End.");
                var imageIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var image in step.images)
                {
                    if (image == null) { Error(step.id, "Null image."); continue; }
                    if (!IsIdentifier(image.id) || !imageIds.Add(image.id) || !IsIdentifier(image.resourceKey) || !Enum.IsDefined(typeof(StageImagePlane), image.plane)) Error(step.id, "Invalid image ID, resource or plane.");
                    if (!Unit(image.x) || !Unit(image.y) || !Unit(image.width) || !Unit(image.height) || !Unit(image.opacity) || image.width <= 0 || image.height <= 0 || image.x + image.width > 1.0001f || image.y + image.height > 1.0001f)
                        Error(step.id, "Image rectangle and opacity must be within the normalized stage.");
                }
                if (step.kind == PresentationStepKind.Choice)
                {
                    if (step.choices.Count == 0 || !step.choices.Exists(c => c.conditions.Count == 0)) Error(step.id, "Choice steps require an unconditional fallback option.");
                }
                else if (step.choices.Count > 0) Error(step.id, "Only Choice steps may contain choices.");
                foreach (var choice in step.choices)
                {
                    Conditions(p, r, choice.id, choice.conditions, extensions);
                    Effects(p, r, choice.id, choice.effects, extensions);
                }
            }
            var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var step in nodes.Values)
            {
                var targets = new List<string>(); edges.Add(step.id, targets);
                void Link(string target)
                {
                    if (target == "" || target == EventSequence.End) return;
                    if (!nodes.ContainsKey(target)) Error(step.id, "Missing next step: " + target);
                    else targets.Add(target);
                }
                // Validate even an overridden default link, so stale references never hide in data.
                if (!string.IsNullOrEmpty(step.nextStepId) && step.nextStepId != EventSequence.End && step.nextStepId != EventSequence.TerminalChoices && !nodes.ContainsKey(step.nextStepId)) Error(step.id, "Missing default next step.");
                if (step.kind == PresentationStepKind.Choice)
                    foreach (var choice in step.choices) Link(EventSequence.Next(e, step, choice.nextStepId));
                else
                {
                    Link(EventSequence.Next(e, step));
                    if (step.kind == PresentationStepKind.Condition) Link(EventSequence.Next(e, step, step.falseStepId));
                }
            }
            var entry = EventSequence.Entry(e);
            if (!string.IsNullOrEmpty(entry) && entry != EventSequence.End && !nodes.ContainsKey(entry)) Error(e.id, "Connect Start to a node or End.");
            if (nodes.Count > 512) return;
            var colors = new Dictionary<string, int>(StringComparer.Ordinal);
            void Visit(string id)
            {
                if (colors.TryGetValue(id, out var color)) { if (color == 1) Error(id, "Event sequence contains a cycle."); return; }
                colors[id] = 1;
                foreach (var next in edges[id]) Visit(next);
                colors[id] = 2;
            }
            if (nodes.ContainsKey(entry)) Visit(entry);
            foreach (var id in nodes.Keys)
                if (!colors.ContainsKey(id)) r.Add(IssueSeverity.Warning, "sequence.unreachable", id, "No path from the event entry reaches this step.");
            foreach (var id in nodes.Keys) Visit(id);
            foreach (var choice in e.choices)
                if (!string.IsNullOrEmpty(choice.nextStepId)) Error(choice.id, "Use a Choice step for branching; legacy terminal choices end the event.");
            if (e.presentation.Exists(s => s.kind == PresentationStepKind.Choice) && e.choices.Count > 0 && !e.choices.Exists(c => c.conditions.Count == 0))
                Error(e.id, "Branching events with terminal choices require an unconditional terminal fallback.");
        }
        private static bool Unit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
    }
}
