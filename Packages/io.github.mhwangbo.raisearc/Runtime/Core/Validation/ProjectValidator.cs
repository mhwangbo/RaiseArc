using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PrincessStudio.Core
{
    public enum IssueSeverity
    {
        Info, Warning, Error
    }
    [Serializable]
    public sealed class ValidationIssue
    {
        public IssueSeverity severity;
        public string code, target, message;
        public override string ToString() => severity + " [" + code + "] " + target + ": " + message;
    }
    [Serializable]
    public sealed class ValidationReport
    {
        public List<ValidationIssue> issues = new List<ValidationIssue>();
        public bool HasErrors => issues.Exists(x => x.severity == IssueSeverity.Error);
        public string Summary
        {
            get
            {
                var s = new StringBuilder();
                foreach (var i in issues)
                    s.AppendLine(i.ToString());
                return s.Length == 0 ? "No issues found." : s.ToString();
            }
        }
        public void Add(IssueSeverity severity, string code, string target, string message) => issues.Add(new ValidationIssue { severity = severity, code = code, target = target, message = message });
    }
    public static partial class ProjectValidator
    {
        private static readonly Regex Identifier = new Regex("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$", RegexOptions.CultureInvariant);
        public static bool IsIdentifier(string value) => value != null && Identifier.IsMatch(value);
        public static ValidationReport Validate(ProjectDefinition p, ExtensionRegistry extensions = null)
        {
            var r = new ValidationReport();
            if (extensions != null) foreach (var error in extensions.ConfigurationErrors)
                r.Add(IssueSeverity.Error, "extension.configuration", "project", error);
            if (p == null)
            {
                r.Add(IssueSeverity.Error, "project.null", "project", "Project is required.");
                return r;
            }
            try
            {
                ValidateStructure(p, r, extensions ?? new ExtensionRegistry());
                foreach (var flow in p.events)
                {
                    foreach (var step in flow.presentation)
                    {
                        try { RaiseArc.Core.RaiseArcFlowReuse.Resolve(p, step); }
                        catch (ArgumentException error) { r.Add(IssueSeverity.Error, "shared.content", step.id, error.Message); }
                    }
                    try { RaiseArc.Core.RaiseArcFlowReuse.Expand(p, flow); }
                    catch (ArgumentException error) { r.Add(IssueSeverity.Error, "shared.flow", flow.id, error.Message); }
                }
            }
            catch (Exception e) when (e is NullReferenceException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
            {
                r.Add(IssueSeverity.Error, "data.malformed", "project", "Malformed or null content: " + e.Message);
            }
            return r;
        }
        private static void ValidateStructure(ProjectDefinition p, ValidationReport r, ExtensionRegistry extensions)
        {
            foreach (var error in RaiseArc.Core.ReusableRules.Errors(p)) r.Add(IssueSeverity.Error, "rules.invalid", p.id, error);
            foreach (var error in p.time.Errors(p)) r.Add(IssueSeverity.Error, "time.invalid", p.id, error);
            void Error(string code, string id, string message) => r.Add(IssueSeverity.Error, code, id, message);
            if (p.schemaVersion != ProjectDefinition.CurrentSchema)
                Error("schema.unsupported", p.id, "Unsupported schema version.");
            if (!IsIdentifier(p.id))
                Error("id.invalid", p.id, "Use a stable alphanumeric identifier.");
            if (p.revision < 0 || p.daysPerMonth < 1 || p.daysPerMonth > 366 || p.monthsPerYear < 1 || p.monthsPerYear > 24 || p.durationDays < 1 || p.durationDays > 365000 || p.startingMoney < 0 || p.character.startingAge < 0 || p.character.startingAge > 1000)
            {
                Error("calendar.invalid", p.id, "Calendar, duration, starting age or money is outside supported limits.");
                return;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var requiredKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in p.time.periodNameKeys) requiredKeys.Add(key);
            ValidatePresentation(p, r, extensions);
            foreach (var d in p.AllDefinitions())
            {
                if (!IsIdentifier(d.id) || !ids.Add(d.id))
                    Error("id.duplicate", d.id, "IDs must be valid and globally unique, including choices and encounters.");
                if (!IsIdentifier(d.nameKey))
                    Error("key.invalid", d.id, "Name requires a localization key.");
                else
                    requiredKeys.Add(d.nameKey);
                if (!string.IsNullOrEmpty(d.descriptionKey))
                {
                    if (!IsIdentifier(d.descriptionKey))
                        Error("key.invalid", d.id, "Invalid description key.");
                    requiredKeys.Add(d.descriptionKey);
                }
            }
            foreach (var s in p.stats)
                if (s.minimum > s.maximum || s.initial < s.minimum || s.initial > s.maximum)
                    Error("range.invalid", s.id, "Initial stat must be inside its bounds.");
            foreach (var n in p.npcs)
            {
                if (n.minimum > n.maximum || n.initial < n.minimum || n.initial > n.maximum)
                    Error("range.invalid", n.id, "Invalid relationship bounds.");
                foreach (var stage in n.stages)
                {
                    if (!IsIdentifier(stage.nameKey))
                        Error("key.invalid", n.id, "Relationship stage needs a key.");
                    else
                        requiredKeys.Add(stage.nameKey);
                }
            }
            foreach (var i in p.items)
                if (i.maximum < 1)
                    Error("range.invalid", i.id, "Item maximum must be positive.");
            foreach (var a in p.activities)
            {
                if (!Enum.IsDefined(typeof(RaiseArc.Core.ActivityCheckFrequency), a.checkFrequency))
                    Error("activity.frequency", a.id, "Choose project default, once per activity, or once per day.");
                var frequency = a.checkFrequency == RaiseArc.Core.ActivityCheckFrequency.ProjectDefault ? p.defaultActivityCheckFrequency : a.checkFrequency;
                if (!string.IsNullOrEmpty(a.moduleId) && frequency == RaiseArc.Core.ActivityCheckFrequency.OncePerDay)
                    Error("activity.module-frequency", a.id, "External modules control their own elapsed time. Choose once per activity for this activity.");
                if (!Enum.IsDefined(typeof(ActivityKind), a.category) || a.days < 1 || a.days > p.durationDays || a.cost < 0 || a.income < 0)
                    Error("activity.invalid", a.id, "Invalid activity duration or economy.");
                if (!string.IsNullOrEmpty(a.moduleId) && !p.modules.Exists(m => m.id == a.moduleId))
                    Error("reference.module", a.id, "Missing module: " + a.moduleId);
                Conditions(p, r, a.id, a.conditions, extensions);
                Effects(p, r, a.id, a.effects, extensions);
                Effects(p, r, a.id, a.failureEffects, extensions);
                if (!string.IsNullOrEmpty(a.categoryId) && !IsIdentifier(a.categoryId))
                    Error("activity.category", a.id, "Custom category must be a valid ID.");
                if (a.successChance != null && a.successChance.enabled)
                {
                    ValidateChance(p, r, a.id, a.successChance);
                    if (!string.IsNullOrEmpty(a.moduleId)) Error("activity.module-chance", a.id, "External modules decide their own outcomes. Disable the built-in success check.");
                }
                if (a.progression != null)
                {
                    if (a.progression.enabled && !string.IsNullOrEmpty(a.moduleId))
                        Error("activity.module-progression", a.id, "External modules decide their own success and progression. Disable built-in activity experience for this activity.");
                    if (a.progression.experienceOnSuccess < 0 || a.progression.experienceOnFailure < 0)
                        Error("activity.experience", a.id, "Experience gains cannot be negative.");
                    foreach (var level in a.progression.levels)
                    {
                        if (level.requiredExperience < 0 || level.successBonusPercent < 0 || level.successBonusPercent > 100)
                            Error("activity.level", level.id, "Level experience must be nonnegative and chance bonus 0–100.");
                        Conditions(p, r, level.id, level.conditions, extensions);
                        Effects(p, r, level.id, level.effects, extensions);
                    }
                }
            }
            if (p.defaultActivityCheckFrequency != RaiseArc.Core.ActivityCheckFrequency.OncePerActivity && p.defaultActivityCheckFrequency != RaiseArc.Core.ActivityCheckFrequency.OncePerDay)
                Error("activity.default-frequency", p.id, "Choose once per activity or once per day as the project default.");
            foreach (var e in p.events)
            {
                ValidateChance(p, r, e.id, e.triggerChance);
                Conditions(p, r, e.id, e.conditions, extensions);
                Effects(p, r, e.id, e.effects, extensions);
                foreach (var c in e.choices)
                {
                    Conditions(p, r, c.id, c.conditions, extensions);
                    Effects(p, r, c.id, c.effects, extensions);
                }
                if (e.choices.Count > 0 && !e.choices.Exists(c => c.conditions.Count == 0))
                    r.Add(IssueSeverity.Warning, "event.no-fallback", e.id, "No unconditional choice. Event is skipped if every choice is locked.");
            }
            foreach (var e in p.endings)
            {
                var atEnding = new List<ConditionSpec>(e.conditions) { new ConditionSpec { kind = ValueKind.Day, comparison = Comparison.Equal, value = p.durationDays } };
                if (p.daysPerMonth > 0 && p.monthsPerYear > 0)
                    atEnding.Add(new ConditionSpec { kind = ValueKind.Age, comparison = Comparison.Equal, value = p.character.startingAge + p.durationDays / (p.daysPerMonth * p.monthsPerYear) });
                Conditions(p, r, e.id, atEnding, extensions);
            }
            if (!p.endings.Exists(e => e.conditions.Count == 0))
                r.Add(IssueSeverity.Warning, "ending.no-fallback", p.id, "Add an unconditional ending to cover all outcomes.");
            for (var i = 0; i < p.endings.Count; i++)
                for (var j = i + 1; j < p.endings.Count; j++)
                    if (p.endings[i].priority == p.endings[j].priority)
                        r.Add(IssueSeverity.Warning, "ending.priority-tie", p.endings[j].id, "Ties resolve by ordinal ID; consider explicit priorities.");
            foreach (var g in p.growth)
            {
                Conditions(p, r, g.id, g.conditions, extensions);
                if (!IsIdentifier(g.appearanceKey))
                    Error("appearance.key", g.id, "Growth needs a localized asset key.");
            }
            foreach (var s in p.schedules)
            {
                long days = 0;
                foreach (var id in s.activityIds)
                {
                    var a = p.activities.Find(x => x.id == id);
                    if (a == null)
                        Error("reference.activity", s.id, "Unknown activity: " + id);
                    else
                        days += a.days;
                }
                if (s.periodDays < 1 || s.activityIds.Count > 366 || days > s.periodDays)
                    Error("schedule.overflow", s.id, "Activities exceed the schedule period or entry limit.");
            }
            foreach (var m in p.modules)
            {
                if (m.maximumDays < 1 || m.maximumDays > p.durationDays || m.maximumAbsoluteDelta < 1)
                    Error("module.budget", m.id, "Invalid module limits.");
                if (string.IsNullOrEmpty(m.scenePath))
                    r.Add(IssueSeverity.Warning, "module.scene", m.id, "Custom Scene adapter requires a scene path.");
                foreach (var id in m.allowedStats)
                    Reference(p, r, m.id, ValueKind.Stat, id);
                foreach (var id in m.allowedItems)
                    Reference(p, r, m.id, ValueKind.Item, id);
                foreach (var id in m.allowedNpcs)
                    Reference(p, r, m.id, ValueKind.Relationship, id);
                foreach (var id in m.allowedFlags)
                    Reference(p, r, m.id, ValueKind.Flag, id);
                foreach (var e in m.encounters)
                {
                    if (e.difficulty < 0)
                        Error("encounter.difficulty", e.id, "Difficulty cannot be negative.");
                    Effects(p, r, e.id, e.rewards, extensions);
                }
                foreach (var enemy in m.enemies)
                {
                    if (enemy.health < 1 || enemy.attack < 0)
                        Error("enemy.range", enemy.id, "Health must be positive and attack nonnegative.");
                    Effects(p, r, enemy.id, enemy.rewards, extensions);
                }
                foreach (var location in m.locations)
                {
                    foreach (var id in location.connections)
                        if (!m.locations.Exists(x => x.id == id))
                            Error("map.connection", location.id, "Unknown location: " + id);
                    foreach (var id in location.encounterIds)
                        if (!m.encounters.Exists(x => x.id == id))
                            Error("map.encounter", location.id, "Unknown encounter: " + id);
                    foreach (var id in location.enemyIds)
                        if (!m.enemies.Exists(x => x.id == id))
                            Error("map.enemy", location.id, "Unknown enemy: " + id);
                }
            }
            var locales = new HashSet<string>(StringComparer.Ordinal);
            foreach (var locale in p.locales)
                if (!IsIdentifier(locale) || !locales.Add(locale))
                    Error("locale.invalid", locale, "Invalid or duplicate locale.");
            if (!locales.Contains(p.defaultLocale) || !locales.Contains(p.fallbackLocale))
                Error("locale.fallback", p.id, "Default and fallback locales must be enabled.");
            var texts = new Dictionary<string, TranslationEntry>(StringComparer.Ordinal);
            foreach (var t in p.translations)
            {
                var composite = t.locale + ":" + t.key;
                if (!IsIdentifier(t.key) || !locales.Contains(t.locale) || texts.ContainsKey(composite))
                {
                    Error("translation.invalid", t.key, "Invalid/duplicate key or unknown locale.");
                    continue;
                }
                texts.Add(composite, t);
                requiredKeys.Add(t.key);
                if (t.draft)
                    r.Add(IssueSeverity.Warning, "translation.draft", composite, "Translation draft needs human review.");
                if (t.text == null)
                    Error("translation.null", composite, "Text cannot be null.");
                else if (t.recommendedMaxLength > 0 && t.text.Length > t.recommendedMaxLength)
                    r.Add(IssueSeverity.Warning, "translation.length", composite, "Exceeds suggested character budget; measure the rendered UI.");
                if (t.locale.StartsWith("ar", StringComparison.Ordinal) || t.locale.StartsWith("he", StringComparison.Ordinal))
                    r.Add(IssueSeverity.Info, "translation.rtl", composite, "Verify shaping, reading order and mirrored layout in the target UI.");
            }
            foreach (var key in requiredKeys)
                foreach (var locale in p.locales)
                    if (!texts.TryGetValue(locale + ":" + key, out var t) || string.IsNullOrWhiteSpace(t.text))
                        r.Add(IssueSeverity.Warning, "translation.missing", locale + ":" + key, "Missing translation.");
            var assets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in p.localizedAssets)
                if (!IsIdentifier(a.key) || !locales.Contains(a.locale) || !Regex.IsMatch(a.assetGuid ?? "", "^[a-fA-F0-9]{32}$") || !assets.Add(a.locale + ":" + a.key))
                    Error("asset.invalid", a.key, "Localized asset requires a unique locale/key and an asset GUID.");
            foreach (var g in p.growth)
                foreach (var locale in p.locales)
                    if (!assets.Contains(locale + ":" + g.appearanceKey))
                        r.Add(IssueSeverity.Warning, "appearance.missing", g.id, "No appearance asset for " + locale);
            foreach (var term in p.glossary)
            {
                if (string.IsNullOrWhiteSpace(term.source) || string.IsNullOrWhiteSpace(term.translation) || !locales.Contains(term.locale))
                {
                    Error("glossary.invalid", term.source, "Glossary needs a source, target and enabled locale.");
                    continue;
                }
                foreach (var source in p.translations)
                    if (source.locale == p.defaultLocale && source.text != null && source.text.IndexOf(term.source, StringComparison.OrdinalIgnoreCase) >= 0 && texts.TryGetValue(term.locale + ":" + source.key, out var translated) && translated.text != null && translated.text.IndexOf(term.translation, StringComparison.OrdinalIgnoreCase) < 0)
                        r.Add(IssueSeverity.Warning, "glossary.mismatch", translated.key, "Expected term: " + term.translation);
            }
        }
        private static void Conditions(ProjectDefinition p, ValidationReport r, string id, List<ConditionSpec> conditions, ExtensionRegistry ext)
        {
            var bounds = new Dictionary<string, Tuple<long, long, HashSet<int>>>(StringComparer.Ordinal);
            foreach (var c in conditions)
            {
                if (!string.IsNullOrEmpty(c.id) && !IsIdentifier(c.id)) r.Add(IssueSeverity.Error, "identity.invalid", id, "Invalid condition ID.");
                if (!Enum.IsDefined(typeof(Comparison), c.comparison))
                    r.Add(IssueSeverity.Error, "condition.operator", id, "Unknown comparison.");
                if (c.kind == ValueKind.Custom)
                {
                    if (!string.IsNullOrEmpty(c.actorId)) r.Add(IssueSeverity.Error, "reference.actor", id, "Custom conditions cannot target an actor.");
                    if (!ext.HasCondition(c.target))
                        r.Add(IssueSeverity.Error, "extension.condition", id, "Unregistered custom condition: " + c.target);
                    var definition = ext.Definition(c.target);
                    if (definition != null) foreach (var error in definition.Errors(p, c.parameters))
                        r.Add(IssueSeverity.Error, "extension.parameter", id, error);
                    continue;
                }
                if (!string.IsNullOrEmpty(c.actorId)) ActorReference(p, r, id, c.actorId, c.kind, c.target, false);
                else Reference(p, r, id, c.kind, c.target);
                if (!string.IsNullOrEmpty(c.anyGroup)) continue; // OR alternatives cannot be intersected as AND bounds.
                var key = c.actorId + ":" + c.kind + ":" + c.target;
                if (!bounds.TryGetValue(key, out var b))
                {
                    long min = int.MinValue, max = int.MaxValue;
                    if (c.kind == ValueKind.Stat)
                    {
                        var s = p.stats.Find(x => x.id == c.target);
                        if (s != null)
                        {
                            min = s.minimum;
                            max = s.maximum;
                        }
                    }
                    if (c.kind == ValueKind.Relationship)
                    {
                        var n = p.npcs.Find(x => x.id == c.target);
                        if (n != null)
                        {
                            min = n.minimum;
                            max = n.maximum;
                        }
                    }
                    if (c.kind == ValueKind.Day)
                    {
                        min = 0;
                        max = p.durationDays;
                    }
                    if (c.kind == ValueKind.Age && p.daysPerMonth > 0 && p.monthsPerYear > 0)
                    {
                        min = p.character.startingAge;
                        max = min + p.durationDays / ((long)p.daysPerMonth * p.monthsPerYear);
                    }
                    if (c.kind == ValueKind.Money)
                        min = 0;
                    if (c.kind == ValueKind.Item)
                    {
                        min = 0;
                        var item = p.items.Find(x => x.id == c.target);
                        if (item != null)
                            max = item.maximum;
                    }
                    if (c.kind == ValueKind.Flag)
                    {
                        min = 0;
                        max = 1;
                    }
                    if (!string.IsNullOrEmpty(c.actorId))
                    {
                        var actor = p.actors.Find(x => x.id == c.actorId);
                        var stat = actor?.conditions.Find(x => x.id == c.target);
                        if (c.kind == ValueKind.Stat && stat != null) { min = stat.minimum; max = stat.maximum; }
                        if (c.kind == ValueKind.Age && actor != null)
                        {
                            min = actor.subjectId == p.character.id ? p.character.startingAge : actor.startingAge;
                            max = min + (actor.fixedAge && actor.subjectId != p.character.id ? 0 : (p.durationDays + (actor.subjectId == p.character.id ? 0 : actor.birthdayOffset)) / ((long)p.daysPerMonth * p.monthsPerYear));
                        }
                    }
                    b = Tuple.Create(min, max, new HashSet<int>());
                }
                long lo = b.Item1, hi = b.Item2;
                if (c.comparison == Comparison.AtLeast || c.comparison == Comparison.Equal)
                    lo = Math.Max(lo, c.value);
                if (c.comparison == Comparison.AtMost || c.comparison == Comparison.Equal)
                    hi = Math.Min(hi, c.value);
                if (c.comparison == Comparison.NotEqual)
                    b.Item3.Add(c.value);
                bounds[key] = Tuple.Create(lo, hi, b.Item3);
            }
            foreach (var b in bounds.Values)
            {
                var impossible = b.Item1 > b.Item2;
                if (!impossible && b.Item2 - b.Item1 < b.Item3.Count)
                {
                    impossible = true;
                    for (long i = b.Item1; i <= b.Item2; i++)
                        if (!b.Item3.Contains((int)i))
                        {
                            impossible = false;
                            break;
                        }
                }
                if (impossible)
                    r.Add(IssueSeverity.Error, "condition.unreachable", id, "Conjunctive conditions contradict each other or the allowed value range.");
            }
        }
        private static void ValidateChance(ProjectDefinition p, ValidationReport r, string id, RaiseArc.Core.SuccessChance chance)
        {
            if (chance == null || !chance.enabled) return;
            if (chance.basePercent < 0 || chance.basePercent > 100 || chance.statBonuses.Count > 128)
                r.Add(IssueSeverity.Error, "chance.invalid", id, "Chance must be 0–100 percent; at most 128 stat bonuses.");
            foreach (var bonus in chance.statBonuses)
                if (!p.stats.Exists(s => s.id == bonus.statId) || bonus.percentPerPoint < 0 || bonus.percentPerPoint > 100)
                    r.Add(IssueSeverity.Error, "chance.stat", id, "Choose an existing stat and 0–100 percentage points per stat point.");
        }
        private static void Effects(ProjectDefinition p, ValidationReport r, string id, List<EffectSpec> effects, ExtensionRegistry ext)
        {
            if (effects.Count > 1024)
                r.Add(IssueSeverity.Error, "effect.budget", id, "Too many effects.");
            foreach (var e in effects)
            {
                if (e.randomRange && (e.minimumValue > e.maximumValue || e.kind == ValueKind.Custom || e.kind == ValueKind.Flag))
                    r.Add(IssueSeverity.Error, "effect.random-range", id, "Random effects need minimum ≤ maximum and a numeric built-in value.");
                if (!string.IsNullOrEmpty(e.id) && !IsIdentifier(e.id)) r.Add(IssueSeverity.Error, "identity.invalid", id, "Invalid effect ID.");
                if (!Enum.IsDefined(typeof(EffectOperation), e.operation))
                    r.Add(IssueSeverity.Error, "effect.operator", id, "Unknown operation.");
                if (e.kind == ValueKind.Custom)
                {
                    if (!string.IsNullOrEmpty(e.actorId)) r.Add(IssueSeverity.Error, "reference.actor", id, "Custom effects cannot target an actor.");
                    if (!ext.HasEffect(e.target))
                        r.Add(IssueSeverity.Error, "extension.effect", id, "Unregistered custom effect: " + e.target);
                    var definition = ext.Definition(e.target);
                    if (definition != null) foreach (var error in definition.Errors(p, e.parameters))
                        r.Add(IssueSeverity.Error, "extension.parameter", id, error);
                    continue;
                }
                if (!string.IsNullOrEmpty(e.actorId)) ActorReference(p, r, id, e.actorId, e.kind, e.target, true);
                else Reference(p, r, id, e.kind, e.target);
                if (e.kind == ValueKind.Day || e.kind == ValueKind.Age)
                    r.Add(IssueSeverity.Error, "effect.time", id, "Use activity duration or module elapsed time.");
                if (RaiseArc.Core.ReusableRules.IsRecord(e.kind))
                    r.Add(IssueSeverity.Error, "effect.record", id, "Evaluation records are written by activities, not effects.");
                if (e.kind == ValueKind.ModifierDays && (e.operation != EffectOperation.Set || e.value < 0 || e.randomRange))
                    r.Add(IssueSeverity.Error, "effect.modifier", id, "Set a nonnegative duration in game days; 0 removes the modifier.");
                if (e.kind == ValueKind.Flag && p.flags.Exists(f => f.id == e.target && f.permanent) && (e.operation != EffectOperation.Set || e.value != 1))
                    r.Add(IssueSeverity.Error, "effect.qualification", id, "Permanent qualifications can only be granted (Set 1).");
                if (e.kind == ValueKind.Flag && e.operation == EffectOperation.Set && e.value != 0 && e.value != 1)
                    r.Add(IssueSeverity.Error, "effect.flag", id, "Flags must be zero or one.");
            }
        }
        private static void Reference(ProjectDefinition p, ValidationReport r, string owner, ValueKind kind, string target)
        {
            bool found;
            switch (kind)
            {
                case ValueKind.Stat:
                    found = p.stats.Exists(x => x.id == target);
                    break;
                case ValueKind.Relationship:
                    found = p.npcs.Exists(x => x.id == target);
                    break;
                case ValueKind.Item:
                    found = p.items.Exists(x => x.id == target);
                    break;
                case ValueKind.Flag:
                    found = p.flags.Exists(x => x.id == target);
                    break;
                case ValueKind.RecordBest:
                case ValueKind.RecordTotal:
                case ValueKind.RecordAttempts:
                case ValueKind.RecordPasses:
                    found = p.activities.Exists(x => x.id == target && x.evaluation?.enabled == true);
                    break;
                case ValueKind.ModifierDays:
                    found = p.modifiers.Exists(x => x.id == target);
                    break;
                case ValueKind.Money:
                case ValueKind.Day:
                case ValueKind.Age:
                    found = string.IsNullOrEmpty(target);
                    break;
                default:
                    found = false;
                    break;
            }
            if (!found)
                r.Add(IssueSeverity.Error, "reference.missing", owner, "Unknown " + kind + ": " + target);
        }
    }
}
