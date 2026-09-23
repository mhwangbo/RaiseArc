using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PrincessStudio.Core
{
    public enum ContentRelation { Reads, Writes, Requires, LeadsTo, Calls, Localizes, Unlocks, EndsWith }
    [Serializable] public sealed class ContentEntry
    {
        public string id, kind, ownerId, path, nameKey, text;
        public List<string> tags = new List<string>();
    }
    [Serializable] public sealed class ContentReference
    {
        public string sourceId, ownerId, targetId, sourcePropertyPath;
        public ContentRelation relationType;
    }
    [Serializable] public sealed class ContentIndexData
    {
        public int revision, total;
        public List<ContentEntry> entries = new List<ContentEntry>();
        public List<ContentReference> references = new List<ContentReference>();
        public List<string> duplicateIds = new List<string>();
    }
    /// <summary>Authoring-only traversal of public DTO fields. No Unity or graph package dependency.</summary>
    public sealed class ContentIndex
    {
        public readonly ContentIndexData Data = new ContentIndexData();
        private readonly ProjectDefinition project;
        private readonly Dictionary<string, object> objects = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<string, ContentEntry> entries = new Dictionary<string, ContentEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ContentReference>> incoming = new Dictionary<string, List<ContentReference>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ContentReference>> outgoing = new Dictionary<string, List<ContentReference>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ContentReference>> descendantReferences = new Dictionary<string, List<ContentReference>>(StringComparer.Ordinal);
        private readonly HashSet<string> referenceKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> searchableText = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> reservedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly ExtensionRegistry extensions;
        public ContentIndex(ProjectDefinition project, string locale = null, bool assignIdentities = false, ExtensionRegistry extensions = null)
        {
            this.extensions = extensions;
            this.project = project;
            if (assignIdentities) Reserve(project);
            Data.revision = project.revision;
            Walk(project, "", "", string.IsNullOrEmpty(locale) ? project.defaultLocale : locale, assignIdentities);
            foreach (var e in project.events)
            {
                Reference(e.id, e.id, EventSequence.Entry(e), ContentRelation.LeadsTo, Path(e.id) + ".entryStepId");
                foreach (var step in e.presentation)
                {
                    if (!string.IsNullOrEmpty(step.sharedStepId)) Reference(step.id, e.id, step.sharedStepId, ContentRelation.Reads, Path(step.id) + ".sharedStepId");
                    if (!string.IsNullOrEmpty(step.sharedEventId)) Reference(step.id, e.id, step.sharedEventId, ContentRelation.Calls, Path(step.id) + ".sharedEventId");
                    if (step.kind == PresentationStepKind.Choice)
                        foreach (var choice in step.choices) Reference(choice.id, step.id, EventSequence.Next(e, step, choice.nextStepId), ContentRelation.LeadsTo, Path(choice.id) + ".nextStepId");
                    else Reference(step.id, e.id, EventSequence.Next(e, step), ContentRelation.LeadsTo, Path(step.id) + ".nextStepId");
                }
            }
            foreach (var r in Data.references)
            {
                if (!incoming.TryGetValue(r.targetId, out var ins)) incoming[r.targetId] = ins = new List<ContentReference>();
                ins.Add(r);
                if (!outgoing.TryGetValue(r.sourceId, out var outs)) outgoing[r.sourceId] = outs = new List<ContentReference>();
                outs.Add(r);
                var owner = r.sourceId;
                for (var depth = 0; !string.IsNullOrEmpty(owner) && depth < 32; depth++, owner = Owner(owner))
                {
                    if (!descendantReferences.TryGetValue(owner, out var nested)) descendantReferences[owner] = nested = new List<ContentReference>();
                    nested.Add(r);
                }
            }
            var builders = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
            foreach (var entry in Data.entries)
            {
                var owner = entry.id;
                for (var depth = 0; !string.IsNullOrEmpty(owner) && depth < 32; depth++, owner = Owner(owner))
                {
                    if (!builders.TryGetValue(owner, out var text)) builders[owner] = text = new StringBuilder();
                    text.Append(entry.id).Append(' ').Append(entry.text).Append(' ').Append(entry.nameKey).Append(' ').Append(string.Join(" ", entry.tags)).Append('\n');
                }
            }
            foreach (var pair in builders) searchableText[pair.Key] = pair.Value.ToString();
            Data.total = Data.entries.Count;
        }
        public object Find(string id) => objects.TryGetValue(id ?? "", out var value) ? value : null;
        public string Path(string id) => entries.TryGetValue(id ?? "", out var value) ? value.path : "";
        public string Owner(string id) => entries.TryGetValue(id ?? "", out var value) ? value.ownerId : "";
        public bool Within(string id, string ancestor)
        {
            for (var n = 0; !string.IsNullOrEmpty(id) && n < 32; n++, id = Owner(id)) if (id == ancestor) return true;
            return false;
        }
        public List<ContentReference> References(string id, bool inbound = true, bool descendants = true)
        {
            var map = inbound ? incoming : descendants ? descendantReferences : outgoing;
            return map.TryGetValue(id ?? "", out var refs) ? new List<ContentReference>(refs) : new List<ContentReference>();
        }
        private void Add(string id, string kind, string owner, string path, string key, string locale, object value, List<string> tags = null)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (entries.ContainsKey(id)) { if (kind != "Localization") Data.duplicateIds.Add(id); return; }
            var entry = new ContentEntry { id = id, kind = kind, ownerId = owner, path = path, nameKey = key ?? "", text = Text(key, locale), tags = tags == null ? new List<string>() : new List<string>(tags) };
            entries.Add(id, entry); objects.Add(id, value); Data.entries.Add(entry);
        }
        public string Text(string key, string locale) => project.translations.Find(t => t.key == key && t.locale == locale)?.text ?? project.translations.Find(t => t.key == key && t.locale == project.fallbackLocale)?.text ?? key ?? "";
        private void Reference(string source, string owner, string target, ContentRelation relation, string path)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) || target == EventSequence.End) return;
            if (!referenceKeys.Add(source + "|" + target + "|" + path + "|" + relation)) return;
            Data.references.Add(new ContentReference { sourceId = source, ownerId = owner, targetId = target, relationType = relation, sourcePropertyPath = path });
        }
        private void Walk(object value, string path, string owner, string locale, bool assign)
        {
            if (value == null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum) return;
            if (value is IList list)
            {
                for (var i = 0; i < list.Count; i++) Walk(list[i], path + "[" + i + "]", owner, locale, assign);
                return;
            }
            var source = owner;
            if (value is Definition d) { source = d.id; Add(source, d.GetType().Name, owner, path, d.nameKey, locale, d, d.tags); }
            if (value is ConditionSpec c)
            {
                if (assign && string.IsNullOrEmpty(c.id)) c.id = NewId(path, "condition");
                source = string.IsNullOrEmpty(c.id) ? LegacyId(path, "condition") : c.id;
                Add(source, "Condition", owner, path, "", locale, c);
                Reference(source, owner, ValueTarget(c.kind, c.target, c.actorId), ContentRelation.Reads, path + ".target");
                if (c.kind == ValueKind.Custom) ExtensionReferences(c.target, c.parameters, source, owner, path, ContentRelation.Reads);
            }
            if (value is EffectSpec f)
            {
                if (assign && string.IsNullOrEmpty(f.id)) f.id = NewId(path, "effect");
                source = string.IsNullOrEmpty(f.id) ? LegacyId(path, "effect") : f.id;
                Add(source, "Effect", owner, path, "", locale, f);
                Reference(source, owner, ValueTarget(f.kind, f.target, f.actorId), ContentRelation.Writes, path + ".target");
                if (f.kind == ValueKind.Custom) ExtensionReferences(f.target, f.parameters, source, owner, path, ContentRelation.Writes);
            }
            if (value is TranslationEntry translation)
                Add("loc:" + translation.key, "Localization", "", path, translation.key, locale, translation);
            foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var child = field.GetValue(value); var childPath = path.Length == 0 ? field.Name : path + "." + field.Name;
                if (child is string text)
                {
                    if (field.Name == "nameKey" || field.Name == "descriptionKey") Reference(source, owner, string.IsNullOrEmpty(text) ? "" : "loc:" + text, ContentRelation.Localizes, childPath);
                    else if (field.Name == "moduleId") Reference(source, owner, text, ContentRelation.Calls, childPath);
                    else if (field.Name == "statId") Reference(source, owner, text, ContentRelation.Reads, childPath);
                    else if (field.Name == "qualificationFlagId") Reference(source, owner, text, ContentRelation.Writes, childPath);
                    else if (field.Name == "activityId") Reference(source, owner, text, ContentRelation.Requires, childPath);
                    else if (field.Name == "actorId" || field.Name == "speakerActorId" || field.Name == "subjectId" || field.Name == "profileId" || field.Name == "slotId") Reference(source, owner, text, ContentRelation.Requires, childPath);
                    else if (field.Name == "falseStepId") Reference(source, owner, text, ContentRelation.LeadsTo, childPath);
                    else if (field.Name == "appearanceKey" || field.Name == "imageKey" || field.Name == "resourceKey" || field.Name == "backgroundKey" || field.Name == "voiceKey" || value is AppearanceSlots)
                        Reference(source, owner, string.IsNullOrEmpty(text) ? "" : "resource:" + text, ContentRelation.Requires, childPath);
                }
                else if (child is List<string> ids && field.Name != "tags" && field.Name != "locales")
                {
                    if (new[] { "activityIds", "connections", "encounterIds", "enemyIds", "allowedStats", "allowedItems", "allowedNpcs", "allowedFlags" }.Contains(field.Name))
                        for (var i = 0; i < ids.Count; i++) Reference(source, owner, ids[i], field.Name.StartsWith("allowed", StringComparison.Ordinal) ? ContentRelation.Writes : ContentRelation.Requires, childPath + "[" + i + "]");
                }
                else Walk(child, childPath, source, locale, assign);
            }
        }
        private static string ValueTarget(ValueKind kind, string target, string actor) => !string.IsNullOrEmpty(target) ? target : !string.IsNullOrEmpty(actor) ? actor : "value:" + kind;
        private void Reserve(object value)
        {
            if (value == null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum) return;
            if (value is IList list) { foreach (var item in list) Reserve(item); return; }
            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var item = field.GetValue(value);
                if (field.Name == "id" && item is string id && id.Length > 0) reservedIds.Add(id);
                else Reserve(item);
            }
        }
        private void ExtensionReferences(string id, IReadOnlyList<RaiseArc.Core.ExtensionValue> values, string source, string owner, string path, ContentRelation relation)
        {
            var definition = extensions?.Definition(id);
            if (definition == null) return;
            foreach (var parameter in definition.parameters)
                if (RaiseArc.Core.ExtensionDefinition.IsReference(parameter.kind))
                    Reference(source, owner, definition.Read(values, parameter.name), relation, path + ".parameters." + parameter.name);
        }
        private string NewId(string path, string prefix)
        {
            var id = LegacyId(path, prefix); var suffix = 0;
            while (!reservedIds.Add(id)) id = LegacyId(path + "#" + ++suffix, prefix);
            return id;
        }
        private static string LegacyId(string path, string prefix)
        {
            using (var hash = SHA256.Create()) return prefix + "-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-", "").Substring(0, 24).ToLowerInvariant();
        }
        public List<ContentEntry> Search(string query, string locale = null)
        {
            var tokens = (query ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var problems = ProjectValidator.Validate(project, extensions);
            bool Match(ContentEntry entry, string token)
            {
                var split = token.IndexOf(':'); var key = split < 0 ? "" : token.Substring(0, split).ToLowerInvariant(); var term = split < 0 ? token : token.Substring(split + 1);
                bool Has(string text) => (text ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                var refs = References(entry.id, false);
                switch (key)
                {
                    case "type": return Has(entry.kind.Replace("Definition", ""));
                    case "uses": return refs.Any(r => r.targetId == term);
                    case "writes": return refs.Any(r => r.targetId == term && r.relationType == ContentRelation.Writes);
                    case "character": return refs.Any(r => Has(r.targetId) && (Find(r.targetId) is ActorDefinition || Find(r.targetId) is CharacterDefinition || Find(r.targetId) is NpcDefinition));
                    case "module": return refs.Any(r => r.relationType == ContentRelation.Calls && Has(r.targetId));
                    case "missing": return refs.Where(r => r.relationType == ContentRelation.Localizes).Any(r => !project.translations.Exists(t => t.key == r.targetId.Substring(4) && t.locale == term && !string.IsNullOrWhiteSpace(t.text)));
                    case "has": return term == "error" && problems.issues.Any(i => i.severity == IssueSeverity.Error && Within(i.target, entry.id));
                    case "tag": return entry.tags.Any(Has);
                    case "age":
                        var range = term.Split(new[] { ".." }, StringSplitOptions.None);
                        return range.Length == 2 && int.TryParse(range[0], out var lo) && int.TryParse(range[1], out var hi) && Data.entries.Any(c => Within(c.id, entry.id) && Find(c.id) is ConditionSpec age && age.kind == ValueKind.Age && age.value >= lo && age.value <= hi);
                    default: return searchableText.TryGetValue(entry.id, out var searchable) && Has(searchable);
                }
            }
            return Data.entries.Where(e => tokens.All(t => Match(e, t))).OrderBy(e => e.kind).ThenBy(e => e.id, StringComparer.Ordinal).ToList();
        }
    }
}
