using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Core
{
    [Serializable]
    public sealed class EvaluationDefinition
    {
        public bool enabled;
        public string statId = "";
        public int passingScore;
        public string qualificationFlagId = "";
    }

    [Serializable]
    public sealed class TimedModifierDefinition : Definition
    {
        public string statId = "";
        public string activityId = "";
        public int additiveBonus;
    }

    public static class ReusableRules
    {
        public static bool IsRecord(ValueKind kind) => kind == ValueKind.RecordBest || kind == ValueKind.RecordTotal ||
            kind == ValueKind.RecordAttempts || kind == ValueKind.RecordPasses;

        public static IEnumerable<string> Errors(ProjectDefinition project)
        {
            foreach (var modifier in project.modifiers)
            {
                if (!project.stats.Exists(s => s.id == modifier.statId)) yield return modifier.id + ": choose the stat whose activity effect is adjusted.";
                if (!string.IsNullOrEmpty(modifier.activityId) && !project.activities.Exists(a => a.id == modifier.activityId))
                    yield return modifier.id + ": activity does not exist.";
            }
            foreach (var activity in project.activities)
            {
                var evaluation = activity.evaluation;
                if (evaluation == null || !evaluation.enabled) continue;
                if (!project.stats.Exists(s => s.id == evaluation.statId)) yield return activity.id + ": choose an evaluation score stat.";
                if (activity.moduleId.Length > 0) yield return activity.id + ": built-in evaluations cannot also run an external module.";
                if (evaluation.qualificationFlagId.Length > 0 && !project.flags.Exists(f => f.id == evaluation.qualificationFlagId && f.permanent))
                    yield return activity.id + ": qualification must reference a permanent flag.";
            }
        }

        internal static void ValidateState(ProjectDefinition project, StateData state)
        {
            var best = (state.recordBest ?? new List<IntEntry>()).ToDictionary(x => x.id, x => x.value);
            var total = (state.recordTotal ?? new List<IntEntry>()).ToDictionary(x => x.id, x => x.value);
            var attempts = (state.recordAttempts ?? new List<IntEntry>()).ToDictionary(x => x.id, x => x.value);
            var passes = (state.recordPasses ?? new List<IntEntry>()).ToDictionary(x => x.id, x => x.value);
            if (best.Count != attempts.Count || total.Count != attempts.Count || passes.Count != attempts.Count)
                throw new ArgumentException("Incomplete saved evaluation records.");
            foreach (var pair in attempts)
            {
                if (!project.activities.Exists(a => a.id == pair.Key && a.evaluation?.enabled == true) || pair.Value < 1 ||
                    !best.ContainsKey(pair.Key) || !total.ContainsKey(pair.Key) || !passes.TryGetValue(pair.Key, out var passed) || passed < 0 || passed > pair.Value)
                    throw new ArgumentException("Invalid saved evaluation: " + pair.Key);
            }
            foreach (var entry in state.modifierExpiry ?? new List<IntEntry>())
                if (!project.modifiers.Exists(m => m.id == entry.id) || entry.value <= state.day)
                    throw new ArgumentException("Invalid or expired saved modifier: " + entry.id);
        }
    }
}
