using System.Collections.Generic;
using System.Globalization;
using PrincessStudio.Core;
using RaiseArc.Core;
using RaiseArc.Unity;
using UnityEngine;

namespace RaiseArc.Samples
{
    [CreateAssetMenu(menuName = "RaiseArc/Extensions/Gift example")]
    public sealed class RaiseArcGiftExtension : RaiseArcExtensionAsset
    {
        public override string ExtensionId => "raisearc.gift-example";
        public override void Register(ProjectDefinition project, ExtensionRegistry registry)
        {
            var definition = new ExtensionDefinition {
                id = "gift", displayName = "Give a preferred gift",
                parameters = new List<ExtensionParameter> {
                    new ExtensionParameter { name = "npc", displayName = "Recipient", kind = ExtensionParameterKind.Npc },
                    new ExtensionParameter { name = "item", displayName = "Gift item", kind = ExtensionParameterKind.Item },
                    new ExtensionParameter { name = "flag", displayName = "Gift given flag", kind = ExtensionParameterKind.Flag },
                    new ExtensionParameter { name = "liked", displayName = "Preferred gift", kind = ExtensionParameterKind.Boolean, defaultValue = "true" },
                    new ExtensionParameter { name = "amount", displayName = "Relationship increase", kind = ExtensionParameterKind.Integer, defaultValue = "5" }
                }
            };
            var handler = new Gift(definition);
            registry.Register(definition, handler, handler);
        }
        private sealed class Gift : ICondition, IEffect
        {
            private readonly ExtensionDefinition definition;
            public Gift(ExtensionDefinition definition) { this.definition = definition; }
            public bool Evaluate(StateSnapshot state, ConditionSpec condition) =>
                bool.Parse(definition.Read(condition.parameters, "liked")) && state.Items[definition.Read(condition.parameters, "item")] > 0;
            public IReadOnlyList<EffectSpec> Expand(StateSnapshot state, EffectSpec effect)
            {
                string Read(string name) => definition.Read(effect.parameters, name);
                return new[] {
                    new EffectSpec { kind = ValueKind.Item, target = Read("item"), value = -1 },
                    new EffectSpec { kind = ValueKind.Relationship, target = Read("npc"), value = int.Parse(Read("amount"), CultureInfo.InvariantCulture) },
                    new EffectSpec { kind = ValueKind.Flag, target = Read("flag"), operation = EffectOperation.Set, value = 1 }
                };
            }
        }
    }
}
