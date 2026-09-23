using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Core
{
    [Serializable] public sealed class PlanEntry
    {
        public int slot;
        public string activityId = "";
    }
    [Serializable] public sealed class GamePlan
    {
        public int version = 1, startTick, capacity;
        public bool confirmed;
        public List<PlanEntry> entries = new List<PlanEntry>();
        public GamePlan Copy() => new GamePlan { version = version, startTick = startTick, capacity = capacity, confirmed = confirmed,
            entries = entries.Select(e => new PlanEntry { slot = e.slot, activityId = e.activityId }).ToList() };
        public static GamePlan New(ProjectDefinition p, StateSnapshot state) => new GamePlan {
            startTick = p.time.Tick(state), capacity = Math.Min(p.time.Capacity, p.durationDays * p.time.PeriodsPerDay - p.time.Tick(state)) };
        public int OwnerSlot(ProjectDefinition project, int slot)
        {
            var entry = entries.Find(e => e.slot <= slot && e.slot + project.time.Duration(project.activities.Find(a => a.id == e.activityId)) > slot);
            return entry?.slot ?? -1;
        }
        public GamePlan Place(ProjectDefinition p, int slot, string id)
        {
            if (confirmed) throw new InvalidOperationException("Confirmed plans are locked. / 확정한 계획은 잠겨 있습니다.");
            if (slot < 0 || slot >= capacity) throw new ArgumentOutOfRangeException(nameof(slot));
            var copy = Copy(); var owner = OwnerSlot(p, slot);
            if (owner >= 0) copy.entries.RemoveAll(e => e.slot == owner);
            if (!string.IsNullOrEmpty(id)) copy.entries.Add(new PlanEntry { slot = slot, activityId = id });
            copy.entries.Sort((a, b) => a.slot.CompareTo(b.slot)); copy.Validate(p, false); return copy;
        }
        public void Validate(ProjectDefinition p, bool requireFull)
        {
            if (version != 1 || entries == null || startTick < 0 || capacity < 0 || capacity > p.time.Capacity || startTick + capacity > p.durationDays * p.time.PeriodsPerDay)
                throw new ArgumentException("Invalid plan range or version.");
            var occupied = new bool[capacity];
            foreach (var entry in entries)
            {
                var activity = p.activities.Find(a => a.id == entry.activityId) ?? throw new ArgumentException("Plan activity was deleted: " + entry.activityId);
                var duration = p.time.Duration(activity);
                if (entry.slot < 0 || entry.slot + duration > capacity) throw new ArgumentException("The activity does not fit in the planning range. / 활동이 계획 범위를 넘습니다.");
                for (var n = entry.slot; n < entry.slot + duration; n++)
                {
                    if (occupied[n]) throw new ArgumentException("Activities overlap. / 다른 활동과 시간이 겹칩니다.");
                    occupied[n] = true;
                }
            }
            if ((requireFull || confirmed) && occupied.Any(v => !v)) throw new ArgumentException("Fill every slot before confirming. Empty slots are never replaced automatically. / 모든 칸을 채운 뒤 확정하세요. 빈칸은 자동 대체하지 않습니다.");
        }
    }
}
