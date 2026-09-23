using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Editor;
using PrincessStudio.Editor.Graph;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    internal sealed class RaiseArcNodeDetails
    {
        private readonly ProjectDefinition project;
        private readonly EventDefinition flow, authored;
        private readonly ContentIndex index;
        private readonly string locale;
        private readonly GraphLayoutAsset layout;
        private readonly PresentationSkin skin;
        private readonly Action save;
        private readonly Action<string, string> edit;
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, VisualElement> choices = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, string> sourceIds = new Dictionary<string, string>();

        public RaiseArcNodeDetails(ProjectDefinition project, string eventId, string locale, GraphLayoutAsset layout, PresentationSkin skin, Action save, Action<string, string> edit)
        {
            this.project = project; this.locale = locale; this.layout = layout; this.skin = skin; this.save = save; this.edit = edit;
            index = new ContentIndex(project, locale); authored = project.events.Find(e => e.id == eventId);
            try { flow = RaiseArc.Core.RaiseArcFlowReuse.Expand(project, authored, sourceIds); }
            catch (ArgumentException) { flow = authored; }
            foreach (var step in flow.presentation)
                if (sourceIds.TryGetValue(step.id, out var sourceId) && index.Find(sourceId) is PresentationStep source)
                    foreach (var choice in source.choices)
                    {
                        var resolved = step.choices.Find(c => c.id == RaiseArc.Core.RaiseArcFlowReuse.ChoiceId(step.id, choice.id));
                        if (resolved != null) sourceIds[resolved.id] = choice.id;
                    }
        }
        private static string T(string text) => StudioText.T(text);
        private string Name(string id) => string.IsNullOrEmpty(id) ? T("None") : index.Find(id) is Definition d ? index.Text(d.nameKey, locale) : id;
        private string Target(ValueKind kind, string id, string actor)
        {
            var label = string.IsNullOrEmpty(id) ? T(kind.ToString()) : Name(id);
            if (kind == ValueKind.Relationship) label += " · " + T("Relationship");
            if (RaiseArc.Core.ReusableRules.IsRecord(kind) || kind == ValueKind.ModifierDays) label += " · " + T(kind.ToString());
            if (!string.IsNullOrEmpty(actor)) label = Name(actor) + " · " + label;
            return label;
        }
        private string Condition(ConditionSpec c) => (string.IsNullOrEmpty(c.anyGroup) ? "" : "[OR " + c.anyGroup + "] ") + Target(c.kind, c.target, c.actorId) + " " +
            (c.comparison == Comparison.AtLeast ? "≥" : c.comparison == Comparison.AtMost ? "≤" : c.comparison == Comparison.NotEqual ? "≠" : "=") + " " + c.value;
        private string Effect(EffectSpec e) => Target(e.kind, e.target, e.actorId) + " " +
            (e.operation == EffectOperation.Set ? "= " + (e.kind == ValueKind.Flag ? T(e.value == 0 ? "Off" : "On") : e.value.ToString()) : (e.value >= 0 ? "+" : "") + e.value);
        private static Label Line(VisualElement parent, string text, string css = "node-detail-summary")
        {
            var label = new Label(text); label.AddToClassList(css); parent.Add(label); return label;
        }
        private Foldout Fold(VisualElement parent, NodeLayout state, string key, string text, bool expanded = false, string collapsedText = null)
        {
            var fold = new Foldout { text = text, name = "detail-" + key };
            fold.SetValueWithoutNotify(state.openDetails.Contains(key) || expanded && !state.closedDetails.Contains(key));
            fold.text = fold.value ? text : collapsedText ?? text;
            fold.AddToClassList("node-detail-fold"); parent.Add(fold);
            fold.RegisterValueChangedCallback(e =>
            {
                if (e.target != fold) return;
                fold.text = e.newValue ? text : collapsedText ?? text;
                Undo.RecordObject(layout, "Fold graph details");
                state.closedDetails.Remove(key); state.openDetails.Remove(key);
                if (e.newValue != expanded) (e.newValue ? state.openDetails : state.closedDetails).Add(key);
                save(); e.StopPropagation();
            });
            fold.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            return fold;
        }
        private void Edit(VisualElement parent, string id, string section)
        {
            var target = sourceIds.TryGetValue(id, out var source) ? source : id;
            var button = new Button(() => edit?.Invoke(target, section)) { text = T("Edit"), name = "edit-" + id + "-" + section };
            button.AddToClassList("node-detail-edit"); button.RegisterCallback<PointerDownEvent>(e => e.StopPropagation()); parent.Add(button);
        }
        private void Rules(VisualElement parent, NodeLayout state, string owner, List<ConditionSpec> conditions, List<EffectSpec> effects, string conditionTitle, string effectTitle)
        {
            var cfold = Fold(parent, state, owner + "/conditions", T(conditionTitle) + " · " + (conditions.Count == 0 ? T("Always available") : conditions.Count.ToString()), conditions.Count > 0,
                conditions.Count == 0 ? null : T(conditionTitle) + " · " + string.Join("; ", conditions.Select(Condition)));
            if (conditions.Count > 1) Line(cfold, T("All conditions must match"));
            for (var i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i]; var row = Fold(cfold, state, owner + "/condition/" + (string.IsNullOrEmpty(c.id) ? i.ToString() : c.id), Condition(c));
                Line(row, T("Target") + ": " + Target(c.kind, c.target, c.actorId));
                Line(row, T("Required value") + ": " + c.value); Edit(row, string.IsNullOrEmpty(c.id) ? owner : c.id, "Conditions");
            }
            Edit(cfold, owner, "Conditions");
            var efold = Fold(parent, state, owner + "/effects", T(effectTitle) + " · " + (effects.Count == 0 ? T("No direct effects") : effects.Count.ToString()), effects.Count > 0,
                effects.Count == 0 ? null : T(effectTitle) + " · " + string.Join("; ", effects.Select(Effect)));
            for (var i = 0; i < effects.Count; i++)
            {
                var e = effects[i]; var row = Fold(efold, state, owner + "/effect/" + (string.IsNullOrEmpty(e.id) ? i.ToString() : e.id), Effect(e));
                Line(row, T("Applied before following the outgoing connection"));
                Edit(row, string.IsNullOrEmpty(e.id) ? owner : e.id, "Effects");
            }
            Edit(efold, owner, "Effects");
        }
        private void Resource(VisualElement parent, string key)
        {
            Line(parent, string.IsNullOrEmpty(key) ? T("None") : key);
            if (string.IsNullOrEmpty(key)) return;
            if (!sprites.TryGetValue(key, out var sprite)) { sprite = skin == null ? null : skin.Find(key, locale, project.fallbackLocale); sprites[key] = sprite; }
            if (sprite == null) { Line(parent, T(skin == null ? "Select a Presentation Skin to preview images" : "Image unavailable in the selected skin")); return; }
            var image = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("node-resource-preview"); parent.Add(image);
        }
        private string Change(StageChange change, int count) => change == StageChange.Keep ? T("Keep previous state") : change == StageChange.Clear || count == 0 ? T("Clear") : T("Replace") + " · " + count;
        private void Stage(VisualElement parent, NodeLayout state, PresentationStep step, string editId)
        {
            var bg = Fold(parent, state, step.id + "/background", T("Background") + " · " + Change(step.backgroundChange, string.IsNullOrEmpty(step.backgroundKey) ? 0 : 1));
            if (step.backgroundChange == StageChange.Replace) Resource(bg, step.backgroundKey);
            Edit(bg, editId, "Presentation");
            var actors = Fold(parent, state, step.id + "/actors", T("Actors") + " · " + Change(step.actorsChange, step.actors.Count));
            if (step.actorsChange == StageChange.Replace)
                for (var i = 0; i < step.actors.Count; i++)
                {
                    var a = step.actors[i]; var row = Fold(actors, state, step.id + "/actor/" + a.actorId + "/" + i, Name(a.actorId) + " · " + Name(a.slotId));
                    Line(row, T("Appearance depends on the current game state"));
                    foreach (var pair in new[] { ("Body", a.overrides.body), ("Hair", a.overrides.hair), ("Outfit", a.overrides.outfit), ("Expression", a.overrides.expression) })
                        if (!string.IsNullOrEmpty(pair.Item2)) { Line(row, T(pair.Item1)); Resource(row, pair.Item2); }
                    Edit(row, editId, "Presentation");
                }
            Edit(actors, editId, "Presentation");
            var images = Fold(parent, state, step.id + "/images", T("Images") + " · " + Change(step.imagesChange, step.images.Count));
            if (step.imagesChange == StageChange.Replace)
                foreach (var image in step.images)
                {
                    var row = Fold(images, state, step.id + "/image/" + image.id, image.resourceKey + " · " + T(image.plane.ToString()));
                    Resource(row, image.resourceKey);
                    Line(row, string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.##}, {2:0.##} · {3}: {4:0.##} × {5:0.##}", T("Position"), image.x, image.y, T("Size"), image.width, image.height));
                    Line(row, T("Opacity") + ": " + image.opacity.ToString("0.##", CultureInfo.InvariantCulture)); Edit(row, editId, "Presentation");
                }
            Edit(images, editId, "Presentation");
        }
        public void Populate(FlowNode node, NodeLayout state, VisualElement card, IEnumerable<FlowEdge> edges, Func<FlowEdge, VisualElement> output)
        {
            state.closedDetails ??= new List<string>(); state.openDetails ??= new List<string>();
            var bodies = new List<VisualElement>();
            var compact = new List<VisualElement>();
            var choiceSummaries = new List<(Label summary, Foldout fold)>();
            var toggle = new Button { name = "collapse-" + node.id }; toggle.AddToClassList("node-detail-collapse"); card.Add(toggle);
            void ApplyCollapse()
            {
                toggle.text = T(state.collapsed ? "Expand details" : "Collapse details");
                foreach (var body in bodies) body.style.display = state.collapsed ? DisplayStyle.None : DisplayStyle.Flex;
                foreach (var brief in compact) brief.style.display = state.collapsed ? DisplayStyle.Flex : DisplayStyle.None;
                foreach (var item in choiceSummaries) item.summary.style.display = state.collapsed || !item.fold.value ? DisplayStyle.Flex : DisplayStyle.None;
            }
            toggle.clicked += () => { Undo.RecordObject(layout, "Fold graph node"); state.collapsed = !state.collapsed; ApplyCollapse(); save(); };
            toggle.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            VisualElement Body(VisualElement parent) { var body = new VisualElement(); parent.Add(body); bodies.Add(body); return body; }
            if (!string.IsNullOrEmpty(state.note)) Line(Body(card), state.note);
            if (node.missingTranslations > 0) Line(card, T("Missing") + " · " + node.missingTranslations, "node-warning-message");
            if (node.kind == "Entry")
            {
                compact.Add(Line(card, T("Entry conditions") + " " + flow.conditions.Count + " · " + T("On entry") + " " + string.Join("; ", flow.effects.Select(Effect))));
                Rules(Body(card), state, flow.id, flow.conditions, flow.effects, "Entry conditions", "On entry");
            }
            var options = node.id == flow.id + ":terminal" ? flow.choices : node.stepIds.Select(id => flow.presentation.Find(s => s.id == id)).Where(s => s != null).SelectMany(s => s.choices).ToList();
            Foldout dialogueGroup = null;
            if (node.kind == "Dialogue Block") dialogueGroup = Fold(Body(card), state, node.id + "/dialogue", T("Dialogue") + " · " + node.lineCount, true);
            foreach (var id in node.stepIds)
            {
                var step = flow.presentation.Find(s => s.id == id); var original = authored.presentation.Find(s => s.id == id);
                if (step == null) continue;
                if (!string.IsNullOrEmpty(original?.sharedEventId))
                {
                    Line(card, T("Shared flow") + " · " + Name(original.sharedEventId));
                    Line(card, T("Returns here before following the outgoing connection")); Edit(card, original.sharedEventId, "Content"); continue;
                }
                if (!string.IsNullOrEmpty(original?.sharedStepId)) { Line(card, T("Shared content") + " · " + Name(original.sharedStepId)); Edit(card, original.sharedStepId, "Content"); }
                var steps = !string.IsNullOrEmpty(original?.sharedStepId) && step.kind == PresentationStepKind.Dialogue ? flow.presentation.Where(s => s.id == id || s.id.StartsWith(id + ".line.", StringComparison.Ordinal)).ToList() : new List<PresentationStep> { step };
                if (step.kind == PresentationStepKind.Dialogue)
                {
                    var dialogue = dialogueGroup ?? Fold(Body(card), state, id + "/dialogue", T("Dialogue") + " · " + steps.Count, true);
                    foreach (var line in steps)
                    {
                        var text = index.Text(line.nameKey, locale); var speaker = string.IsNullOrEmpty(line.speakerActorId) ? T("Narration") : Name(line.speakerActorId);
                        var caption = speaker + ": " + text;
                        compact.Add(Line(card, caption.Length > 100 ? caption.Substring(0, 100) + "…" : caption, "node-dialogue"));
                        var item = Fold(dialogue, state, id + "/line/" + line.id, caption.Length > 100 ? caption.Substring(0, 100) + "…" : caption);
                        Line(item, caption, "node-dialogue"); Edit(item, line.id, "Localization"); Stage(item, state, line, line.id);
                    }
                }
                else
                {
                    var group = Body(card);
                    if (step.kind == PresentationStepKind.Condition || step.kind == PresentationStepKind.Effect)
                    {
                        compact.Add(Line(card, string.Join("; ", step.kind == PresentationStepKind.Condition ? step.conditions.Select(Condition) : step.effects.Select(Effect))));
                        Rules(group, state, id, step.conditions, step.effects, "Conditions", "Effects");
                    }
                    Stage(group, state, step, original?.sharedStepId.Length > 0 ? original.sharedStepId : id);
                }
            }
            foreach (var choice in options)
            {
                var area = new VisualElement { name = "choice-detail-" + choice.id }; area.AddToClassList("node-choice-detail"); card.Add(area); choices[choice.id] = area;
                Line(area, index.Text(choice.nameKey, locale), "node-choice-title");
                var summary = Line(area, (choice.conditions.Count == 0 ? T("Always available") : string.Join("; ", choice.conditions.Select(Condition))) + " · " +
                    (choice.effects.Count == 0 ? T("No direct effects") : string.Join("; ", choice.effects.Select(Effect))));
                var fold = Fold(Body(area), state, choice.id + "/details", T("Conditions and effects"));
                choiceSummaries.Add((summary, fold)); fold.RegisterValueChangedCallback(e => { if (e.target == fold) ApplyCollapse(); });
                Rules(fold, state, choice.id, choice.conditions, choice.effects, "Choice conditions", "On choosing"); Edit(fold, choice.id, "Localization");
                var edge = edges.FirstOrDefault(e => e.portId == choice.id); if (edge != null) area.Add(output(edge));
            }
            foreach (var edge in edges.Where(e => !options.Any(c => c.id == e.portId))) card.Add(output(edge));
            if (node.kind == "End") toggle.style.display = DisplayStyle.None;
            ApplyCollapse();
        }
        public void ShowChoice(string id)
        {
            foreach (var item in choices) item.Value.EnableInClassList("choice-executing", item.Key == id);
        }
        public void OpenIssue(string id) => edit?.Invoke(sourceIds.TryGetValue(id, out var source) ? source : id, "Content");
    }
}
