using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor.Graph
{
    public sealed partial class GraphWorkbenchWindow
    {
        private bool LeaveInspector()
        {
            if (!inspectorDirty) return true;
            StageInspector();
            return !inspectorDirty;
        }
        private void StageInspector()
        {
            if (!inspectorDirty) return;
            var value = inspectorProject;
            Stage(new GraphEdit { operation = "ReplaceProject", targetId = selectedId, replacement = value });
        }
        private void RefreshInspector()
        {
            if (inspector == null || project == null) return;
            // Canvas selection preserves an unfinished inspector draft instead of silently overwriting it.
            if (inspectorDirty) { Notify("Stage or discard the current inspector edits before editing another selection."); return; }
            inspector.Clear(); inspectorProject = codec.Clone(project);
            var localIndex = new ContentIndex(inspectorProject, locale, extensions: asset.CreateExtensions(inspectorProject)); var selected = localIndex.Find(selectedId);
            var graph = string.IsNullOrEmpty(eventId) ? null : GraphProjection.Build(project, eventId, locale);
            var node = graph?.nodes.Find(n => n.id == selectedId);
            var shared = selected as PresentationStep;
            var sourceId = !string.IsNullOrEmpty(shared?.sharedStepId) ? shared.sharedStepId : shared?.sharedEventId;
            var isShared = !string.IsNullOrEmpty(sourceId);
            if (selected == null && selectedId.EndsWith(":terminal", StringComparison.Ordinal)) selected = localIndex.Find(eventId);
            inspector.Add(new Label(StudioText.T(node?.kind ?? selected?.GetType().Name ?? "Selection")) { name = "inspector-heading" });
            inspector.Add(new Label(StudioText.T(selectedId)));
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; inspector.Add(actions);
            actions.Add(B("Save", ApplyChanges)); actions.Add(B("Update preview", StageInspector));
            if (selected is EndingDefinition ending)
                inspector.Add(B(StudioText.Language == "ko" ? "저장된 엔딩으로 가는 경로 찾기" : "Find a path to the saved ending", () => RaiseArc.Editor.EndingTests.Open(asset, ending.id)));
            actions.Add(B("Revert fields", () => { inspectorDirty = false; RefreshInspector(); RefreshSaveStatus(); }));
            inspector.Add(new HelpBox(StudioText.T("Save writes all content edits to the project asset. Selecting another node keeps your edits in the preview; it does not save them."), HelpBoxMessageType.Info));
            if (node != null)
            {
                var flow = new Foldout { text = StudioText.T("Flow"), value = true }; inspector.Add(flow);
                foreach (var edge in graph.edges.Where(x => x.sourceId == node.id))
                {
                    Picker(flow, edge.label, graph.nodes.Where(n => n.id != node.id && n.kind != "Entry").Select(n => (n.id, n.title)), edge.targetId,
                        id => { if (LeaveInspector()) Stage(new GraphEdit { operation = "ConnectNodes", eventId = eventId, targetId = node.id, portId = edge.portId, nextId = id }); });
                    flow.Add(B("Disconnect " + edge.label, () => Stage(new GraphEdit { operation = "DisconnectNodes", eventId = eventId, targetId = node.id, portId = edge.portId })));
                    flow.Add(B("Insert dialogue on " + edge.label, () => { var id = "step-" + Guid.NewGuid().ToString("N").Substring(0, 8); Stage(new GraphEdit { operation = "InsertNodeBetween", eventId = eventId, targetId = node.id, portId = edge.portId, nextId = edge.targetId.EndsWith(":terminal", StringComparison.Ordinal) ? EventSequence.TerminalChoices : edge.targetId, node = new PresentationStep { id = id, nameKey = "step." + id, backgroundChange = StageChange.Keep, actorsChange = StageChange.Keep, imagesChange = StageChange.Keep } }); }));
                }
                if (isShared)
                {
                    inspector.Add(new HelpBox(StudioText.T("Shared content follows its original. Connections here belong to this use. Edit the original to change all uses."), HelpBoxMessageType.Info));
                    inspector.Add(B("Edit original", () => Navigate(sourceId)));
                    inspector.Add(B("Show shared uses", () => { Navigate(sourceId); panel = "References"; RefreshDiagnostics(); }));
                }
                else if (node.kind == "Dialogue Block")
                {
                    inspector.Add(new Label(StudioText.T("Dialogue Block · " + node.stepIds.Count + " lines. Edit the scene here; individual line IDs remain stable.")));
                    foreach (var id in node.stepIds) { var step = (PresentationStep)localIndex.Find(id); var line = new Foldout { text = StudioText.T(index.Text(step.nameKey, locale) + " · " + id), value = false }; inspector.Add(line); DrawSections(line, step); }
                    inspector.Add(B("+ Dialogue line", () => { var last = node.stepIds[node.stepIds.Count - 1]; var e = inspectorProject.events.Find(x => x.id == eventId); var step = e.presentation.Find(x => x.id == last); var next = EventSequence.Next(e, step); var id = "line-" + Guid.NewGuid().ToString("N").Substring(0, 8); var added = new PresentationStep { id = id, nameKey = "dialogue." + id, nextStepId = next.Length == 0 ? EventSequence.TerminalChoices : next, backgroundChange = StageChange.Keep, actorsChange = StageChange.Keep, imagesChange = StageChange.Keep }; step.nextStepId = id; e.presentation.Insert(e.presentation.IndexOf(step) + 1, added); MarkInspectorDirty(); StageInspector(); }));
                }
                else if (selected != null) DrawSections(inspector, selected);
                var metadata = new Foldout { text = StudioText.T("Layout only · note / group / color"), value = false }; inspector.Add(metadata);
                var position = layout.Get(eventId, node.id, graph.nodes.IndexOf(node));
                void Metadata(Action edit) { Undo.RecordObject(layout, "Edit graph layout metadata"); edit(); SaveLayout(); RefreshGraph(); }
                Text(metadata, "Note", position.note, x => Metadata(() => position.note = x)); Text(metadata, "Group", position.group, x => Metadata(() => position.group = x));
                var collapsed = new Toggle(StudioText.T("Collapsed")) { value = position.collapsed }; collapsed.RegisterValueChangedCallback(e => Metadata(() => position.collapsed = e.newValue)); metadata.Add(collapsed);
                var color = new UnityEditor.UIElements.ColorField(StudioText.T("Color")) { value = position.color }; color.RegisterValueChangedCallback(e => Metadata(() => position.color = e.newValue)); metadata.Add(color);
            }
            else if (selected != null) DrawSections(inspector, selected);
            if (!isShared) DrawLocalizationInspector(inspector, selectedId);
            var references = new Foldout { text = StudioText.T("References"), value = false }; inspector.Add(references);
            foreach (var reference in index.References(selectedId).Concat(index.References(selectedId, false)).Take(50)) references.Add(B(reference.relationType + " · " + reference.sourceId + " → " + reference.targetId, () => Navigate(reference.sourceId == selectedId ? reference.targetId : reference.sourceId)));
            if (selected != null) inspector.Add(B("Review deletion impact…", () =>
            {
                Safe(() =>
                {
                    var draftApi = new AuthoringService(project, codec, asset.CreateExtensions(project)); var impact = draftApi.GetDeletionImpact(selectedId);
                    var message = impact.references.Count + " incoming references:\n" + string.Join("\n", impact.references.Take(12).Select(r => r.sourceId + " · " + r.sourcePropertyPath)) + "\nStep links will end the event. Other broken references must be repaired before Apply.";
                    if (StudioText.Dialog("Delete " + selectedId, message, "Stage deletion", "Cancel")) Stage(new GraphEdit { operation = "DeleteWithImpactCheck", targetId = selectedId, impactToken = impact.token });
                });
            }));
        }
        private void EditNodeDetail(string id, string section)
        {
            if (!LeaveInspector()) return;
            if (OwningEvent(id) != eventId) Navigate(id);
            else { selectedId = id; RefreshInspector(); SyncExplorerSelection(); RefreshDiagnostics(); }
            var target = inspector.Q<Foldout>("inspector-section-" + section);
            if (target == null) target = inspector.Q<Foldout>("inspector-section-Content");
            if (target == null) return;
            target.value = true;
            target.Query<Foldout>().ForEach(f => f.value = true);
            var scrollTarget = target;
            inspector.schedule.Execute(() => { scrollTarget.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(scrollTarget); scrollTarget.Focus(); });
        }
        private void DrawSections(VisualElement parent, object value)
        {
            if (value is PresentationStep dialogue) RaiseArc.Editor.RaiseArcVoiceField.Draw(parent, inspectorProject, dialogue, locale, MarkInspectorDirty);
            if (value is AppearanceRule) parent.Add(new HelpBox(StudioText.T("Scales all actor layers together around the body image's bottom. Higher priority wins; 0 keeps the previous size."), HelpBoxMessageType.Info));
            var presentationFields = new[] { "backgroundKey", "backgroundChange", "actorsChange", "imagesChange", "actors", "images", "overrides", "slots" };
            var identity = new[] { "id", "nameKey", "descriptionKey", "tags" };
            var flow = new[] { "kind", "nextStepId", "falseStepId" };
            var fields = value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => f.Name).Where(n => n != "voiceKey").ToArray();
            var groups = new[] { ("Identity", identity), ("Flow settings", flow), ("Conditions", new[] { "conditions" }), ("Content", fields.Except(identity).Except(flow).Except(presentationFields).Except(new[] { "conditions", "effects", "presentation" }).ToArray()), ("Presentation", presentationFields), ("Effects", new[] { "effects" }) };
            foreach (var group in groups)
            {
                if (!group.Item2.Any(fields.Contains)) continue;
                var section = new Foldout { text = StudioText.T(group.Item1), name = "inspector-section-" + group.Item1, value = group.Item1 == "Identity" || group.Item1 == "Content" }; parent.Add(section);
                DrawDto(section, value, 0, group.Item2);
            }
        }
        private void DrawDto(VisualElement parent, object value, int depth, string[] only = null)
        {
            if (value == null || depth > 5) return;
            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.Name == "parameters" && (value is ConditionSpec || value is EffectSpec)) continue;
                if (only != null && !only.Contains(field.Name)) continue;
                if (field.Name == "presentation" && value is EventDefinition) continue;
                if (field.Name == "sharedStepId" || field.Name == "sharedEventId") continue;
                var child = field.GetValue(value); var label = value is AppearanceRule && field.Name == "scale" ? "Size multiplier (0 = inherit, 1 = original)" : ObjectNames.NicifyVariableName(field.Name);
                void Set(object v) { field.SetValue(value, v); MarkInspectorDirty(); }
                if (field.FieldType == typeof(string))
                {
                    if (field.Name == "id") { var identity = new Label(StudioText.T("ID · " + child)) { tooltip = child as string }; identity.AddToClassList("workbench-id"); parent.Add(identity); continue; }
                    var references = ReferenceOptions(value, field.Name);
                    if (references != null || field.Name == "target" && (value is ConditionSpec || value is EffectSpec))
                    {
                        var host = new VisualElement(); parent.Add(host);
                        void Rebuild()
                        {
                            host.Clear(); var options = ReferenceOptions(value, field.Name);
                            if (field.Name == "target" && value is ConditionSpec customCondition && customCondition.kind == ValueKind.Custom)
                                RaiseArc.Editor.RaiseArcExtensionFields.Draw(host, inspectorProject, asset.CreateExtensions(inspectorProject), true, customCondition.target, x => customCondition.target = x, customCondition.parameters, MarkInspectorDirty);
                            else if (field.Name == "target" && value is EffectSpec customEffect && customEffect.kind == ValueKind.Custom)
                                RaiseArc.Editor.RaiseArcExtensionFields.Draw(host, inspectorProject, asset.CreateExtensions(inspectorProject), false, customEffect.target, x => customEffect.target = x, customEffect.parameters, MarkInspectorDirty);
                            else if (options == null) Text(host, label, field.GetValue(value) as string, x => Set(x));
                            else Picker(host, label, options, field.GetValue(value) as string, x => Set(x));
                        }
                        Rebuild();
                        if (field.Name == "target")
                        {
                            var signature = (value is ConditionSpec c ? c.kind + ":" + c.actorId : value is EffectSpec f ? f.kind + ":" + f.actorId : "");
                            host.schedule.Execute(() =>
                            {
                                var current = value is ConditionSpec condition ? condition.kind + ":" + condition.actorId : value is EffectSpec effect ? effect.kind + ":" + effect.actorId : "";
                                if (current == signature) return;
                                signature = current; Rebuild();
                            }).Every(100);
                        }
                    }
                    else Text(parent, label, child as string, x => Set(x));
                }
                else if (field.FieldType == typeof(int)) { var input = new IntegerField(StudioText.T(label)) { value = (int)child }; input.RegisterValueChangedCallback(e => Set(e.newValue)); parent.Add(input); }
                else if (field.FieldType == typeof(float)) { var input = new FloatField(StudioText.T(label)) { value = (float)child }; input.RegisterValueChangedCallback(e => Set(e.newValue)); parent.Add(input); }
                else if (field.FieldType == typeof(bool)) { var input = new Toggle(StudioText.T(label)) { value = (bool)child }; input.RegisterValueChangedCallback(e => Set(e.newValue)); parent.Add(input); }
                else if (field.FieldType.IsEnum) { var input = StudioText.EnumField(label, (Enum)child); input.RegisterValueChangedCallback(e => Set(e.newValue)); parent.Add(input); }
                else if (child is IList list)
                {
                    var foldout = new Foldout { text = StudioText.T(label + " · " + list.Count), value = false }; parent.Add(foldout);
                    var type = field.FieldType.GetGenericArguments().FirstOrDefault(); if (type == null) continue;
                    for (var i = 0; i < list.Count; i++)
                    {
                        var item = list[i]; var index = i; var row = new Foldout { text = StudioText.T(label + " " + (i + 1)), value = false }; foldout.Add(row);
                        if (type == typeof(string)) Text(row, "Value", item as string, x => { list[index] = x; MarkInspectorDirty(); }); else DrawDto(row, item, depth + 1);
                        row.Add(B("Remove", () => { list.Remove(item); MarkInspectorDirty(); row.RemoveFromHierarchy(); }));
                    }
                    foldout.Add(B("+ " + label, () =>
                    {
                        var item = CreateDto(type); list.Add(item); MarkInspectorDirty();
                        var row = new Foldout { text = StudioText.T("New " + label), value = true }; foldout.Add(row);
                        if (type == typeof(string)) { var index = list.Count - 1; Text(row, "Value", "", x => list[index] = x); } else DrawDto(row, item, depth + 1);
                        row.Add(B("Remove", () => { list.Remove(item); MarkInspectorDirty(); row.RemoveFromHierarchy(); }));
                    }));
                }
                else if (child != null) { var fold = new Foldout { text = StudioText.T(label), value = false }; parent.Add(fold); DrawDto(fold, child, depth + 1); }
            }
        }
        private object CreateDto(Type type)
        {
            if (type == typeof(string)) return "";
            var value = Activator.CreateInstance(type);
            if (value is Definition d) { d.id = "content-" + Guid.NewGuid().ToString("N").Substring(0, 8); d.nameKey = "content." + d.id; }
            if (value is ConditionSpec c) { c.id = "condition-" + Guid.NewGuid().ToString("N"); c.target = project.stats.FirstOrDefault()?.id ?? ""; }
            if (value is EffectSpec f) { f.id = "effect-" + Guid.NewGuid().ToString("N"); f.target = project.stats.FirstOrDefault()?.id ?? ""; }
            if (value is StageImage image) image.id = "image-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (value is ActorPlacement actor) { actor.actorId = project.actors.FirstOrDefault()?.id ?? ""; actor.slotId = project.stageSlots.FirstOrDefault()?.id ?? ""; }
            return value;
        }
        private IEnumerable<(string, string)> ReferenceOptions(object owner, string field)
        {
            IEnumerable<Definition> definitions = null;
            switch (field)
            {
                case "actorId": case "speakerActorId": definitions = inspectorProject.actors; break;
                case "subjectId": definitions = new Definition[] { inspectorProject.character }.Concat(inspectorProject.npcs); break;
                case "profileId": definitions = inspectorProject.appearanceProfiles; break;
                case "slotId": definitions = inspectorProject.stageSlots; break;
                case "moduleId": definitions = inspectorProject.modules; break;
                case "statId": definitions = inspectorProject.stats; break;
                case "qualificationFlagId": definitions = inspectorProject.flags.Where(f => f.permanent); break;
                case "activityId": definitions = inspectorProject.activities; break;
                case "nextStepId": case "falseStepId": return (inspectorProject.events.Find(e => e.id == eventId)?.presentation ?? new List<PresentationStep>()).Select(s => (s.id, index.Text(s.nameKey, locale))).Concat(new[] { (EventSequence.End, "End event"), (EventSequence.TerminalChoices, "Terminal choices"), ("", "List order") });
                case "target":
                    var kind = owner is ConditionSpec c ? c.kind : owner is EffectSpec f ? f.kind : ValueKind.Custom;
                    var actor = owner is ConditionSpec condition ? condition.actorId : owner is EffectSpec effect ? effect.actorId : "";
                    if (kind == ValueKind.Custom) return null;
                    definitions = kind == ValueKind.ModifierDays ? inspectorProject.modifiers : RaiseArc.Core.ReusableRules.IsRecord(kind) ? inspectorProject.activities.Where(a => a.evaluation?.enabled == true) : kind == ValueKind.Stat ? string.IsNullOrEmpty(actor) ? inspectorProject.stats : inspectorProject.actors.Find(a => a.id == actor)?.conditions : kind == ValueKind.Flag ? (IEnumerable<Definition>)inspectorProject.flags : kind == ValueKind.Item ? inspectorProject.items : kind == ValueKind.Relationship ? inspectorProject.npcs : new List<Definition>(); break;
                case "backgroundKey": case "imageKey": case "resourceKey": case "body": case "hair": case "outfit": case "expression": case "appearanceKey":
                    var skin = previewSkin;
                    return (skin == null ? Enumerable.Empty<string>() : skin.sprites.Select(s => s.key)).Concat(inspectorProject.localizedAssets.Select(a => a.key)).Distinct().Select(x => (x, x)).Concat(new[] { ("", "None / inherit") });
            }
            return definitions?.Select(d => (d.id, index.Text(d.nameKey, locale))).Concat(new[] { ("", "Default / none") });
        }
        private static void Text(VisualElement parent, string label, string value, Action<string> set)
        { var field = new TextField(StudioText.T(label)) { value = value ?? "", multiline = label.Contains("Text") || label == "Note" }; field.RegisterValueChangedCallback(e => set(e.newValue)); parent.Add(field); }
        private static void Picker(VisualElement parent, string label, IEnumerable<(string id, string name)> source, string current, Action<string> set)
        {
            var options = source.ToList(); var selectedName = options.FirstOrDefault(x => x.id == current).name ?? current;
            var fold = new Foldout { text = StudioText.T(label) + " · " + StudioText.T(selectedName), value = false }; parent.Add(fold);
            var search = new UnityEditor.UIElements.ToolbarSearchField(); fold.Add(search); var matches = options.ToList();
            var list = new ListView { itemsSource = matches, fixedItemHeight = 24 }; list.style.height = 125; fold.Add(list);
            list.makeItem = () => new Label(); list.bindItem = (element, i) => ((Label)element).text = matches[i].name + " [" + matches[i].id + "]";
            list.selectionChanged += selected => { foreach (var item in selected) { var option = ((string id, string name))item; set(option.id); fold.text = StudioText.T(label) + " · " + option.name; fold.value = false; break; } };
            search.RegisterValueChangedCallback(e => { list.ClearSelection(); matches.Clear(); matches.AddRange(options.Where(x => (x.id + " " + x.name).IndexOf(e.newValue, StringComparison.OrdinalIgnoreCase) >= 0)); list.Rebuild(); });
        }
        private void DrawLocalizationInspector(VisualElement host, string id)
        {
            var fold = new Foldout { text = StudioText.T("Localization · all languages"), name = "inspector-section-Localization", value = true }; host.Add(fold);
            var local = new ContentIndex(inspectorProject, locale, extensions: asset.CreateExtensions(inspectorProject));
            var node = string.IsNullOrEmpty(eventId) ? null : GraphProjection.Build(inspectorProject, eventId).nodes.Find(n => n.id == id);
            var ids = node != null && node.stepIds.Count > 0 ? node.stepIds : new List<string> { id };
            foreach (var key in ids.SelectMany(member => local.References(member, false)).Where(r => r.relationType == ContentRelation.Localizes).Select(r => r.targetId.Substring(4)).Distinct())
            {
                var row = new Foldout { text = StudioText.T(key), value = false }; fold.Add(row);
                foreach (var language in inspectorProject.locales)
                {
                    var entry = inspectorProject.translations.Find(t => t.key == key && t.locale == language);
                    var review = new Label(); row.Add(review);
                    void RefreshReview() => review.text = language + " · " + StudioText.T(string.IsNullOrWhiteSpace(entry?.text) ? "Missing" : entry.draft ? "Draft" : !entry.reviewed ? "Needs review" : "Reviewed");
                    RefreshReview();
                    Text(row, language + " Text", entry?.text ?? "", text => { if (entry == null) { entry = new TranslationEntry { key = key, locale = language }; inspectorProject.translations.Add(entry); } entry.text = text; entry.reviewed = false; RefreshReview(); MarkInspectorDirty(); });
                    row.Add(B("Mark as draft", () => { if (entry == null) return; entry.draft = true; entry.reviewed = false; RefreshReview(); MarkInspectorDirty(); }));
                    row.Add(B("Mark reviewed", () => { if (entry == null || string.IsNullOrWhiteSpace(entry.text)) return; entry.draft = false; entry.reviewed = true; RefreshReview(); MarkInspectorDirty(); }));
                }
            }
        }
    }
}
