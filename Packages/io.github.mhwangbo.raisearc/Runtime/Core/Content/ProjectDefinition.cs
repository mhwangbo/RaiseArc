using System;
using System.Collections.Generic;

namespace PrincessStudio.Core
{
    public enum ValueKind
    {
        Stat, Money, Day, Age, Relationship, Item, Flag, Custom,
        RecordBest, RecordTotal, RecordAttempts, RecordPasses, ModifierDays
    }
    public enum Comparison
    {
        AtLeast, AtMost, Equal, NotEqual
    }
    public enum EffectOperation
    {
        Add, Set
    }
    public enum ActivityKind
    {
        Lesson, Work, Rest, Travel, GameMode
    }

    [Serializable]
    public sealed class ConditionSpec
    {
        // Conditions sharing a nonempty group are alternatives; different groups remain conjunctive.
        public string anyGroup = "";
        public List<RaiseArc.Core.ExtensionValue> parameters = new List<RaiseArc.Core.ExtensionValue>();
        public string id = "";
        public string actorId = "";
        public ValueKind kind;
        public string target = "";
        public Comparison comparison;
        public int value;
    }
    [Serializable]
    public sealed class EffectSpec
    {
        public List<RaiseArc.Core.ExtensionValue> parameters = new List<RaiseArc.Core.ExtensionValue>();
        public bool randomRange;
        public int minimumValue;
        public int maximumValue;
        public string id = "";
        public string actorId = "";
        public ValueKind kind;
        public string target = "";
        public EffectOperation operation;
        public int value;
    }
    [Serializable]
    public abstract class Definition
    {
        public List<string> tags = new List<string>();
        public string id = "";
        public string nameKey = "";
        public string descriptionKey = "";
    }
    [Serializable]
    public sealed class StatDefinition : Definition
    {
        public int minimum;
        public int maximum = 100;
        public int initial = 20;
        public bool isStatus;
    }
    [Serializable]
    public sealed class ActivityDefinition : Definition
    {
        public RaiseArc.Core.EvaluationDefinition evaluation = new RaiseArc.Core.EvaluationDefinition();
        public RaiseArc.Core.ActivityCheckFrequency checkFrequency;
        public string categoryId = "";
        public string imageKey = "";
        public RaiseArc.Core.SuccessChance successChance = new RaiseArc.Core.SuccessChance();
        public RaiseArc.Core.ActivityProgression progression = new RaiseArc.Core.ActivityProgression();
        public List<EffectSpec> failureEffects = new List<EffectSpec>();
        public ActivityKind category;
        public int days = 1;
        public int periods;
        public int cost;
        public int income;
        public string moduleId = "";
        public List<ConditionSpec> conditions = new List<ConditionSpec>();
        public List<EffectSpec> effects = new List<EffectSpec>();
    }
    [Serializable]
    public sealed class ChoiceDefinition : Definition
    {
        public string nextStepId = "";
        public List<ConditionSpec> conditions = new List<ConditionSpec>();
        public List<EffectSpec> effects = new List<EffectSpec>();
    }
    [Serializable]
    public sealed class EventDefinition : Definition
    {
        public RaiseArc.Core.SuccessChance triggerChance = new RaiseArc.Core.SuccessChance();
        // Empty preserves the legacy first-step entry.
        public string entryStepId = "";
        public List<PresentationStep> presentation = new List<PresentationStep>();
        public int priority;
        public bool once = true;
        public bool callOnly;
        public bool evaluateEachPeriod;
        public List<ConditionSpec> conditions = new List<ConditionSpec>();
        public List<EffectSpec> effects = new List<EffectSpec>();
        public List<ChoiceDefinition> choices = new List<ChoiceDefinition>();
    }
    [Serializable]
    public sealed class EndingDefinition : Definition
    {
        public int priority;
        public List<ConditionSpec> conditions = new List<ConditionSpec>();
    }
    [Serializable]
    public sealed class RelationshipStage
    {
        public int minimum;
        public string nameKey = "";
    }
    [Serializable]
    public sealed class NpcDefinition : Definition
    {
        public int initial;
        public int minimum = -100;
        public int maximum = 100;
        public List<RelationshipStage> stages = new List<RelationshipStage>();
    }
    [Serializable]
    public sealed class ItemDefinition : Definition
    {
        public int maximum = 999;
    }
    [Serializable]
    public sealed class FlagDefinition : Definition
    {
        public bool initial;
        public bool permanent;
    }
    [Serializable]
    public sealed class GrowthDefinition : Definition
    {
        public int minimumAge = 10;
        public int priority;
        public string appearanceKey = "";
        public List<ConditionSpec> conditions = new List<ConditionSpec>();
    }
    [Serializable]
    public sealed class CharacterDefinition : Definition
    {
        public int startingAge = 10;
    }
    [Serializable]
    public sealed class ScheduleDefinition : Definition
    {
        public int periodDays = 7;
        public List<string> activityIds = new List<string>();
    }
    [Serializable]
    public sealed class ModuleDefinition : Definition
    {
        public string scenePath = "";
        public int maximumDays = 30;
        public int maximumAbsoluteDelta = 100;
        public List<string> allowedStats = new List<string>();
        public List<string> allowedItems = new List<string>();
        public List<string> allowedNpcs = new List<string>();
        public List<string> allowedFlags = new List<string>();
        public List<ModuleEncounter> encounters = new List<ModuleEncounter>();
        public List<ModuleLocation> locations = new List<ModuleLocation>();
        public List<ModuleEnemy> enemies = new List<ModuleEnemy>();
    }
    [Serializable]
    public sealed class ModuleLocation : Definition
    {
        public int x, y;
        public List<string> connections = new List<string>();
        public List<string> encounterIds = new List<string>();
        public List<string> enemyIds = new List<string>();
    }
    [Serializable]
    public sealed class ModuleEnemy : Definition
    {
        public int health = 10;
        public int attack = 2;
        public List<EffectSpec> rewards = new List<EffectSpec>();
    }
    [Serializable]
    public sealed class ModuleEncounter : Definition
    {
        public int difficulty = 1;
        public List<EffectSpec> rewards = new List<EffectSpec>();
    }
    [Serializable]
    public sealed class TranslationEntry
    {
        public string key = "";
        public string locale = "en";
        public string text = "";
        public bool draft;
        public bool reviewed;
        public int recommendedMaxLength = 120;
    }
    [Serializable]
    public sealed class LocalizedAssetEntry
    {
        public string key = "";
        public string locale = "en";
        public string assetGuid = "";
    }
    [Serializable]
    public sealed class GlossaryEntry
    {
        public string source = "";
        public string locale = "";
        public string translation = "";
    }
    [Serializable]
    public sealed class ProjectDefinition
    {
        public const int CurrentSchema = 1;
        public int schemaVersion = CurrentSchema;
        public string id = "princess-game";
        public int revision;
        public int daysPerMonth = 30;
        public int monthsPerYear = 12;
        public int durationDays = 2880;
        public int startingMoney = 500;
        public RaiseArc.Core.TimePlanRules time = new RaiseArc.Core.TimePlanRules();
        public RaiseArc.Core.ActivityCheckFrequency defaultActivityCheckFrequency = RaiseArc.Core.ActivityCheckFrequency.OncePerActivity;
        public string defaultLocale = "en";
        public string fallbackLocale = "en";
        public List<string> locales = new List<string> { "en", "ko" };
        public CharacterDefinition character = new CharacterDefinition { id = "daughter", nameKey = "character.daughter.name" };
        public List<StatDefinition> stats = new List<StatDefinition>();
        public List<ActivityDefinition> activities = new List<ActivityDefinition>();
        public List<EventDefinition> events = new List<EventDefinition>();
        public List<EndingDefinition> endings = new List<EndingDefinition>();
        public List<NpcDefinition> npcs = new List<NpcDefinition>();
        public List<ItemDefinition> items = new List<ItemDefinition>();
        public List<FlagDefinition> flags = new List<FlagDefinition>();
        public List<RaiseArc.Core.TimedModifierDefinition> modifiers = new List<RaiseArc.Core.TimedModifierDefinition>();
        public List<GrowthDefinition> growth = new List<GrowthDefinition>();
        public List<ScheduleDefinition> schedules = new List<ScheduleDefinition>();
        public List<ModuleDefinition> modules = new List<ModuleDefinition>();
        public List<TranslationEntry> translations = new List<TranslationEntry>();
        public List<LocalizedAssetEntry> localizedAssets = new List<LocalizedAssetEntry>();
        public List<GlossaryEntry> glossary = new List<GlossaryEntry>();
        public int presentationVersion = 1;
        public List<ActorDefinition> actors = new List<ActorDefinition>();
        public List<AppearanceProfile> appearanceProfiles = new List<AppearanceProfile>();
        public List<AppearanceRule> appearanceRules = new List<AppearanceRule>();
        public List<StageSlot> stageSlots = new List<StageSlot>();

        internal ProjectDefinition WithEvents(List<EventDefinition> value)
        {
            var result = (ProjectDefinition)MemberwiseClone(); result.events = value; return result;
        }
        public IEnumerable<Definition> AllDefinitions()
        {
            yield return character;
            foreach (var actor in actors)
            {
                yield return actor;
                foreach (var condition in actor.conditions) yield return condition;
            }
            foreach (var profile in appearanceProfiles) yield return profile;
            foreach (var rule in appearanceRules) yield return rule;
            foreach (var slot in stageSlots) yield return slot;
            foreach (var x in stats)
                yield return x;
            foreach (var x in activities)
            {
                yield return x;
                if (x.progression != null) foreach (var level in x.progression.levels) yield return level;
            }
            foreach (var x in events)
            {
                yield return x;
                foreach (var step in x.presentation)
                {
                    yield return step;
                    foreach (var choice in step.choices) yield return choice;
                }
                foreach (var c in x.choices)
                    yield return c;
            }
            foreach (var x in endings)
                yield return x;
            foreach (var x in npcs)
                yield return x;
            foreach (var x in items)
                yield return x;
            foreach (var x in flags)
                yield return x;
            foreach (var x in modifiers)
                yield return x;
            foreach (var x in growth)
                yield return x;
            foreach (var x in schedules)
                yield return x;
            foreach (var x in modules)
            {
                yield return x;
                foreach (var e in x.encounters)
                    yield return e;
                foreach (var l in x.locations)
                    yield return l;
                foreach (var enemy in x.enemies)
                    yield return enemy;
            }
        }
    }
}
