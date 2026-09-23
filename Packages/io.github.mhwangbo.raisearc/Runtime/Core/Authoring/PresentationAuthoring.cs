using System;
using System.Collections.Generic;
using System.Linq;

namespace PrincessStudio.Core
{
    [Serializable]
    public sealed class ReferencePage
    {
        public List<ReferenceEntry> entries = new List<ReferenceEntry>();
        public int total, offset;
    }
    [Serializable]
    public sealed class ReferenceEntry { public string id, nameKey, kind; }

    public sealed partial class AuthoringService
    {
        public void ImportLegacyActors(int expectedRevision)
        {
            if (expectedRevision != Revision) throw new InvalidOperationException("Revision conflict. Read a fresh project snapshot.");
            var subjects = new Definition[] { project.character }.Concat(project.npcs).ToList();
            if (subjects.All(subject => project.actors.Exists(a => a.subjectId == subject.id)) && project.stageSlots.Count > 0) return;
            Edit(p =>
            {
                foreach (var subject in new Definition[] { p.character }.Concat(p.npcs))
                {
                    if (p.actors.Exists(a => a.subjectId == subject.id)) continue;
                    var profile = new AppearanceProfile { id = "presentation.profile." + subject.id, nameKey = subject.nameKey, slots = new AppearanceSlots { body = "appearance." + subject.id } };
                    if (subject.id == p.character.id && p.growth.Count > 0) profile.slots.body = p.growth[0].appearanceKey;
                    p.appearanceProfiles.Add(profile);
                    p.actors.Add(new ActorDefinition { id = "presentation.actor." + subject.id, nameKey = subject.nameKey, subjectId = subject.id, profileId = profile.id, startingAge = subject.id == p.character.id ? p.character.startingAge : 18 });
                }
                if (p.stageSlots.Count == 0) p.stageSlots.Add(new StageSlot { id = "presentation.center", nameKey = p.character.nameKey });
            }, expectedRevision);
        }
        public void UpsertActor(ActorDefinition actor, int expectedRevision) => Edit(p => Upsert(p.actors, actor), expectedRevision);
        public void UpsertAppearanceProfile(AppearanceProfile profile, int expectedRevision) => Edit(p => Upsert(p.appearanceProfiles, profile), expectedRevision);
        public void UpsertAppearanceRule(AppearanceRule rule, int expectedRevision) => Edit(p => Upsert(p.appearanceRules, rule), expectedRevision);
        public void UpsertStageSlot(StageSlot slot, int expectedRevision) => Edit(p => Upsert(p.stageSlots, slot), expectedRevision);
        public void SetEventPresentation(string eventId, List<PresentationStep> steps, int expectedRevision) => Edit(p =>
        {
            var e = p.events.Find(x => x.id == eventId) ?? throw new ArgumentException("Unknown event.");
            e.presentation = steps ?? throw new ArgumentNullException(nameof(steps));
        }, expectedRevision);
        public void SetDialogueVoice(string stepId, string locale, string assetGuid, int expectedRevision) => Edit(p =>
        {
            var line = p.events.SelectMany(e => e.presentation).FirstOrDefault(s => s.id == stepId) ?? throw new ArgumentException("Unknown dialogue line.");
            if (line.kind != PresentationStepKind.Dialogue || !string.IsNullOrEmpty(line.sharedStepId)) throw new ArgumentException("Select the original dialogue line.");
            if (!p.locales.Contains(locale)) throw new ArgumentException("Unknown voice locale.");
            if (!string.IsNullOrEmpty(assetGuid) && (assetGuid.Length != 32 || assetGuid.Any(c => !Uri.IsHexDigit(c)))) throw new ArgumentException("Expected a Unity asset GUID, or empty for silence.");
            if (string.IsNullOrEmpty(line.voiceKey)) line.voiceKey = "voice." + Guid.NewGuid().ToString("N");
            else if (p.events.SelectMany(e => e.presentation).Count(s => s.voiceKey == line.voiceKey) > 1)
            {
                var previousKey = line.voiceKey;
                line.voiceKey = "voice." + Guid.NewGuid().ToString("N");
                p.localizedAssets.AddRange(p.localizedAssets.Where(a => a.key == previousKey).Select(a => new LocalizedAssetEntry { key = line.voiceKey, locale = a.locale, assetGuid = a.assetGuid }).ToList());
            }
            p.localizedAssets.RemoveAll(a => a.key == line.voiceKey && a.locale == locale);
            if (!string.IsNullOrEmpty(assetGuid)) p.localizedAssets.Add(new LocalizedAssetEntry { key = line.voiceKey, locale = locale, assetGuid = assetGuid });
        }, expectedRevision);
        private static void Upsert<T>(List<T> entries, T value) where T : Definition
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var index = entries.FindIndex(x => x.id == value.id);
            if (index < 0) entries.Add(value); else entries[index] = value;
        }
        public ReferencePage ListReferences(string kind = "", string query = "", int offset = 0, int limit = 50)
        {
            if (offset < 0 || limit < 1 || limit > 200) throw new ArgumentOutOfRangeException(nameof(limit));
            var all = project.AllDefinitions().Where(x => (string.IsNullOrEmpty(kind) || x.GetType().Name == kind) &&
                (x.id.IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0 || x.nameKey.IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(x => x.id, StringComparer.Ordinal).ToList();
            return new ReferencePage { total = all.Count, offset = offset, entries = all.Skip(offset).Take(limit).Select(x => new ReferenceEntry { id = x.id, nameKey = x.nameKey, kind = x.GetType().Name }).ToList() };
        }
        public ActorPresentationState PreviewPresentation(string actorId, StateData state = null)
        {
            var session = new GameSession(project, extensions);
            if (state != null) session.Restore(state);
            return new AppearanceResolver(project, extensions).Resolve(session.State, actorId, session.State.CaptureStage().actors.Find(x => x.actorId == actorId));
        }
    }
}
