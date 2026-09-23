using System;
using System.Collections.Generic;

namespace RaiseArc.Analysis
{
    [Serializable] public sealed class RuntimeObservation
    {
        public string kind, id, relatedId, outcome;
        public string ownerId;
        public int day;
        public int cost, income;
        public string target, comparison;
        public int actual, expected;
        public bool hasActual;
        public bool committed;
        public bool hasDelta;
        public int before, after;
        public long requestedDelta, appliedDelta;
    }

    // A passive buffer, not callbacks: recording cannot invoke extension code or change rule order.
    internal sealed class RuntimeObservations
    {
        public readonly List<RuntimeObservation> Entries = new List<RuntimeObservation>();
        public void Record(string kind, string id, string outcome = "", int day = 0, string relatedId = "") =>
            Entries.Add(new RuntimeObservation { kind = kind, id = id, outcome = outcome, day = day, relatedId = relatedId });
        public void CommitFrom(int offset)
        {
            for (var i = offset; i < Entries.Count; i++) Entries[i].committed = Entries[i].outcome != "Discarded";
        }
    }
}
