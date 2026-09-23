using System;
using System.Collections.Generic;

namespace PrincessStudio.Core
{
    [Serializable]
    public sealed class ActorDefinition : Definition
    {
        public string subjectId = "", profileId = "";
        public int startingAge = 18, birthdayOffset;
        public bool fixedAge;
        public List<StatDefinition> conditions = new List<StatDefinition>();
    }
    [Serializable]
    public sealed class AppearanceSlots
    {
        public string body = "", hair = "", outfit = "", expression = "";
        public AppearanceSlots Copy() => new AppearanceSlots { body = body, hair = hair, outfit = outfit, expression = expression };
        public void Overlay(AppearanceSlots other)
        {
            if (!string.IsNullOrEmpty(other.body)) body = other.body;
            if (!string.IsNullOrEmpty(other.hair)) hair = other.hair;
            if (!string.IsNullOrEmpty(other.outfit)) outfit = other.outfit;
            if (!string.IsNullOrEmpty(other.expression)) expression = other.expression;
        }
    }
    [Serializable]
    public sealed class AppearanceProfile : Definition
    {
        public AppearanceSlots slots = new AppearanceSlots();
    }
    [Serializable]
    public sealed class AppearanceRule : Definition
    {
        public string actorId = "";
        public int priority;
        // Zero leaves scale to the previous matching rule; positive values replace it.
        public float scale;
        public List<ConditionSpec> conditions = new List<ConditionSpec>();
        public AppearanceSlots slots = new AppearanceSlots();
    }
    [Serializable]
    public sealed class StageSlot : Definition
    {
        public float x = 0.5f, width = 0.32f, scale = 1;
    }
    [Serializable]
    public sealed class ActorPlacement
    {
        public string actorId = "", slotId = "";
        public AppearanceSlots overrides = new AppearanceSlots();
    }
    /// <summary>Stage operations and progression; restore never replays side effects.</summary>
    [Serializable]
    public sealed class PresentationStep : Definition
    {
        public PresentationStepKind kind;
        public string sharedStepId = "", sharedEventId = "";
        // Empty follows list order. "$end" finishes the event without legacy terminal choices.
        public string nextStepId = "";
        public string falseStepId = "$end";
        public List<ConditionSpec> conditions = new List<ConditionSpec>();
        public List<EffectSpec> effects = new List<EffectSpec>();
        public StageChange backgroundChange, actorsChange, imagesChange;
        public string speakerActorId = "", backgroundKey = "";
        public string voiceKey = "";
        public List<ActorPlacement> actors = new List<ActorPlacement>();
        public List<StageImage> images = new List<StageImage>();
        public List<ChoiceDefinition> choices = new List<ChoiceDefinition>();
    }
    [Serializable]
    public sealed class ActorPresentationState
    {
        public string actorId, nameKey, slotId;
        public float scale = 1;
        public AppearanceSlots slots;
        public List<string> ruleIds = new List<string>();
    }
    public sealed class AppearanceResolver
    {
        private readonly ProjectDefinition project;
        private readonly RuleEngine engine;
        private readonly Dictionary<string, ActorDefinition> actors = new Dictionary<string, ActorDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, AppearanceProfile> profiles = new Dictionary<string, AppearanceProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<AppearanceRule>> rules = new Dictionary<string, List<AppearanceRule>>(StringComparer.Ordinal);
        public AppearanceResolver(ProjectDefinition project, ExtensionRegistry extensions = null)
        {
            this.project = project;
            engine = new RuleEngine(project, extensions);
            foreach (var profile in project.appearanceProfiles) profiles.Add(profile.id, profile);
            foreach (var actor in project.actors) { actors.Add(actor.id, actor); rules.Add(actor.id, new List<AppearanceRule>()); }
            foreach (var rule in project.appearanceRules) rules[rule.actorId].Add(rule);
            foreach (var list in rules.Values)
                list.Sort((a, b) => a.priority != b.priority ? a.priority.CompareTo(b.priority) : string.CompareOrdinal(b.id, a.id));
        }
        public ActorPresentationState Resolve(StateSnapshot state, string actorId, ActorPlacement placement = null)
        {
            var actor = actors[actorId];
            var result = new ActorPresentationState { actorId = actorId, nameKey = actor.nameKey, slotId = placement?.slotId ?? "", slots = profiles[actor.profileId].slots.Copy() };
            GrowthDefinition growth = null;
            if (actor.subjectId == project.character.id)
                foreach (var candidate in project.growth)
                    if (state.Age >= candidate.minimumAge && engine.Matches(state, candidate.conditions) &&
                        (growth == null || candidate.priority > growth.priority || candidate.priority == growth.priority && string.CompareOrdinal(candidate.id, growth.id) < 0)) growth = candidate;
            if (growth != null) result.slots.body = growth.appearanceKey;
            foreach (var rule in rules[actorId])
                if (engine.Matches(state, rule.conditions)) { result.slots.Overlay(rule.slots); if (rule.scale > 0) result.scale = rule.scale; result.ruleIds.Add(rule.id); }
            if (placement != null) result.slots.Overlay(placement.overrides);
            return result;
        }
    }
}
