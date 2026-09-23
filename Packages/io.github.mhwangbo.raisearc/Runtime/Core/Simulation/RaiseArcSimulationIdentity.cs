using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PrincessStudio.Core;

namespace RaiseArc.Analysis
{
    public static class SimulationIdentity
    {
        internal static string Pack(IEnumerable<string> values) => string.Concat(values.Select(v => v == null ? "-1:" : v.Length.ToString(CultureInfo.InvariantCulture) + ":" + v));
        public static string Hash(string value)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        public static string StateKey(StateData s, IEnumerable<ModuleState> modules)
        {
            var b = new StringBuilder();
            void Add(string v) { v = v ?? ""; b.Append(v.Length).Append(':').Append(v); }
            void Number(int v) => Add(v.ToString(CultureInfo.InvariantCulture));
            void Map(List<IntEntry> values) { Number(values.Count); foreach (var e in values.OrderBy(x => x.id, StringComparer.Ordinal)) { Add(e.id); Number(e.value); } }
            Number(s.day); Number(s.money); Add(s.pendingEventId); Add(s.presentationStepId); Add(s.endingId);
            Number(s.period); Number(s.periodsPerDay); Number(s.remainingActivityPeriods);
            Add(s.pendingActivityId); Number(s.remainingActivityDays); Add(s.randomState.ToString(CultureInfo.InvariantCulture));
            Map(s.activityExperience ?? new List<IntEntry>());
            Map(s.recordBest ?? new List<IntEntry>()); Map(s.recordTotal ?? new List<IntEntry>());
            Map(s.recordAttempts ?? new List<IntEntry>()); Map(s.recordPasses ?? new List<IntEntry>());
            Map(s.modifierExpiry ?? new List<IntEntry>());
            Map(s.stats); Map(s.relationships); Map(s.items); Map(s.flags); Map(s.actorValues);
            Number(s.seenEvents.Count); foreach (var id in s.seenEvents.OrderBy(x => x, StringComparer.Ordinal)) Add(id);
            Number(s.presentationPath.Count); foreach (var id in s.presentationPath) Add(id);
            foreach (var m in modules.OrderBy(x => x.id, StringComparer.Ordinal)) { Add(m.id); Add(m.payload); }
            // Revision is an optimistic-concurrency counter, not a built-in gameplay value. Custom rules are opaque.
            return b.ToString(); // Keep canonical values, not only hashes: collisions cannot merge states.
        }
        internal static double Next(ref uint state)
        {
            if (state == 0) state = 0x9e3779b9;
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return state / 4294967296.0;
        }
        internal static int Select(IReadOnlyList<double> weights, ref uint random)
        {
            double total = 0;
            foreach (var weight in weights)
            {
                if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0) throw new ArgumentException("Weights must be finite and non-negative.");
                total += weight;
            }
            if (total <= 0 || double.IsInfinity(total)) throw new ArgumentException("Available weights must have a finite positive sum; no uniform fallback was applied.");
            var selected = Next(ref random) * total;
            for (var i = 0; i < weights.Count; i++) { selected -= weights[i]; if (selected < 0) return i; }
            return weights.Count - 1;
        }
    }
}
