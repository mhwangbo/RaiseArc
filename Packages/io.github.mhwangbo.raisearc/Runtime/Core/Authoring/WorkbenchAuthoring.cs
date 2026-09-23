using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PrincessStudio.Core
{
    [Serializable] public sealed class GraphEdit
    {
        public string operation = "", eventId = "", targetId = "", portId = "next", nextId = "", locale = "", text = "", impactToken = "";
        public PresentationStep node;
        public ChoiceDefinition choice;
        public EventDefinition eventDefinition;
        public ProjectDefinition replacement;
        internal GraphEdit Copy() => (GraphEdit)MemberwiseClone();
    }
    [Serializable] public sealed class AuthoringChangeSet
    {
        public int baseRevision;
        public List<GraphEdit> edits = new List<GraphEdit>();
    }
    [Serializable] public sealed class ChangeSetPreview
    {
        public string token;
        public int baseRevision;
        public List<string> added = new List<string>(), changed = new List<string>(), removed = new List<string>();
        public ValidationReport validation;
        public ProjectDefinition project;
    }
    [Serializable] public sealed class DeletionImpact
    {
        public string targetId, token;
        public int revision;
        public List<ContentReference> references = new List<ContentReference>();
    }
    [Serializable] public sealed class LocalizationStatus
    {
        public string sourceId, key, locale, text;
        public bool missing, fallback;
        public bool draft, needsReview;
    }
    public sealed partial class AuthoringService
    {
        public ContentIndexData GetContentIndex(string locale = null) => new ContentIndex(project, locale, extensions: extensions).Data;
        public ContentIndexData SearchContent(string query, string locale = null, int offset = 0, int limit = 100)
        {
            if (offset < 0 || limit < 1 || limit > 500) throw new ArgumentOutOfRangeException(nameof(limit));
            var index = new ContentIndex(project, locale, extensions: extensions); var matches = index.Search(query, locale);
            return new ContentIndexData { revision = Revision, total = matches.Count, entries = matches.Skip(offset).Take(limit).ToList() };
        }
        public List<ContentReference> GetReferences(string target, bool inbound = true) => new ContentIndex(project, extensions: extensions).References(target, inbound);
        public GraphProjection GetGraphProjection(string eventId, string locale = null) => GraphProjection.Build(project, eventId, locale);
        public ValidationReport GetProblems() => ValidateProject();
        public List<LocalizationStatus> GetLocalizationStatus(string targetId = "")
        {
            var index = new ContentIndex(project, extensions: extensions);
            var refs = string.IsNullOrEmpty(targetId) ? index.Data.references : index.References(targetId, false);
            var result = new List<LocalizationStatus>();
            foreach (var reference in refs.Where(r => r.relationType == ContentRelation.Localizes))
                foreach (var locale in project.locales)
                {
                    var key = reference.targetId.Substring(4);
                    var entry = project.translations.Find(t => t.key == key && t.locale == locale);
                    var text = entry?.text;
                    var fallback = project.translations.Find(t => t.key == key && t.locale == project.fallbackLocale)?.text;
                    result.Add(new LocalizationStatus { sourceId = reference.sourceId, key = key, locale = locale, text = text ?? "", missing = string.IsNullOrWhiteSpace(text), fallback = string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(fallback), draft = entry?.draft ?? false, needsReview = entry != null && !entry.reviewed });
                }
            return result;
        }
        public AuthoringChangeSet BeginChangeSet() => new AuthoringChangeSet { baseRevision = Revision };
        public void RollbackChangeSet(AuthoringChangeSet changes) { changes.edits.Clear(); }
        public ChangeSetPreview PreviewChangeSet(AuthoringChangeSet changes)
        {
            if (changes == null || changes.baseRevision != Revision) throw new InvalidOperationException("Revision conflict. Refresh and review the changes again.");
            if (changes.edits == null || changes.edits.Count > 256) throw new ArgumentException("Change set budget: 256 operations.");
            var next = codec.Clone(project);
            var terminalAliases = new Dictionary<string, string>();
            foreach (var edit in changes.edits) ApplyGraphEdit(ref next, NormalizeTerminalEdit(next, edit, terminalAliases));
            new ContentIndex(next, assignIdentities: true);
            next.revision = checked(Revision + 1);
            var before = new ContentIndex(project, extensions: extensions); var after = new ContentIndex(next, extensions: extensions);
            var preview = new ChangeSetPreview { baseRevision = Revision, project = next, validation = ProjectValidator.Validate(next, extensions) };
            var oldIds = new HashSet<string>(before.Data.entries.Select(x => x.id)); var newIds = new HashSet<string>(after.Data.entries.Select(x => x.id));
            preview.added.AddRange(newIds.Except(oldIds)); preview.removed.AddRange(oldIds.Except(newIds));
            // Property-level equality remains in the codec; list affected owners for a readable review.
            foreach (var edit in changes.edits) if (!string.IsNullOrEmpty(edit.targetId)) preview.changed.Add(edit.targetId); else if (!string.IsNullOrEmpty(edit.eventId)) preview.changed.Add(edit.eventId); else preview.changed.Add("project");
            preview.changed = preview.changed.Distinct().ToList();
            preview.token = Fingerprint(next);
            return preview;
        }
        public void CommitChangeSet(AuthoringChangeSet changes, string previewToken, int expectedRevision)
        {
            if (expectedRevision != Revision) throw new InvalidOperationException("Revision conflict.");
            var preview = PreviewChangeSet(changes);
            if (previewToken != preview.token) throw new InvalidOperationException("Changes differ from the reviewed preview. Preview again.");
            if (preview.validation.HasErrors) throw new ArgumentException(preview.validation.Summary);
            Replace(preview.project, expectedRevision);
        }
        public DeletionImpact GetDeletionImpact(string targetId) => Impact(project, targetId);
        private static DeletionImpact Impact(ProjectDefinition p, string targetId)
        {
            var index = new ContentIndex(p); if (index.Find(targetId) == null) throw new ArgumentException("Unknown content.");
            var refs = index.References(targetId).Where(r => !index.Within(r.sourceId, targetId)).ToList();
            return new DeletionImpact { targetId = targetId, revision = p.revision, references = refs, token = Hash(targetId + "|" + string.Join("|", refs.Select(r => r.sourcePropertyPath + ":" + r.sourceId + ":" + r.targetId))) };
        }
        private static string Hash(string text) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        public static string Fingerprint(object value)
        {
            var text = new StringBuilder();
            void Write(object item)
            {
                if (item == null) { text.Append("null;"); return; }
                if (item is string str) { text.Append(str.Length).Append(':').Append(str); return; }
                if (item.GetType().IsPrimitive || item.GetType().IsEnum || item is decimal) { text.Append(Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture)).Append(';'); return; }
                if (item is IList list) { text.Append('['); foreach (var child in list) Write(child); text.Append(']'); return; }
                text.Append('{');
                foreach (var field in item.GetType().GetFields().OrderBy(f => f.Name, StringComparer.Ordinal))
                    if (!field.IsStatic) { Write(field.Name); Write(field.GetValue(item)); }
                text.Append('}');
            }
            Write(value); return Hash(text.ToString());
        }
        private void ApplyGraphEdit(ref ProjectDefinition p, GraphEdit edit)
        {
            if (edit == null) throw new ArgumentException("Null edit.");
            if (edit.operation == "ReplaceProject") { p = codec.Clone(edit.replacement ?? throw new ArgumentException("Replacement required.")); return; }
            if (edit.operation == "CreateEvent") { if (edit.eventDefinition == null) throw new ArgumentException("Event required."); var ev = codec.Clone(new ProjectDefinition { events = new List<EventDefinition> { edit.eventDefinition } }).events[0]; GenerateKeys(ev, "event"); p.events.Add(ev); return; }
            if (edit.operation == "RenameContent" || edit.operation == "AddLocalization")
            {
                var d = p.AllDefinitions().FirstOrDefault(x => x.id == edit.targetId);
                var key = edit.operation == "RenameContent" ? d?.nameKey ?? throw new ArgumentException("Unknown content.") : edit.targetId;
                var locale = string.IsNullOrEmpty(edit.locale) ? p.defaultLocale : edit.locale;
                p.translations.RemoveAll(t => t.key == key && t.locale == locale); p.translations.Add(new TranslationEntry { key = key, locale = locale, text = edit.text }); return;
            }
            if (edit.operation == "DeleteWithImpactCheck")
            {
                var impact = Impact(p, edit.targetId);
                if (impact.references.Count > 0 && impact.token != edit.impactToken) throw new InvalidOperationException("Review deletion impact before deleting referenced content.");
                foreach (var ev in p.events)
                {
                    FreezeLinks(ev);
                    if (ev.presentation.Exists(s => s.id == edit.targetId))
                    {
                        if (EventSequence.Entry(ev) == edit.targetId) ev.entryStepId = "$disconnected";
                        foreach (var s in ev.presentation) { if (s.nextStepId == edit.targetId) s.nextStepId = EventSequence.End; if (s.falseStepId == edit.targetId) s.falseStepId = EventSequence.End; foreach (var c in s.choices) if (c.nextStepId == edit.targetId) c.nextStepId = EventSequence.End; }
                    }
                }
                if (!RemoveObject(p, new ContentIndex(p).Find(edit.targetId))) throw new ArgumentException("This root object cannot be deleted.");
                return;
            }
            var e = p.events.Find(x => x.id == edit.eventId) ?? throw new ArgumentException("Unknown event.");
            FreezeLinks(e);
            var graph = GraphProjection.Build(p, e.id);
            var projected = graph.nodes.Find(x => x.id == edit.targetId);
            var stepId = projected != null && projected.stepIds.Count > 0 ? projected.stepIds[projected.stepIds.Count - 1] : edit.targetId;
            var step = e.presentation.Find(x => x.id == stepId);
            var graphProject = p;
            void Connect(string target)
            {
                if (target == e.id + ":terminal") target = EventSequence.TerminalChoices;
                if (edit.targetId == e.id)
                {
                    e.entryStepId = target; return;
                }
                if (step == null) throw new ArgumentException("Select an executable node.");
                if (edit.portId == "false") step.falseStepId = target;
                else if (edit.portId == "next") step.nextStepId = target;
                else
                {
                    var choice = step.choices.Find(x => x.id == edit.portId);
                    if (choice == null && !string.IsNullOrEmpty(step.sharedStepId))
                    {
                        var shared = RaiseArc.Core.RaiseArcFlowReuse.Resolve(graphProject, step).choices.Find(c => c.id == edit.portId);
                        if (shared != null) { choice = new ChoiceDefinition { id = edit.portId, nameKey = shared.nameKey }; step.choices.Add(choice); }
                    }
                    (choice ?? throw new ArgumentException("Unknown output port.")).nextStepId = target;
                }
            }
            switch (edit.operation)
            {
                case "ConnectNodes": Connect(edit.nextId); break;
                case "DisconnectNodes": Connect("$disconnected"); break;
                case "CreateNode":
                case "InsertNodeBetween":
                    if (edit.node == null) throw new ArgumentException("Node required.");
                    var node = codec.Clone(new ProjectDefinition { events = new List<EventDefinition> { new EventDefinition { presentation = new List<PresentationStep> { edit.node } } } }).events[0].presentation[0];
                    if (string.IsNullOrEmpty(node.id)) throw new ArgumentException("Stable node ID required.");
                    GenerateKeys(node, "step");
                    e.presentation.Add(node);
                    if (edit.operation == "InsertNodeBetween") { node.nextStepId = edit.nextId; Connect(node.id); }
                    break;
                case "CreateChoice":
                case "CreateBranch":
                    if (step == null) throw new ArgumentException("Choice node required.");
                    step.kind = PresentationStepKind.Choice;
                    if (edit.choice == null) throw new ArgumentException("Choice required.");
                    var choice = codec.Clone(new ProjectDefinition { events = new List<EventDefinition> { new EventDefinition { choices = new List<ChoiceDefinition> { edit.choice } } } }).events[0].choices[0]; GenerateKeys(choice, "choice"); step.choices.Add(choice); break;
                case "DuplicateFlow":
                    var copy = codec.Clone(p).events.Find(x => x.id == e.id); var suffix = edit.nextId;
                    if (string.IsNullOrEmpty(suffix)) throw new ArgumentException("New ID suffix required.");
                    var map = new Dictionary<string, string>();
                    foreach (var d in new[] { (Definition)copy }.Concat(copy.presentation).Concat(copy.choices).Concat(copy.presentation.SelectMany(s => s.choices))) { map[d.id] = d.id + "-" + suffix; d.id = map[d.id]; }
                    foreach (var s in copy.presentation) { if (map.TryGetValue(s.nextStepId, out var next)) s.nextStepId = next; if (map.TryGetValue(s.falseStepId, out var other)) s.falseStepId = other; foreach (var c in s.choices) { if (map.TryGetValue(c.nextStepId, out var nextChoice)) c.nextStepId = nextChoice; foreach (var condition in c.conditions) condition.id = ""; foreach (var effect in c.effects) effect.id = ""; } foreach (var condition in s.conditions) condition.id = ""; foreach (var effect in s.effects) effect.id = ""; }
                    if (map.TryGetValue(copy.entryStepId ?? "", out var entry)) copy.entryStepId = entry;
                    foreach (var c in copy.conditions) c.id = ""; foreach (var f in copy.effects) f.id = "";
                    foreach (var c in copy.choices) { foreach (var condition in c.conditions) condition.id = ""; foreach (var effect in c.effects) effect.id = ""; }
                    p.events.Add(copy); break;
                default: throw new ArgumentException("Unknown graph edit: " + edit.operation);
            }
        }
        private static GraphEdit NormalizeTerminalEdit(ProjectDefinition p, GraphEdit original, Dictionary<string, string> aliases)
        {
            if (original.operation != "ConnectNodes" && original.operation != "DisconnectNodes" && original.operation != "InsertNodeBetween") return original;
            var e = p.events.Find(item => item.id == original.eventId);
            if (e == null) return original;
            var terminal = e.id + ":terminal";
            if (original.targetId == terminal && e.choices.Count > 0)
            {
                FreezeLinks(e);
                var id = "choices-" + Hash(e.id).Substring(0, 16);
                var index = new ContentIndex(p); var suffix = 0; var candidate = id;
                while (index.Find(candidate) != null) candidate = id + "-" + ++suffix;
                id = candidate;
                if (EventSequence.Entry(e).Length == 0) e.entryStepId = id;
                foreach (var step in e.presentation)
                {
                    if (step.nextStepId == EventSequence.TerminalChoices) step.nextStepId = id;
                    if (step.falseStepId == EventSequence.TerminalChoices) step.falseStepId = id;
                    foreach (var choice in step.choices) if (choice.nextStepId == EventSequence.TerminalChoices) choice.nextStepId = id;
                }
                foreach (var choice in e.choices) if (string.IsNullOrEmpty(choice.nextStepId)) choice.nextStepId = EventSequence.End;
                e.presentation.Add(new PresentationStep { id = id, nameKey = e.nameKey, kind = PresentationStepKind.Choice,
                    choices = e.choices, nextStepId = EventSequence.End,
                    backgroundChange = StageChange.Keep, actorsChange = StageChange.Keep, imagesChange = StageChange.Keep });
                e.choices = new List<ChoiceDefinition>();
                aliases[terminal] = id;
            }
            var edit = original.Copy();
            if (aliases.TryGetValue(edit.targetId, out var source)) edit.targetId = source;
            if (aliases.TryGetValue(edit.nextId, out var target)) edit.nextId = target;
            if (edit.nextId == EventSequence.TerminalChoices && aliases.TryGetValue(terminal, out var choices)) edit.nextId = choices;
            return edit;
        }
        private static void FreezeLinks(EventDefinition e)
        {
            for (var i = 0; i < e.presentation.Count; i++)
            {
                var s = e.presentation[i];
                if (string.IsNullOrEmpty(s.nextStepId)) s.nextStepId = i + 1 < e.presentation.Count ? e.presentation[i + 1].id : e.choices.Count > 0 ? EventSequence.TerminalChoices : EventSequence.End;
            }
        }
        private static bool RemoveObject(object container, object target)
        {
            if (container == null || target == null || container is string || container.GetType().IsPrimitive || container.GetType().IsEnum) return false;
            if (container is IList list)
            {
                if (list.Contains(target)) { list.Remove(target); return true; }
                foreach (var child in list) if (RemoveObject(child, target)) return true;
            }
            else foreach (var field in container.GetType().GetFields()) if (RemoveObject(field.GetValue(container), target)) return true;
            return false;
        }
        public ExecutionTrace SimulateFromNode(string eventId, string nodeId, StateData preset = null, List<string> choices = null, int maxSteps = 512)
        {
            if (maxSteps < 1 || maxSteps > 2048) throw new ArgumentOutOfRangeException(nameof(maxSteps));
            var result = new ExecutionTrace(); var session = new GameSession(codec.Clone(project), extensions) { TraceEnabled = true };
            try
            {
                if (preset != null) session.Restore(preset);
                var e = project.events.Find(x => x.id == eventId) ?? throw new ArgumentException("Unknown event.");
                result.entryConditions = session.Explain(e.conditions, session.State);
                if (!session.StartPreview(eventId, nodeId)) { result.status = "Entry conditions failed"; result.finalState = session.Capture(); return result; }
                var choiceIndex = 0;
                for (var i = 0; i < maxSteps; i++)
                {
                    if (session.State.PendingEventId.Length == 0) { result.status = "Completed"; break; }
                    var available = session.AvailableChoices();
                    if (available.Count > 0)
                    {
                        if (choices == null || choiceIndex >= choices.Count) { result.status = "Waiting for choice: " + string.Join(", ", available); break; }
                        session.Choose(choices[choiceIndex++]);
                    }
                    else if (session.State.PresentationStepId.Length > 0) session.AdvancePresentation(session.State.PresentationStepId);
                    else { result.status = "Blocked: no available choice"; break; }
                }
                if (result.status.Length == 0) result.status = session.State.PendingEventId.Length == 0 ? "Completed" : "Step budget reached";
            }
            catch (Exception ex) { result.status = "Failed"; result.error = ex.Message; }
            result.frames = session.TakeTrace(); result.finalState = session.Capture(); return result;
        }
    }
}
