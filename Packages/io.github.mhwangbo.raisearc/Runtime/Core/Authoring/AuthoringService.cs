using System;
using System.Collections.Generic;

namespace PrincessStudio.Core
{
    public interface IProjectCodec
    {
        ProjectDefinition Clone(ProjectDefinition project);
    }
    /// <summary>Shared human/LLM command boundary, with optimistic concurrency and detached snapshots.</summary>
    public sealed partial class AuthoringService
    {
        private ProjectDefinition project;
        private readonly IProjectCodec codec;
        private readonly ExtensionRegistry extensions;
        public AuthoringService(ProjectDefinition initial, IProjectCodec codec, ExtensionRegistry extensions = null)
        {
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.extensions = extensions;
            project = codec.Clone(initial ?? throw new ArgumentNullException(nameof(initial)));
            new ContentIndex(project, assignIdentities: true);
        }
        public int Revision => project.revision;
        public List<RaiseArc.Core.ExtensionDefinition> GetExtensions()
        {
            var result = new List<RaiseArc.Core.ExtensionDefinition>();
            if (extensions != null) foreach (var d in extensions.Definitions)
            {
                var copy = new RaiseArc.Core.ExtensionDefinition { id = d.id, displayName = d.displayName, condition = d.condition, effect = d.effect, analysis = d.analysis };
                foreach (var p in d.parameters) copy.parameters.Add(new RaiseArc.Core.ExtensionParameter { name = p.name, displayName = p.displayName, kind = p.kind, defaultValue = p.defaultValue, required = p.required });
                result.Add(copy);
            }
            return result;
        }
        public ProjectDefinition Snapshot() => codec.Clone(project);
        public ValidationReport ValidateProject() => ProjectValidator.Validate(project, extensions);
        public void Edit(Action<ProjectDefinition> edit, int expectedRevision = -1)
        {
            if (expectedRevision >= 0 && expectedRevision != project.revision)
                throw new InvalidOperationException("Revision conflict. Read a fresh project snapshot.");
            var next = codec.Clone(project);
            edit(next);
            new ContentIndex(next, assignIdentities: true);
            next.revision = checked(project.revision + 1);
            var validation = ProjectValidator.Validate(next, extensions);
            if (validation.HasErrors)
                throw new ArgumentException(validation.Summary);
            project = codec.Clone(next);
        }
        public void CreateActivity(ActivityDefinition activity, int expectedRevision = -1) => Edit(p => { GenerateKeys(activity, "activity"); p.activities.Add(activity); }, expectedRevision);
        public void CreateEvent(EventDefinition ev, int expectedRevision = -1) => Edit(p => { GenerateKeys(ev, "event"); p.events.Add(ev); }, expectedRevision);
        public void CreateEnding(EndingDefinition ending, int expectedRevision = -1) => Edit(p => { GenerateKeys(ending, "ending"); p.endings.Add(ending); }, expectedRevision);
        public void AddCondition(string owner, ConditionSpec condition, int expectedRevision = -1) => Edit(p => Conditions(p, owner).Add(condition), expectedRevision);
        public void AddEffect(string owner, EffectSpec effect, int expectedRevision = -1) => Edit(p => Effects(p, owner).Add(effect), expectedRevision);
        public void AttachGameMode(string activityId, string moduleId, int expectedRevision = -1) => Edit(p =>
        {
            var a = p.activities.Find(x => x.id == activityId) ?? throw new ArgumentException("Unknown activity.");
            a.moduleId = moduleId;
            a.category = ActivityKind.GameMode;
        }, expectedRevision);
        public void AddLocalization(TranslationEntry entry, int expectedRevision = -1) => Edit(p =>
        {
            p.translations.RemoveAll(t => t.key == entry.key && t.locale == entry.locale);
            p.translations.Add(entry);
        }, expectedRevision);
        public SimulationReport SimulatePlaythrough(int seed = 1, int runs = 32, int maxSteps = 10000) => PlaythroughSimulator.Run(Snapshot(), codec, seed, runs, maxSteps, extensions);
        public void Replace(ProjectDefinition replacement, int expectedRevision = -1)
        {
            if (expectedRevision >= 0 && expectedRevision != Revision)
                throw new InvalidOperationException("Revision conflict.");
            var next = codec.Clone(replacement);
            new ContentIndex(next, assignIdentities: true);
            next.revision = checked(Revision + 1);
            var report = ProjectValidator.Validate(next, extensions);
            if (report.HasErrors)
                throw new ArgumentException(report.Summary);
            project = next;
        }
        public static void GenerateKeys(Definition d, string category)
        {
            if (string.IsNullOrEmpty(d.nameKey))
                d.nameKey = category + "." + d.id + ".name";
        }
        public static List<ConditionSpec> Conditions(ProjectDefinition p, string id)
        {
            foreach (var a in p.activities)
                if (a.id == id)
                    return a.conditions;
            foreach (var e in p.events)
            {
                if (e.id == id)
                    return e.conditions;
                foreach (var c in e.choices)
                    if (c.id == id)
                        return c.conditions;
                foreach (var step in e.presentation)
                {
                    if (step.id == id) return step.conditions;
                    foreach (var c in step.choices)
                        if (c.id == id) return c.conditions;
                }
            }
            foreach (var e in p.endings)
                if (e.id == id)
                    return e.conditions;
            foreach (var g in p.growth)
                if (g.id == id)
                    return g.conditions;
            throw new ArgumentException("Definition does not support conditions: " + id);
        }
        public static List<EffectSpec> Effects(ProjectDefinition p, string id)
        {
            foreach (var a in p.activities)
                if (a.id == id)
                    return a.effects;
            foreach (var e in p.events)
            {
                if (e.id == id)
                    return e.effects;
                foreach (var c in e.choices)
                    if (c.id == id)
                        return c.effects;
                foreach (var step in e.presentation)
                {
                    if (step.id == id) return step.effects;
                    foreach (var c in step.choices)
                        if (c.id == id) return c.effects;
                }
            }
            foreach (var m in p.modules)
                foreach (var e in m.encounters)
                    if (e.id == id)
                        return e.rewards;
            foreach (var m in p.modules)
                foreach (var e in m.enemies)
                    if (e.id == id)
                        return e.rewards;
            throw new ArgumentException("Definition does not support effects: " + id);
        }
    }
}
