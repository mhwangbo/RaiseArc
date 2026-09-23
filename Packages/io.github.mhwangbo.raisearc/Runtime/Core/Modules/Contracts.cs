using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PrincessStudio.Core
{
    public interface ICondition
    {
        bool Evaluate(StateSnapshot state, ConditionSpec condition);
    }
    /// <summary>Extensions propose built-in effects; only the core applies them.</summary>
    public interface IEffect
    {
        IReadOnlyList<EffectSpec> Expand(StateSnapshot state, EffectSpec effect);
    }
    public interface IGameModeModule
    {
        string Id
        {
            get;
        }
        Task<ModuleResult> ExecuteAsync(ModuleContext context, CancellationToken cancellationToken);
    }
    public interface IResultMapper
    {
        ModuleResult Map(ModuleContext context, ModuleResult result);
    }
    public interface ISaveParticipant
    {
        string Id
        {
            get;
        }
        int Version
        {
            get;
        }
        string Capture();
        void Validate(int version, string payload);
        void Restore(int version, string payload);
    }
    public sealed class ExtensionRegistry
    {
        private readonly Dictionary<string, ICondition> conditions = new Dictionary<string, ICondition>(StringComparer.Ordinal);
        private readonly Dictionary<string, IEffect> effects = new Dictionary<string, IEffect>(StringComparer.Ordinal);
        private readonly Dictionary<string, RaiseArc.Core.ExtensionDefinition> definitions = new Dictionary<string, RaiseArc.Core.ExtensionDefinition>(StringComparer.Ordinal);
        public IEnumerable<RaiseArc.Core.ExtensionDefinition> Definitions => definitions.Values;
        public readonly List<string> ConfigurationErrors = new List<string>();
        public RaiseArc.Core.ExtensionDefinition Definition(string id) => id != null && definitions.TryGetValue(id, out var value) ? value : null;
        public void Register(RaiseArc.Core.ExtensionDefinition definition, ICondition condition = null, IEffect effect = null)
        {
            if (definition == null || !ProjectValidator.IsIdentifier(definition.id) || string.IsNullOrWhiteSpace(definition.displayName) ||
                condition == null && effect == null || conditions.ContainsKey(definition.id) || effects.ContainsKey(definition.id) || definitions.ContainsKey(definition.id))
                throw new ArgumentException("Invalid or duplicate extension ID: " + definition?.id);
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (definition.parameters == null) throw new ArgumentException("Extension parameters are required.");
            foreach (var p in definition.parameters)
                if (p == null || !ProjectValidator.IsIdentifier(p.name) || !names.Add(p.name)) throw new ArgumentException("Invalid or duplicate extension parameter.");
            definition.condition = condition != null; definition.effect = effect != null;
            definitions.Add(definition.id, definition);
            if (condition != null) RegisterCondition(definition.id, condition);
            if (effect != null) RegisterEffect(definition.id, effect);
        }
        public void RegisterCondition(string id, ICondition handler) => conditions.Add(id, handler ?? throw new ArgumentNullException(nameof(handler)));
        public void RegisterEffect(string id, IEffect handler) => effects.Add(id, handler ?? throw new ArgumentNullException(nameof(handler)));
        public bool HasCondition(string id) => conditions.ContainsKey(id);
        public bool HasEffect(string id) => effects.ContainsKey(id);
        internal ICondition Condition(string id) => conditions[id];
        internal IEffect Effect(string id) => effects[id];
    }
    public sealed class ModuleContext
    {
        public string SessionId
        {
            get;
        }
        public string ModuleId
        {
            get;
        }
        public string ActivityId
        {
            get;
        }
        public StateSnapshot State
        {
            get;
        }
        internal ModuleContext(string module, string activity, StateSnapshot state)
        {
            SessionId = Guid.NewGuid().ToString("N");
            ModuleId = module;
            ActivityId = activity;
            State = state;
        }
    }
    [Serializable]
    public sealed class ModuleResult
    {
        public string sessionId = "";
        public int elapsedDays;
        public string messageKey = "";
        public List<EffectSpec> effects = new List<EffectSpec>();
    }
    public sealed class ModuleResultException : InvalidOperationException
    {
        public ModuleResultException(string message) : base(message) { }
    }
}
