using System;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Analysis
{
    public sealed partial class SimulationExplorer
    {
        private void SampleRuleIntervals(SimulationRecord record)
        {
            if (record.parent < 0) return;
            var previous = checkpoint.records[record.parent].state;
            foreach (var flag in project.flags.Where(f => f.permanent))
            {
                if (checkpoint.qualificationDays.Any(x => x.id == flag.id)) continue;
                var observation = record.observations.FirstOrDefault(o => o.committed && o.kind == "ValueChange" && o.target == "Flag::" + flag.id && o.before == 0 && o.after == 1);
                if (observation != null) checkpoint.qualificationDays.Add(new IntEntry { id = flag.id, value = observation.day });
            }
            foreach (var modifier in project.modifiers)
            {
                var from = previous.day; var expiry = previous.modifierExpiry.Find(x => x.id == modifier.id)?.value ?? from; var activeDays = 0;
                foreach (var o in record.observations.Where(o => o.committed && o.kind == "ValueChange" && o.target == "ModifierDays::" + modifier.id))
                {
                    activeDays += Math.Max(0, Math.Min(expiry, o.day) - from); from = o.day; expiry = o.day + o.after;
                }
                activeDays += Math.Max(0, Math.Min(expiry, record.state.day) - from);
                if (activeDays == 0) continue;
                var value = checkpoint.modifierActiveDays.Find(x => x.id == modifier.id);
                if (value == null) checkpoint.modifierActiveDays.Add(new IntEntry { id = modifier.id, value = activeDays }); else value.value += activeDays;
            }
        }
        private void AggregateBalance(SimulationRecord record, RuntimeObservation o)
        {
            if (Settings.mode != ExplorationMode.MonteCarlo) return;
            if (o.kind == "ActivityBlocked")
            {
                if (o.outcome == "InsufficientMoney" && checkpoint.firstMoneyBlockedDay < 0) checkpoint.firstMoneyBlockedDay = o.day;
            }
            else if (o.kind != "Condition" && !o.committed) return;
            if (o.kind != "ValueChange" && o.kind != "Economy" && o.kind != "ActivityBlocked" && o.kind != "ActivityResult" &&
                o.kind != "Evaluation" && o.kind != "ActivityModifier" && o.kind != "Activity" && o.kind != "Choice" && o.kind != "Condition") return;
            var key = SimulationIdentity.Pack(new[] { o.kind, o.id, o.target ?? "", o.outcome ?? "" });
            if (!ledger.TryGetValue(key, out var row))
            {
                var owner = o.id;
                while (!string.IsNullOrEmpty(owner) && !(index.Find(owner) is ActivityDefinition) && !(index.Find(owner) is EventDefinition)) owner = index.Owner(owner);
                if (string.IsNullOrEmpty(owner)) owner = project.activities.FirstOrDefault(a => o.id == a.id + ".qualification")?.id;
                row = new BalanceLedgerRow { key = key, kind = o.kind, sourceId = o.id, ownerId = owner ?? "", target = o.target ?? "", outcome = o.outcome ?? "",
                    nameKey = (index.Find(owner ?? "") as Definition)?.nameKey ?? "", firstDay = o.day, firstRun = record.run,
                    firstRecord = record.recordId, firstBefore = o.before, firstAfter = o.after };
                row.nameEn = project.translations.Find(t => t.key == row.nameKey && t.locale == "en")?.text ?? row.nameKey;
                row.nameKo = project.translations.Find(t => t.key == row.nameKey && t.locale == "ko")?.text ?? row.nameKey;
                Report.ledger.Add(row); ledger.Add(key, row); Report.retainedBytes += 512 + key.Length * 2;
            }
            row.count++; row.lastDay = o.day;
            if (o.hasDelta) { row.requested += o.requestedDelta; row.applied += o.appliedDelta; }
            if (o.kind == "Economy") { row.moneyIn += o.income; row.moneyOut += o.cost; }
            if (o.kind == "ValueChange" && o.target.StartsWith("Money:", StringComparison.Ordinal))
            { row.moneyIn += Math.Max(0, o.appliedDelta); row.moneyOut += Math.Max(0, -o.appliedDelta); }
            if (o.hasActual) row.actualTotal += o.actual;
        }
    }
}
