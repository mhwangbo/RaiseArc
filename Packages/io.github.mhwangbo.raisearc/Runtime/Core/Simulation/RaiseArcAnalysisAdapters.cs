using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Analysis
{
    public enum ModuleAnalysisKind { Headless, ResultModel, SceneDriver }
    public enum CatalogCompleteness { Unknown, Partial, Complete, Procedural }
    [Serializable] public sealed class RuleAnalysisContract
    {
        public string id, version, fingerprint, source, editOwner;
        public bool statelessDeterministic;
        public List<string> conditionIds = new List<string>(), effectIds = new List<string>(), reads = new List<string>(), writes = new List<string>();
        internal RuleAnalysisContract Copy()
        {
            var c = (RuleAnalysisContract)MemberwiseClone(); c.conditionIds = new List<string>(conditionIds); c.effectIds = new List<string>(effectIds); c.reads = new List<string>(reads); c.writes = new List<string>(writes); return c;
        }
        internal string Identity => SimulationIdentity.Pack(new[] { id, version, fingerprint, source, editOwner, statelessDeterministic.ToString(), SimulationIdentity.Pack(conditionIds), SimulationIdentity.Pack(effectIds), SimulationIdentity.Pack(reads), SimulationIdentity.Pack(writes) });
    }
    [Serializable] public sealed class ModuleAnalysisContract
    {
        public string moduleId, provider, version, fingerprint, source, editOwner, inputContract, outputContract;
        public string stateContract, randomContract, generatorVersion;
        public ModuleAnalysisKind kind;
        public CatalogCompleteness catalogCompleteness;
        public bool exhaustiveOutcomes;
        public List<string> reads = new List<string>(), writes = new List<string>(), contentIds = new List<string>();
        internal ModuleAnalysisContract Copy()
        {
            var c = (ModuleAnalysisContract)MemberwiseClone(); c.reads = new List<string>(reads); c.writes = new List<string>(writes); c.contentIds = new List<string>(contentIds); return c;
        }
        internal string Identity => SimulationIdentity.Pack(new[] { moduleId, provider, version, fingerprint, source, editOwner, inputContract, outputContract, stateContract, randomContract, generatorVersion,
            kind.ToString(), catalogCompleteness.ToString(), exhaustiveOutcomes.ToString(), SimulationIdentity.Pack(reads), SimulationIdentity.Pack(writes), SimulationIdentity.Pack(contentIds) });
    }
    [Serializable] public sealed class AnalysisOutcome
    {
        public string id, nextState = "", terminalCause = "";
        public double probability = -1;
        public ModuleResult result = new ModuleResult();
        public List<string> contentIds = new List<string>();
    }
    [Serializable] public sealed class ModelDefinition
    {
        public string moduleId;
        public List<AnalysisOutcome> outcomes = new List<AnalysisOutcome>();
    }
    // Callers own serialization of the opaque state. A call must not modify its inputs or retain mutable run state.
    public interface IModuleAnalysis
    {
        ModuleAnalysisContract Contract { get; }
        IReadOnlyList<AnalysisOutcome> Outcomes(StateSnapshot state, string activityId, string savedState);
    }
    public interface IModuleSampler : IModuleAnalysis
    {
        AnalysisOutcome Sample(StateSnapshot state, string activityId, string savedState, uint seed);
    }
    public sealed class ResultModel : IModuleAnalysis
    {
        private readonly ModuleAnalysisContract contract;
        public ModuleAnalysisContract Contract => contract.Copy();
        private readonly IReadOnlyList<AnalysisOutcome> outcomes;
        public ResultModel(ModuleAnalysisContract contract, IReadOnlyList<AnalysisOutcome> outcomes)
        {
            this.contract = (contract ?? throw new ArgumentNullException(nameof(contract))).Copy();
            if (contract.kind != ModuleAnalysisKind.ResultModel) throw new ArgumentException("ResultModel must be labelled as a model.");
            this.outcomes = (outcomes ?? throw new ArgumentNullException(nameof(outcomes))).Select(Copy).ToList();
            this.contract.fingerprint = SimulationIdentity.Hash(SimulationIdentity.Pack(new[] { contract.fingerprint, SimulationIdentity.Pack(this.outcomes.Select(OutcomeIdentity)) }));
        }
        private static string OutcomeIdentity(AnalysisOutcome o) => SimulationIdentity.Pack(new[] { o.id, o.nextState, o.terminalCause,
            o.probability.ToString("R", System.Globalization.CultureInfo.InvariantCulture), o.result.elapsedDays.ToString(System.Globalization.CultureInfo.InvariantCulture), o.result.messageKey,
            SimulationIdentity.Pack(o.result.effects.Select(e => SimulationIdentity.Pack(new[] { e.kind.ToString(), e.actorId, e.target, e.operation.ToString(), e.value.ToString(System.Globalization.CultureInfo.InvariantCulture) }))), SimulationIdentity.Pack(o.contentIds) });
        private static AnalysisOutcome Copy(AnalysisOutcome o) => new AnalysisOutcome { id = o.id, nextState = o.nextState, terminalCause = o.terminalCause, probability = o.probability,
            contentIds = new List<string>(o.contentIds), result = new ModuleResult { elapsedDays = o.result.elapsedDays, messageKey = o.result.messageKey,
                effects = o.result.effects.ConvertAll(e => new EffectSpec { id = e.id, actorId = e.actorId, kind = e.kind, target = e.target, operation = e.operation, value = e.value }) } };
        public IReadOnlyList<AnalysisOutcome> Outcomes(StateSnapshot state, string activityId, string savedState) => outcomes.Select(Copy).ToList();
    }
    public sealed class AnalysisAdapters
    {
        private readonly Dictionary<string, IModuleAnalysis> modules = new Dictionary<string, IModuleAnalysis>(StringComparer.Ordinal);
        private readonly List<KeyValuePair<RuleAnalysisContract, Action<ExtensionRegistry>>> rules = new List<KeyValuePair<RuleAnalysisContract, Action<ExtensionRegistry>>>();
        public IEnumerable<IModuleAnalysis> Modules => modules.Values;
        public IEnumerable<RuleAnalysisContract> Rules => rules.Select(r => r.Key);
        public void RegisterRules(RuleAnalysisContract contract, Action<ExtensionRegistry> registerFreshHandlers)
        {
            if (contract == null || registerFreshHandlers == null || string.IsNullOrEmpty(contract.id) || string.IsNullOrEmpty(contract.version) || string.IsNullOrEmpty(contract.fingerprint))
                throw new ArgumentException("Rules require a stable provider ID, version, code fingerprint and fresh-handler registration.");
            if (rules.Any(r => r.Key.id == contract.id)) throw new ArgumentException("Duplicate rule provider ID.");
            rules.Add(new KeyValuePair<RuleAnalysisContract, Action<ExtensionRegistry>>(contract.Copy(), registerFreshHandlers));
        }
        public void Register(IModuleAnalysis adapter)
        {
            var c = adapter?.Contract ?? throw new ArgumentNullException(nameof(adapter));
            if (string.IsNullOrWhiteSpace(c.moduleId) || string.IsNullOrWhiteSpace(c.version) || string.IsNullOrWhiteSpace(c.fingerprint))
                throw new ArgumentException("Module analysis requires stable module ID, version and implementation/model fingerprint.");
            modules.Add(c.moduleId, adapter);
        }
        public IModuleAnalysis Find(string id) => modules.TryGetValue(id, out var value) ? value : null;
        public void Unregister(string moduleId) => modules.Remove(moduleId);
        public string Fingerprint => SimulationIdentity.Hash(string.Join("\n", modules.Values.OrderBy(a => a.Contract.moduleId, StringComparer.Ordinal)
            .Select(a => a.Contract.Identity + ":" + a.GetType().Assembly.ManifestModule.ModuleVersionId)) + "\n" +
            string.Join("\n", rules.OrderBy(r => r.Key.id, StringComparer.Ordinal).Select(r => r.Key.Identity + ":" + r.Value.Method.Module.ModuleVersionId)));

        internal ExtensionRegistry Extensions(ContentIndex index, bool allowExternalCode)
        {
            var registry = new ExtensionRegistry();
            if (allowExternalCode)
                foreach (var rule in rules)
                    if (rule.Key.statelessDeterministic) rule.Value(registry);
            foreach (var row in index.Data.entries)
            {
                if (index.Find(row.id) is ConditionSpec c && c.kind == ValueKind.Custom && !registry.HasCondition(c.target))
                    registry.RegisterCondition(c.target, new OpaqueCondition());
                if (index.Find(row.id) is EffectSpec e && e.kind == ValueKind.Custom && !registry.HasEffect(e.target))
                    registry.RegisterEffect(e.target, new OpaqueEffect());
            }
            return registry;
        }
        private sealed class OpaqueCondition : ICondition
        {
            public bool Evaluate(StateSnapshot state, ConditionSpec condition) => throw new AnalysisBoundaryException("Custom condition has no isolated analysis contract: " + condition.target);
        }
        private sealed class OpaqueEffect : IEffect
        {
            public IReadOnlyList<EffectSpec> Expand(StateSnapshot state, EffectSpec effect) => throw new AnalysisBoundaryException("Custom effect has no isolated analysis contract: " + effect.target);
        }
    }
}
