using System;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Authoring
{
    public static class ActivityCopy
    {
        public static string Duplicate(AuthoringService service, string sourceId, int expectedRevision)
        {
            var source = service.Snapshot().activities.Find(a => a.id == sourceId) ?? throw new ArgumentException("Activity not found.");
            var newId = "activity-" + Guid.NewGuid().ToString("N");
            service.Edit(p =>
            {
                void CopyText(Definition d, string id)
                {
                    var oldName = d.nameKey; var oldDescription = d.descriptionKey;
                    d.id = id; d.nameKey = "activity." + id + ".name"; d.descriptionKey = "activity." + id + ".description";
                    foreach (var text in p.translations.Where(t => !string.IsNullOrEmpty(t.key) && (t.key == oldName || t.key == oldDescription)).ToArray())
                        p.translations.Add(new TranslationEntry { key = text.key == oldName ? d.nameKey : d.descriptionKey, locale = text.locale,
                            text = text.text + (text.key == oldName && id == newId ? (text.locale == "ko" ? " 복사본" : " copy") : "") });
                }
                void Ids(System.Collections.Generic.List<ConditionSpec> conditions, System.Collections.Generic.List<EffectSpec> effects)
                {
                    foreach (var c in conditions) c.id = "condition-" + Guid.NewGuid().ToString("N");
                    foreach (var e in effects) e.id = "effect-" + Guid.NewGuid().ToString("N");
                }
                CopyText(source, newId); Ids(source.conditions, source.effects);
                foreach (var e in source.failureEffects) e.id = "effect-" + Guid.NewGuid().ToString("N");
                foreach (var level in source.progression.levels) { CopyText(level, "level-" + Guid.NewGuid().ToString("N")); Ids(level.conditions, level.effects); }
                p.activities.Add(source);
            }, expectedRevision);
            return newId;
        }
    }
}
