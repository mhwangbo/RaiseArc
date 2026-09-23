using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using RaiseArc.Core;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public static class RaiseArcExtensionFields
    {
        public static void Draw(VisualElement parent, ProjectDefinition project, ExtensionRegistry registry, bool condition,
            string id, Action<string> setId, List<ExtensionValue> values, Action changed)
        {
            parent.Clear();
            var definitions = registry.Definitions.Where(x => condition ? x.condition : x.effect).ToList();
            var ids = definitions.Select(x => x.id).ToList();
            var labels = definitions.Select(x => x.displayName + " · " + x.id).ToList();
            if (!ids.Contains(id)) { ids.Insert(0, id); labels.Insert(0, string.IsNullOrEmpty(id) ? "Select an extension" : "Missing extension: " + id); }
            var picker = new DropdownField("Extension", labels, ids.IndexOf(id));
            parent.Add(picker);
            picker.RegisterValueChangedCallback(e => {
                var selected = ids[labels.IndexOf(e.newValue)];
                setId(selected);
                // Changing implementations is explicit. Unknown data is otherwise retained untouched.
                values.Clear();
                var d = registry.Definition(selected);
                if (d != null) foreach (var p in d.parameters) values.Add(new ExtensionValue { name = p.name, value = p.defaultValue });
                changed?.Invoke(); Draw(parent, project, registry, condition, selected, setId, values, changed);
            });
            var definition = registry.Definition(id);
            if (definition == null)
            {
                parent.Add(new HelpBox("Enable the extension asset on this Game Project in the Unity Inspector, then refresh. Existing parameters are preserved.", HelpBoxMessageType.Warning));
                foreach (var v in values) if (v != null) parent.Add(new Label(v.name + ": " + v.value));
                return;
            }
            foreach (var p in definition.parameters)
            {
                var current = definition.Read(values, p.name);
                void Set(string value)
                {
                    var entry = values.Find(x => x.name == p.name);
                    if (entry == null) { entry = new ExtensionValue { name = p.name }; values.Add(entry); }
                    entry.value = value; changed?.Invoke();
                }
                var label = string.IsNullOrEmpty(p.displayName) ? p.name : p.displayName;
                if (ExtensionDefinition.IsReference(p.kind))
                {
                    IEnumerable<Definition> content = p.kind == ExtensionParameterKind.Npc ? project.npcs.Cast<Definition>() :
                        p.kind == ExtensionParameterKind.Item ? project.items.Cast<Definition>() : p.kind == ExtensionParameterKind.Flag ? project.flags.Cast<Definition>() : project.stats.Cast<Definition>();
                    var options = content.Select(x => x.id).ToList();
                    if (!options.Contains(current)) options.Insert(0, current);
                    var field = new DropdownField(label, options, options.IndexOf(current));
                    field.RegisterValueChangedCallback(e => Set(e.newValue)); parent.Add(field);
                }
                else if (p.kind == ExtensionParameterKind.Integer && int.TryParse(current, out var number))
                {
                    var field = new IntegerField(label) { value = number, isDelayed = true };
                    field.RegisterValueChangedCallback(e => Set(e.newValue.ToString(System.Globalization.CultureInfo.InvariantCulture))); parent.Add(field);
                }
                else if (p.kind == ExtensionParameterKind.Boolean && bool.TryParse(current, out var boolean))
                {
                    var field = new Toggle(label) { value = boolean };
                    field.RegisterValueChangedCallback(e => Set(e.newValue ? "true" : "false")); parent.Add(field);
                }
                else
                {
                    var field = new TextField(label) { value = current, tooltip = p.kind.ToString() };
                    field.RegisterValueChangedCallback(e => Set(e.newValue)); parent.Add(field);
                }
            }
            parent.Add(new HelpBox(definition.analysis, HelpBoxMessageType.Info));
        }
    }
}
