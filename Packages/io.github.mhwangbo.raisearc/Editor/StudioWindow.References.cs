using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public sealed partial class StudioWindow
    {
        private void DrawTargetPicker(VisualElement host, ValueKind kind, string selected, Action<string> changed)
        {
            host.Clear();
            if (kind == ValueKind.Money || kind == ValueKind.Day || kind == ValueKind.Age)
            {
                host.Add(new Label(StudioText.T("Target: global value (no reference required)")));
                return;
            }
            if (kind == ValueKind.Custom)
            {
                Text(host, "Extension key (SDK)", selected, changed);
                return;
            }
            var project = service.Snapshot();
            IEnumerable<Definition> definitions;
            switch (kind)
            {
                case ValueKind.Stat: definitions = project.stats; break;
                case ValueKind.Relationship: definitions = project.npcs; break;
                case ValueKind.Item: definitions = project.items; break;
                case ValueKind.Flag: definitions = project.flags; break;
                case ValueKind.ModifierDays: definitions = project.modifiers; break;
                case ValueKind.RecordBest: case ValueKind.RecordTotal: case ValueKind.RecordAttempts: case ValueKind.RecordPasses:
                    definitions = project.activities.Where(a => a.evaluation?.enabled == true); break;
                default: definitions = Array.Empty<Definition>(); break;
            }
            var entries = definitions.OrderBy(x => x.id, StringComparer.Ordinal).ToList();
            var translations = project.translations
                .Where(x => x.locale == project.defaultLocale)
                .GroupBy(x => x.key).ToDictionary(x => x.Key, x => x.First().text);
            string Caption(Definition entry) =>
                (translations.TryGetValue(entry.nameKey, out var text) && !string.IsNullOrEmpty(text) ? text : entry.nameKey)
                + "  [" + entry.id + "]";
            var current = entries.Find(x => x.id == selected);
            var picker = new Foldout { text = StudioText.T("Target: " + (current != null ? Caption(current) : string.IsNullOrEmpty(selected) ? "Choose…" : "Missing: " + selected)), value = current == null };
            host.Add(picker);
            var search = new TextField(StudioText.T("Search name / ID"));
            picker.Add(search);
            var matches = new List<Definition>(entries);
            var list = new ListView { itemsSource = matches, fixedItemHeight = 26, selectionType = SelectionType.Single };
            list.style.height = 156;
            list.makeItem = () => new Label();
            list.bindItem = (element, index) => ((Label)element).text = Caption(matches[index]);
            list.selectionChanged += values =>
            {
                var entry = values.OfType<Definition>().FirstOrDefault();
                if (entry == null) return;
                changed(entry.id);
                picker.text = "Target: " + Caption(entry);
                picker.value = false;
            };
            picker.Add(list);
            var empty = new Label(StudioText.T("No matching entries. Create the target in its catalog first."));
            picker.Add(empty);
            void Filter(string query)
            {
                list.ClearSelection();
                matches.Clear();
                matches.AddRange(entries.Where(x => Caption(x).IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0));
                empty.style.display = matches.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                list.RefreshItems();
            }
            search.RegisterValueChangedCallback(evt => Filter(evt.newValue));
            Filter("");
        }
    }
}
