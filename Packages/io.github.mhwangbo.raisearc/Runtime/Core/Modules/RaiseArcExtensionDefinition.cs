using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Core
{
    [Serializable] public sealed class ExtensionValue
    {
        public string name = "";
        public string value = "";
    }
    public enum ExtensionParameterKind { Text, Integer, Boolean, Stat, Npc, Item, Flag }
    [Serializable] public sealed class ExtensionParameter
    {
        public string name = "";
        public string displayName = "";
        public ExtensionParameterKind kind;
        public string defaultValue = "";
        public bool required = true;
    }
    [Serializable] public sealed class ExtensionDefinition
    {
        public string id = "";
        public string displayName = "";
        public List<ExtensionParameter> parameters = new List<ExtensionParameter>();
        public bool condition;
        public bool effect;
        public string analysis = "Requires an explicit analysis adapter; registration alone does not enable analysis.";
        [NonSerialized] public Func<IReadOnlyList<ExtensionValue>, string> validate;

        public string Read(IReadOnlyList<ExtensionValue> values, string name)
        {
            var parameter = parameters.Find(p => p.name == name) ?? throw new ArgumentException("Unknown extension parameter: " + name);
            return values?.FirstOrDefault(v => v != null && v.name == name)?.value ?? parameter.defaultValue;
        }
        public IEnumerable<string> Errors(ProjectDefinition project, IReadOnlyList<ExtensionValue> values)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (values != null) foreach (var value in values)
                if (value == null || !names.Add(value.name) || !parameters.Exists(p => p.name == value.name))
                    yield return "Unknown or duplicate parameter in " + id + ".";
            foreach (var p in parameters)
            {
                var value = Read(values, p.name);
                if (string.IsNullOrEmpty(value)) { if (p.required) yield return id + "." + p.name + " is required."; continue; }
                bool valid;
                switch (p.kind)
                {
                    case ExtensionParameterKind.Integer: valid = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _); break;
                    case ExtensionParameterKind.Boolean: valid = bool.TryParse(value, out _); break;
                    case ExtensionParameterKind.Stat: valid = project.stats.Exists(x => x.id == value); break;
                    case ExtensionParameterKind.Npc: valid = project.npcs.Exists(x => x.id == value); break;
                    case ExtensionParameterKind.Item: valid = project.items.Exists(x => x.id == value); break;
                    case ExtensionParameterKind.Flag: valid = project.flags.Exists(x => x.id == value); break;
                    default: valid = true; break;
                }
                if (!valid) yield return "Invalid " + p.kind + " parameter: " + id + "." + p.name + " = " + value;
            }
            var error = validate?.Invoke(values ?? Array.Empty<ExtensionValue>());
            if (!string.IsNullOrEmpty(error)) yield return error;
        }
        public static bool IsReference(ExtensionParameterKind kind) => kind == ExtensionParameterKind.Stat || kind == ExtensionParameterKind.Npc || kind == ExtensionParameterKind.Item || kind == ExtensionParameterKind.Flag;
    }
}
