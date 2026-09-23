using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public sealed partial class StudioWindow
    {
        [SerializeField] private PresentationSkin screenSkin;
        private void DrawScopedTarget(VisualElement host, ValueKind kind, string actorId, string target, Action<string> setActor, Action<string> setTarget)
        {
            host.Clear();
            var p = service.Snapshot();
            if (kind != ValueKind.Stat && kind != ValueKind.Age) { setActor(""); DrawTargetPicker(host, kind, target, setTarget); return; }
            SelectReference(host, "Subject (default = protagonist)", p.actors, actorId, id => { setActor(id); setTarget(""); DrawScopedTarget(host, kind, id, "", setActor, setTarget); }, true);
            var valueHost = new VisualElement(); host.Add(valueHost);
            if (string.IsNullOrEmpty(actorId)) DrawTargetPicker(valueHost, kind, target, setTarget);
            else if (kind == ValueKind.Stat) SelectReference(valueHost, "Local condition", p.actors.Find(x => x.id == actorId)?.conditions ?? new List<StatDefinition>(), target, setTarget);
            else valueHost.Add(new Label(StudioText.T("Uses this actor's age.")));
        }
        private void SelectReference<T>(VisualElement parent, string label, IEnumerable<T> source, string current, Action<string> changed, bool optional = false) where T : Definition
        {
            var entries = source.ToList();
            var picker = new Foldout { text = StudioText.T(label + ": " + (entries.Find(x => x.id == current)?.id ?? (string.IsNullOrEmpty(current) ? "Choose…" : "Missing: " + current))), value = false };
            var search = new ToolbarSearchField(); picker.Add(search);
            var matches = new List<T>(entries);
            var list = new ListView { itemsSource = matches, fixedItemHeight = 26, selectionType = SelectionType.Single };
            list.style.height = 130;
            var p = service.Snapshot();
            string Caption(T d) => PreviewText(p, d.nameKey) + " [" + d.id + "]";
            list.makeItem = () => new Label(); list.bindItem = (row, i) => ((Label)row).text = Caption(matches[i]);
            list.selectionChanged += selected => { var d = selected.OfType<T>().FirstOrDefault(); if (d == null) return; changed(d.id); picker.text = label + ": " + Caption(d); picker.value = false; };
            search.RegisterValueChangedCallback(e => { list.ClearSelection(); matches.Clear(); matches.AddRange(entries.Where(x => Caption(x).IndexOf(e.newValue, StringComparison.OrdinalIgnoreCase) >= 0)); list.RefreshItems(); });
            picker.Add(list);
            if (optional) picker.Add(Button("Clear / default", () => { changed(""); picker.text = label + ": default"; picker.value = false; }));
            parent.Add(picker);
        }
        private static void Float(VisualElement parent, string label, float value, Action<float> changed)
        {
            var field = new FloatField(StudioText.T(label)) { value = value }; field.RegisterValueChangedCallback(e => changed(e.newValue)); parent.Add(field);
        }
        private void DrawActor(VisualElement parent, ProjectDefinition p, ActorDefinition actor)
        {
            SelectReference(parent, "Character / relationship", new Definition[] { p.character }.Concat(p.npcs), actor.subjectId, x => actor.subjectId = x, true);
            SelectReference(parent, "Appearance profile", p.appearanceProfiles, actor.profileId, x => actor.profileId = x);
            Number(parent, "Starting age (NPC)", actor.startingAge, x => actor.startingAge = x);
            Number(parent, "Days since birthday at start", actor.birthdayOffset, x => actor.birthdayOffset = x);
            Toggle(parent, "Fixed NPC age", actor.fixedAge, x => actor.fixedAge = x);
            parent.Add(new Label(StudioText.T("A protagonist-linked actor uses the game's protagonist age. Local conditions are independent of global stats.")));
            var rows = new VisualElement(); parent.Add(rows);
            void Draw(StatDefinition condition)
            {
                var card = new Foldout { text = StudioText.T(condition.id )}; rows.Add(card);
                Text(card, "Name key", condition.nameKey, x => condition.nameKey = x);
                Number(card, "Minimum", condition.minimum, x => condition.minimum = x);
                Number(card, "Maximum", condition.maximum, x => condition.maximum = x);
                Number(card, "Initial", condition.initial, x => condition.initial = x);
                card.Add(Button("Remove", () => { actor.conditions.Remove(condition); card.RemoveFromHierarchy(); }));
            }
            foreach (var condition in actor.conditions) Draw(condition);
            parent.Add(Button("+ Local condition", () => { var c = new StatDefinition { id = actor.id + ".condition-" + Guid.NewGuid().ToString("N").Substring(0, 6), maximum = 100 }; AuthoringService.GenerateKeys(c, "actor"); actor.conditions.Add(c); Draw(c); }));
        }
        private void DrawSlots(VisualElement parent, ProjectDefinition p, AppearanceSlots slots)
        {
            var keys = new HashSet<string>(p.localizedAssets.Select(x => x.key));
            foreach (var profile in p.appearanceProfiles) { keys.Add(profile.slots.body); keys.Add(profile.slots.hair); keys.Add(profile.slots.outfit); keys.Add(profile.slots.expression); }
            if (screenSkin != null) foreach (var binding in screenSkin.sprites) keys.Add(binding.key);
            var entries = keys.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x).Select(x => new ItemDefinition { id = x, nameKey = x }).ToList();
            SelectReference(parent, "Body / portrait", entries, slots.body, x => slots.body = x, true);
            SelectReference(parent, "Hair", entries, slots.hair, x => slots.hair = x, true);
            SelectReference(parent, "Outfit", entries, slots.outfit, x => slots.outfit = x, true);
            SelectReference(parent, "Expression", entries, slots.expression, x => slots.expression = x, true);
        }
        private void DrawEventPresentation(VisualElement parent, ProjectDefinition p, EventDefinition e)
        {
            var foldout = new Foldout { text = StudioText.T("Event sequence · dialogue / images / choices"), value = false }; parent.Add(foldout);
            foldout.Add(new HelpBox(StudioText.T("The first step is the entry. Empty next = list order. End event skips terminal choices. Each stage channel independently replaces, keeps or clears its previous content."), HelpBoxMessageType.Info));
            var rows = new VisualElement(); foldout.Add(rows);
            void Rebuild()
            {
                rows.Clear();
                foreach (var step in e.presentation.ToArray())
                {
                    var card = new Foldout { text = StudioText.T(step.id), value = false }; rows.Add(card);
                    EnumValue(card, "Step kind", step.kind, x => { step.kind = x; Rebuild(); });
                    Text(card, "Dialogue localization key", step.nameKey, x => step.nameKey = x);
                    RaiseArc.Editor.RaiseArcVoiceField.Draw(card, p, step, p.defaultLocale, () => { });
                    DrawNextStep(card, e, step, step.nextStepId, x => step.nextStepId = x);
                    SelectReference(card, "Speaker", p.actors, step.speakerActorId, x => step.speakerActorId = x, true);
                    EnumValue(card, "Background", step.backgroundChange, x => step.backgroundChange = x);
                    EnumValue(card, "Actors", step.actorsChange, x => step.actorsChange = x);
                    EnumValue(card, "Images", step.imagesChange, x => step.imagesChange = x);
                    var assets = screenSkin == null ? new List<ItemDefinition>() : screenSkin.sprites.Select(x => x.key).Distinct().Select(x => new ItemDefinition { id = x, nameKey = x }).ToList();
                    SelectReference(card, "Background", assets, step.backgroundKey, x => step.backgroundKey = x, true);
                    foreach (var placement in step.actors.ToArray())
                    {
                        var actorCard = new Foldout { text = StudioText.T("Visible actor / " + placement.actorId )}; card.Add(actorCard);
                        SelectReference(actorCard, "Actor", p.actors, placement.actorId, x => placement.actorId = x);
                        SelectReference(actorCard, "Stage slot", p.stageSlots, placement.slotId, x => placement.slotId = x);
                        DrawSlots(actorCard, p, placement.overrides);
                        actorCard.Add(Button("Remove actor", () => { step.actors.Remove(placement); Rebuild(); }));
                    }
                    card.Add(Button("+ Visible actor", () => { if (p.actors.Count == 0 || p.stageSlots.Count == 0) { Notify("Create actors and stage slots first."); return; } step.actorsChange = StageChange.Replace; step.actors.Add(new ActorPlacement { actorId = p.actors[0].id, slotId = p.stageSlots[0].id }); Rebuild(); }));
                    DrawSequenceImages(card, step, assets, Rebuild);
                    if (step.kind == PresentationStepKind.Choice) DrawSequenceChoices(card, p, e, step, Rebuild);
                    card.Add(Button("Move earlier", () => { var i = e.presentation.IndexOf(step); if (i > 0) { e.presentation.RemoveAt(i); e.presentation.Insert(i - 1, step); Rebuild(); } }));
                    card.Add(Button("Remove step", () => { e.presentation.Remove(step); Rebuild(); }));
                }
            }
            Rebuild();
            foreach (PresentationStepKind kind in Enum.GetValues(typeof(PresentationStepKind)))
                foldout.Add(Button("+ " + kind + " step", () => { var step = new PresentationStep { id = e.id + ".step-" + Guid.NewGuid().ToString("N").Substring(0, 6), kind = kind, backgroundChange = StageChange.Keep, actorsChange = StageChange.Keep, imagesChange = StageChange.Keep }; AuthoringService.GenerateKeys(step, "dialogue"); e.presentation.Add(step); Rebuild(); }));
        }
    }
}
