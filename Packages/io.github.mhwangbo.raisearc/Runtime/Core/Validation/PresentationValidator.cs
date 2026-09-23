using System;
using System.Collections.Generic;

namespace PrincessStudio.Core
{
    public static partial class ProjectValidator
    {
        private static void ActorReference(ProjectDefinition p, ValidationReport r, string owner, string actorId, ValueKind kind, string target, bool effect)
        {
            var actor = p.actors.Find(x => x.id == actorId);
            if (actor == null || !(kind == ValueKind.Stat && actor.conditions.Exists(x => x.id == target) || !effect && kind == ValueKind.Age && string.IsNullOrEmpty(target)))
                r.Add(IssueSeverity.Error, "reference.actor", owner, "Invalid actor value: " + actorId + "/" + target);
        }

        private static void ValidatePresentation(ProjectDefinition p, ValidationReport r, ExtensionRegistry extensions)
        {
            foreach (var duplicate in new ContentIndex(p).Data.duplicateIds)
                r.Add(IssueSeverity.Error, "identity.duplicate", duplicate, "Content, condition and effect IDs must be globally unique.");
            void Error(string id, string message) => r.Add(IssueSeverity.Error, "presentation.invalid", id, message);
            void Slots(string id, AppearanceSlots slots)
            {
                if (slots == null) { Error(id, "Appearance slots required."); return; }
                foreach (var key in new[] { slots.body, slots.hair, slots.outfit, slots.expression })
                    if (!string.IsNullOrEmpty(key) && !IsIdentifier(key)) Error(id, "Invalid appearance key: " + key);
            }
            if (p.presentationVersion != 1) Error(p.id, "Unsupported presentation version.");
            if (p.actors.Count > 1024 || p.appearanceRules.Count > 10000) Error(p.id, "Presentation content budget exceeded.");
            var subjects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var actor in p.actors)
            {
                if (!p.appearanceProfiles.Exists(x => x.id == actor.profileId)) Error(actor.id, "Missing appearance profile.");
                if (!string.IsNullOrEmpty(actor.subjectId) && (actor.subjectId != p.character.id && !p.npcs.Exists(x => x.id == actor.subjectId) || !subjects.Add(actor.subjectId)))
                    Error(actor.id, "Subject must be an unambiguous character or NPC reference.");
                if (actor.startingAge < 0 || actor.startingAge > 1000 || actor.birthdayOffset < 0 || actor.birthdayOffset >= p.daysPerMonth * p.monthsPerYear)
                    Error(actor.id, "Invalid age or birthday offset.");
                foreach (var condition in actor.conditions)
                    if (condition.minimum > condition.maximum || condition.initial < condition.minimum || condition.initial > condition.maximum)
                        Error(condition.id, "Invalid actor condition bounds.");
            }
            foreach (var profile in p.appearanceProfiles)
            {
                Slots(profile.id, profile.slots);
                if (string.IsNullOrEmpty(profile.slots.body)) Error(profile.id, "A default body/portrait key is required.");
            }
            var priorities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in p.appearanceRules)
            {
                if (float.IsNaN(rule.scale) || float.IsInfinity(rule.scale) || rule.scale < 0 || rule.scale > 3)
                    Error(rule.id, "Appearance scale must be 0 (inherit) or in (0,3].");
                if (!p.actors.Exists(x => x.id == rule.actorId)) Error(rule.id, "Unknown actor.");
                Slots(rule.id, rule.slots);
                Conditions(p, r, rule.id, rule.conditions, extensions);
                if (!priorities.Add(rule.actorId + ":" + rule.priority)) r.Add(IssueSeverity.Warning, "appearance.priority-tie", rule.id, "Shared priority: ordinal rule ID breaks ties per slot.");
            }
            foreach (var slot in p.stageSlots)
                if (float.IsNaN(slot.x) || float.IsNaN(slot.width) || float.IsNaN(slot.scale) || slot.x < 0 || slot.x > 1 || slot.width <= 0 || slot.width > 1 || slot.scale <= 0 || slot.scale > 3)
                    Error(slot.id, "Stage x/width must be normalized; scale must be in (0,3].");
            foreach (var e in p.events)
            {
                if (e.presentation.Count > 512) Error(e.id, "Maximum 512 dialogue steps.");
                ValidateEventSequence(p, r, e, extensions);
                foreach (var step in e.presentation)
                {
                    if (!string.IsNullOrEmpty(step.backgroundKey) && !IsIdentifier(step.backgroundKey)) Error(step.id, "Invalid background key.");
                    if (!string.IsNullOrEmpty(step.speakerActorId) && !p.actors.Exists(x => x.id == step.speakerActorId)) Error(step.id, "Unknown speaker.");
                    var visible = new HashSet<string>(StringComparer.Ordinal);
                    var occupied = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var placement in step.actors)
                    {
                        if (!p.actors.Exists(x => x.id == placement.actorId) || !visible.Add(placement.actorId)) Error(step.id, "Unknown or duplicate visible actor.");
                        if (!p.stageSlots.Exists(x => x.id == placement.slotId) || !occupied.Add(placement.slotId)) Error(step.id, "Unknown or occupied stage slot.");
                        Slots(step.id, placement.overrides);
                    }
                }
            }
        }
    }
}
