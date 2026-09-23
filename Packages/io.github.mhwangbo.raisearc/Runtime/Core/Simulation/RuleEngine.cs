using System;
using System.Collections.Generic;

namespace PrincessStudio.Core
{
    public sealed class RuleEngine
    {
        internal RaiseArc.Analysis.RuntimeObservations Observations;
        private readonly ProjectDefinition project;
        private readonly ExtensionRegistry extensions;
        private readonly Dictionary<string, StatDefinition> stats = new Dictionary<string, StatDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, NpcDefinition> npcs = new Dictionary<string, NpcDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemDefinition> items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        public RuleEngine(ProjectDefinition project, ExtensionRegistry extensions = null)
        {
            this.project = project;
            this.extensions = extensions ?? new ExtensionRegistry();
            foreach (var x in project.stats)
                stats.Add(x.id, x);
            foreach (var x in project.npcs)
                npcs.Add(x.id, x);
            foreach (var x in project.items)
                items.Add(x.id, x);
        }
        public bool Matches(StateSnapshot state, IReadOnlyList<ConditionSpec> conditions)
        {
            var alternatives = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (var i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i];
                bool passed;
                try { passed = Evaluate(state, c); }
                catch
                {
                    Observations?.Record("Condition", c.id, "Unknown", state.Day);
                    throw;
                }
                if (Observations != null) Observations.Entries.Add(new RaiseArc.Analysis.RuntimeObservation {
                    kind = "Condition", id = c.id, outcome = passed ? "True" : "False", day = state.Day,
                    target = c.kind + ":" + c.actorId + ":" + c.target, comparison = c.comparison.ToString(), expected = c.value,
                    hasActual = c.kind != ValueKind.Custom, actual = c.kind == ValueKind.Custom ? 0 : state.Read(c.kind, c.target, c.actorId)
                });
                if (!string.IsNullOrEmpty(c.anyGroup))
                {
                    alternatives.TryGetValue(c.anyGroup, out var previous);
                    alternatives[c.anyGroup] = previous || passed;
                    continue;
                }
                if (!passed)
                {
                    if (Observations != null)
                        for (var j = i + 1; j < conditions.Count; j++) Observations.Record("Condition", conditions[j].id, "ShortCircuit", state.Day);
                    return false;
                }
            }
            foreach (var passed in alternatives.Values) if (!passed) return false;
            return true;
        }
        private bool Evaluate(StateSnapshot state, ConditionSpec c)
        {
            if (c.kind == ValueKind.Custom) return extensions.Condition(c.target).Evaluate(state, c);
            var value = state.Read(c.kind, c.target, c.actorId);
            switch (c.comparison)
            {
                case Comparison.AtLeast: return value >= c.value;
                case Comparison.AtMost: return value <= c.value;
                case Comparison.Equal: return value == c.value;
                case Comparison.NotEqual: return value != c.value;
                default: throw new ArgumentOutOfRangeException(nameof(c.comparison));
            }
        }
        internal void Apply(GameState state, IReadOnlyList<EffectSpec> effects, bool allowCustom = true, string activityId = "")
        {
            if (effects.Count > 1024)
                throw new InvalidOperationException("Effect budget exceeded.");
            foreach (var input in effects)
            {
                var e = input;
                if (input.randomRange)
                {
                    if (input.kind == ValueKind.Custom || input.kind == ValueKind.Flag)
                        throw new ArgumentException("Random ranges require a numeric built-in effect.");
                    var value = RaiseArc.Core.GameplayRandom.Inclusive(ref state.RandomState, input.minimumValue, input.maximumValue);
                    e = new EffectSpec { id = input.id, actorId = input.actorId, kind = input.kind, target = input.target, operation = input.operation, value = value };
                    Observations?.Entries.Add(new RaiseArc.Analysis.RuntimeObservation { kind = "RandomEffect", id = e.id, day = state.Day, actual = value, hasActual = true });
                }
                Observations?.Record("Effect", e.id, "Attempted", state.Day);
                var before = Observations != null && e.kind != ValueKind.Custom ? new StateSnapshot(state, project).Read(e.kind, e.target, e.actorId) : 0;
                if (!string.IsNullOrEmpty(e.actorId))
                {
                    if (e.kind != ValueKind.Stat || !Enum.IsDefined(typeof(EffectOperation), e.operation)) throw new ArgumentException("Actor effects support conditions only.");
                    var actor = project.actors.Find(x => x.id == e.actorId) ?? throw new ArgumentException("Unknown actor.");
                    var condition = actor.conditions.Find(x => x.id == e.target) ?? throw new ArgumentException("Unknown actor condition.");
                    var key = StateSnapshot.ActorValueKey(actor.id, condition.id);
                    state.ActorValues[key] = Clamp(Change(state.ActorValues[key], e), condition.minimum, condition.maximum);
                    RecordDelta(state, e, before);
                    continue;
                }
                if (e.kind == ValueKind.Custom)
                {
                    if (!allowCustom)
                        throw new InvalidOperationException("Recursive custom effects are forbidden.");
                    Apply(state, extensions.Effect(e.target).Expand(new StateSnapshot(state, project), e), false, activityId);
                    continue;
                }
                if (e.operation != EffectOperation.Add && e.operation != EffectOperation.Set)
                    throw new ArgumentException("Invalid effect operation.");
                switch (e.kind)
                {
                    case ValueKind.Money:
                        state.Money = Change(state.Money, e);
                        if (state.Money < 0)
                            throw new InvalidOperationException("Insufficient money.");
                        break;
                    case ValueKind.Stat:
                        var stat = stats[e.target];
                        if (e.operation == EffectOperation.Add && !string.IsNullOrEmpty(activityId))
                        {
                            var amount = e.value;
                            foreach (var modifier in project.modifiers)
                                if (modifier.statId == e.target && (string.IsNullOrEmpty(modifier.activityId) || modifier.activityId == activityId) &&
                                    state.ModifierExpiry.TryGetValue(modifier.id, out var expiry) && state.Day < expiry)
                                {
                                    amount = checked(amount + modifier.additiveBonus);
                                    Observations?.Entries.Add(new RaiseArc.Analysis.RuntimeObservation { kind = "ActivityModifier", id = modifier.id,
                                        relatedId = e.id, day = state.Day, target = e.target, hasActual = true, actual = modifier.additiveBonus, outcome = "Applied" });
                                }
                            e = new EffectSpec { id = e.id, kind = e.kind, target = e.target, operation = e.operation, value = amount };
                        }
                        state.Stats[e.target] = Clamp(Change(state.Stats[e.target], e), stat.minimum, stat.maximum);
                        break;
                    case ValueKind.Relationship:
                        var npc = npcs[e.target];
                        state.Relationships[e.target] = Clamp(Change(state.Relationships[e.target], e), npc.minimum, npc.maximum);
                        break;
                    case ValueKind.Item:
                        var item = items[e.target];
                        var quantity = Change(state.Items[e.target], e);
                        if (quantity < 0 || quantity > item.maximum)
                            throw new InvalidOperationException("Item quantity outside limits: " + e.target);
                        state.Items[e.target] = quantity;
                        break;
                    case ValueKind.Flag:
                        var flag = Change(state.Flags[e.target], e);
                        if (flag != 0 && flag != 1)
                            throw new InvalidOperationException("Flags must be zero or one.");
                        if (flag == 0 && state.Flags[e.target] == 1 && project.flags.Find(f => f.id == e.target).permanent)
                            throw new InvalidOperationException("A permanent qualification cannot be revoked: " + e.target);
                        state.Flags[e.target] = flag;
                        break;
                    case ValueKind.ModifierDays:
                        if (e.operation != EffectOperation.Set || e.value < 0 || !project.modifiers.Exists(m => m.id == e.target))
                            throw new InvalidOperationException("Set a modifier duration to nonnegative game days (0 removes it).");
                        if (e.value == 0) state.ModifierExpiry.Remove(e.target);
                        else state.ModifierExpiry[e.target] = checked(state.Day + e.value);
                        break;
                    default:
                        throw new InvalidOperationException("Time and age are controlled by the core.");
                }
                RecordDelta(state, e, before);
            }
        }
        private void RecordDelta(GameState state, EffectSpec effect, int before)
        {
            if (Observations == null) return;
            var after = new StateSnapshot(state, project).Read(effect.kind, effect.target, effect.actorId);
            var requested = effect.operation == EffectOperation.Set ? (long)effect.value - before : effect.value;
            Observations.Entries.Add(new RaiseArc.Analysis.RuntimeObservation { kind = "ValueChange", id = effect.id,
                day = state.Day, target = effect.kind + ":" + effect.actorId + ":" + effect.target,
                hasDelta = true, before = before, after = after, requestedDelta = requested, appliedDelta = (long)after - before,
                outcome = requested == (long)after - before ? "Applied" : "Clamped" });
        }
        private static int Change(int current, EffectSpec effect) => effect.operation == EffectOperation.Set ? effect.value : checked(current + effect.value);
        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    }
}
