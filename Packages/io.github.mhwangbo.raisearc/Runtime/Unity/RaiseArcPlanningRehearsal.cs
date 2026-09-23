using System;
using System.Collections.Generic;
using PrincessStudio.Core;
using UnityEngine;

namespace RaiseArc.Unity
{
    // This preview stores placement intentions only, not an executable schedule or occupied game time.
    public sealed class PlanningRehearsal : ISaveParticipant
    {
        [Serializable] private sealed class Draft { public string[] slots = new string[21]; }
        private readonly HashSet<string> activities;
        private Draft draft = new Draft();
        public string Id => "raisearc-planning-rehearsal";
        public int Version => 1;
        public PlanningRehearsal(IEnumerable<string> activityIds) { activities = new HashSet<string>(activityIds, StringComparer.Ordinal); }
        public string this[int slot] => draft.slots[slot] ?? "";
        public void Place(int slot, string activityId)
        {
            if (slot < 0 || slot >= 21) throw new ArgumentOutOfRangeException(nameof(slot));
            if (!string.IsNullOrEmpty(activityId) && !activities.Contains(activityId)) throw new ArgumentException("Activity no longer exists.");
            draft.slots[slot] = activityId ?? "";
        }
        public void CopyDay(int from, int to)
        {
            if (from < 0 || from > 6 || to < 0 || to > 6) throw new ArgumentOutOfRangeException(nameof(to));
            Array.Copy(draft.slots, from * 3, draft.slots, to * 3, 3);
        }
        public string Capture() => JsonUtility.ToJson(draft);
        public void Validate(int version, string payload) => Parse(version, payload);
        public void Restore(int version, string payload) { draft = Parse(version, payload); }
        private Draft Parse(int version, string payload)
        {
            if (version != Version) throw new ArgumentException("Unsupported planning rehearsal version.");
            var next = JsonUtility.FromJson<Draft>(payload);
            if (next?.slots == null || next.slots.Length != 21) throw new ArgumentException("A rehearsal requires 21 slots.");
            foreach (var id in next.slots) if (!string.IsNullOrEmpty(id) && !activities.Contains(id)) throw new ArgumentException("Draft contains a deleted activity: " + id);
            return next;
        }
    }
}
