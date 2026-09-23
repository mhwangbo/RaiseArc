using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public sealed partial class StudioWindow
    {
        [SerializeField] private string entryLocale = "";
        [SerializeField] private string activityCategoryFilter = "";
        private List<Definition> Definitions(ProjectDefinition p)
        {
            switch (page)
            {
                case "Actors": return p.actors.Cast<Definition>().ToList();
                case "Appearance profiles": return p.appearanceProfiles.Cast<Definition>().ToList();
                case "Appearance rules": return p.appearanceRules.Cast<Definition>().ToList();
                case "Stage slots": return p.stageSlots.Cast<Definition>().ToList();
                case "Character":
                    return new List<Definition> { p.character };
                case "Stats & states":
                    return p.stats.Cast<Definition>().ToList();
                case "Activities":
                    return p.activities.Cast<Definition>().ToList();
                case "Schedule":
                    return p.schedules.Cast<Definition>().ToList();
                case "Events":
                    return p.events.Cast<Definition>().ToList();
                case "Relationships":
                    return p.npcs.Cast<Definition>().ToList();
                case "Items & flags":
                    return p.items.Cast<Definition>().Concat(p.flags).ToList();
                case "Timed modifiers": return p.modifiers.Cast<Definition>().ToList();
                case "Endings":
                    return p.endings.Cast<Definition>().ToList();
                case "Growth":
                    return p.growth.Cast<Definition>().ToList();
                case "Modules":
                    return p.modules.Cast<Definition>().ToList();
                default:
                    return new List<Definition>();
            }
        }
        private void DrawCatalog(ProjectDefinition p)
        {
            if (page == "Actors") content.Add(Button("Import character / NPCs (keeps existing growth)", () =>
            {
                var backup = AssetDatabase.GenerateUniqueAssetPath(AssetDatabase.GetAssetPath(asset) + ".before-presentation.json");
                System.IO.File.WriteAllText(backup, codec.ToJson(p));
                AssetDatabase.ImportAsset(backup);
                Mutate(() => service.ImportLegacyActors(service.Revision));
            }));
            var split = new VisualElement();
            split.AddToClassList("split");
            content.Add(split);
            var catalog = new VisualElement();
            catalog.AddToClassList("catalog");
            split.Add(catalog);
            var definitions = Definitions(p);
            var search = new ToolbarSearchField();
            catalog.Add(search);
            var list = new ListView { itemsSource = definitions, fixedItemHeight = 48, selectionType = SelectionType.Single };
            list.style.flexGrow = 1;
            list.makeItem = () => { var row = new VisualElement(); row.AddToClassList("entry"); row.Add(StyledLabel("", "entry-name")); row.Add(StyledLabel("", "entry-id")); return row; };
            list.bindItem = (element, index) => { var d = (Definition)list.itemsSource[index]; element.Q<Label>(className: "entry-name").text = PreviewText(p, d.nameKey); element.Q<Label>(className: "entry-id").text = d.id; };
            var details = new ScrollView();
            details.AddToClassList("detail");
            split.Add(details);
            if (page == "Activities")
            {
                string Category(ActivityDefinition a) => string.IsNullOrEmpty(a.categoryId) ? a.category.ToString() : a.categoryId;
                var categories = new List<string> { "" }; categories.AddRange(p.activities.Select(Category).Distinct().OrderBy(x => x));
                if (!categories.Contains(activityCategoryFilter)) activityCategoryFilter = "";
                var category = new PopupField<string>(StudioText.T("Category"), categories, categories.IndexOf(activityCategoryFilter),
                    x => string.IsNullOrEmpty(x) ? StudioText.T("All categories") : StudioText.T(x),
                    x => string.IsNullOrEmpty(x) ? StudioText.T("All categories") : StudioText.T(x));
                category.RegisterValueChangedCallback(e => { activityCategoryFilter = e.newValue; Filter(); });
                catalog.Add(category);
            }
            void Select(Definition d)
            {
                details.Clear();
                if (d == null) return;
                selectedId = d.id;
                DrawDefinition(details, p, d);
            }
            list.selectionChanged += items => Select(items.FirstOrDefault() as Definition);
            if (page == "Activities")
                catalog.Add(new Button(() =>
                {
                    if (hasUnsavedChanges) { Notify(StudioText.Language == "ko" ? "변경을 적용하거나 버린 뒤 복제하세요." : "Apply or discard your changes before duplicating."); return; }
                    Mutate(() => selectedId = RaiseArc.Authoring.ActivityCopy.Duplicate(service, selectedId, service.Revision));
                }) { text = StudioText.Language == "ko" ? "선택한 활동 복제" : "Duplicate selected activity" });
            void Filter()
            {
                var filtered = definitions.Where(x =>
                    (page != "Activities" || activityCategoryFilter.Length == 0 || x is ActivityDefinition a &&
                     (string.IsNullOrEmpty(a.categoryId) ? a.category.ToString() : a.categoryId) == activityCategoryFilter) &&
                    (x.id.IndexOf(search.value ?? "", StringComparison.OrdinalIgnoreCase) >= 0 || PreviewText(p, x.nameKey).IndexOf(search.value ?? "", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                list.itemsSource = filtered; list.Rebuild();
                if (filtered.Count == 0) { details.Clear(); return; }
                var selected = Math.Max(0, filtered.FindIndex(x => x.id == selectedId));
                list.SetSelectionWithoutNotify(new[] { selected }); Select(filtered[selected]);
            }
            search.RegisterValueChangedCallback(_ => Filter());
            catalog.Add(list);
            if (page != "Character")
                catalog.Add(Button("+ Add " + page, () => { activityCategoryFilter = ""; AddDefinition(p); }));
            if (page == "Items & flags")
                catalog.Add(Button("+ Add flag", () => { var d = new FlagDefinition { id = "flag-" + Guid.NewGuid().ToString("N").Substring(0, 8) }; AuthoringService.GenerateKeys(d, "flag"); p.flags.Add(d); selectedId = d.id; Mutate(() => service.Replace(p, service.Revision)); }));
            if (definitions.Count > 0)
            {
                Filter();
            }
            else
                details.Add(StyledLabel("Create your first entry to begin.", "subtitle"));
        }
        private void AddDefinition(ProjectDefinition p)
        {
            var id = page.Split(' ')[0].ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Definition d;
            switch (page)
            {
                case "Actors":
                    if (p.appearanceProfiles.Count == 0) { Notify("Create an appearance profile first."); return; }
                    var actor = new ActorDefinition { id = id, profileId = p.appearanceProfiles[0].id }; p.actors.Add(actor); d = actor; break;
                case "Appearance profiles":
                    var profile = new AppearanceProfile { id = id, slots = new AppearanceSlots { body = "appearance." + id + ".body" } }; p.appearanceProfiles.Add(profile); d = profile; break;
                case "Appearance rules":
                    if (p.actors.Count == 0) { Notify("Create an actor first."); return; }
                    var rule = new AppearanceRule { id = id, actorId = p.actors[0].id }; p.appearanceRules.Add(rule); d = rule; break;
                case "Stage slots":
                    var slot = new StageSlot { id = id }; p.stageSlots.Add(slot); d = slot; break;
                case "Stats & states":
                    var s = new StatDefinition { id = id };
                    p.stats.Add(s);
                    d = s;
                    break;
                case "Activities":
                    var a = new ActivityDefinition { id = id };
                    p.activities.Add(a);
                    d = a;
                    break;
                case "Schedule":
                    var sc = new ScheduleDefinition { id = id };
                    p.schedules.Add(sc);
                    d = sc;
                    break;
                case "Events":
                    var ev = new EventDefinition { id = id };
                    p.events.Add(ev);
                    d = ev;
                    break;
                case "Relationships":
                    var n = new NpcDefinition { id = id };
                    p.npcs.Add(n);
                    d = n;
                    break;
                case "Items & flags":
                    var i = new ItemDefinition { id = id };
                    p.items.Add(i);
                    d = i;
                    break;
                case "Timed modifiers":
                    if (p.stats.Count == 0) { Notify("Create a stat first."); return; }
                    var modifier = new RaiseArc.Core.TimedModifierDefinition { id = id, statId = p.stats[0].id };
                    p.modifiers.Add(modifier); d = modifier; break;
                case "Endings":
                    var en = new EndingDefinition { id = id };
                    p.endings.Add(en);
                    d = en;
                    break;
                case "Growth":
                    var g = new GrowthDefinition { id = id, appearanceKey = "appearance." + id };
                    p.growth.Add(g);
                    d = g;
                    break;
                case "Modules":
                    var m = new ModuleDefinition { id = id };
                    p.modules.Add(m);
                    d = m;
                    break;
                default:
                    return;
            }
            AuthoringService.GenerateKeys(d, "content");
            selectedId = id;
            Mutate(() => service.Replace(p, service.Revision));
        }
        private void DrawDefinition(VisualElement parent, ProjectDefinition p, Definition d)
        {
            parent.Add(StyledLabel(PreviewText(p, d.nameKey), "card-title"));
            var refreshText = DrawEntryText(parent, p, d);
            var identity = new Foldout { text = StudioText.T("IDs and localization keys"), value = false };
            parent.Add(identity);
            Text(identity, "Stable ID", d.id, x => d.id = x);
            Text(identity, "Name key", d.nameKey, x => { d.nameKey = x; refreshText(); });
            Text(identity, "Description key", d.descriptionKey, x => { d.descriptionKey = x; refreshText(); });
            switch (d)
            {
                case ActorDefinition actor: DrawActor(parent, p, actor); break;
                case AppearanceProfile profile: DrawSlots(parent, p, profile.slots); break;
                case AppearanceRule rule:
                    SelectReference(parent, "Actor", p.actors, rule.actorId, x => rule.actorId = x);
                    Number(parent, "Priority", rule.priority, x => rule.priority = x);
                    Float(parent, "Size multiplier (0 = inherit, 1 = original)", rule.scale, x => rule.scale = x);
                    parent.Add(new HelpBox(StudioText.T("Scales all actor layers together around the body image's bottom. Higher priority wins; 0 keeps the previous size."), HelpBoxMessageType.Info));
                    DrawConditions(parent, rule.conditions); DrawSlots(parent, p, rule.slots); break;
                case StageSlot slot:
                    Float(parent, "Horizontal center (0–1)", slot.x, x => slot.x = x);
                    Float(parent, "Width (0–1)", slot.width, x => slot.width = x);
                    Float(parent, "Scale", slot.scale, x => slot.scale = x); break;
                case CharacterDefinition c:
                    Number(parent, "Starting age", c.startingAge, x => c.startingAge = x);
                    break;
                case StatDefinition s:
                    Number(parent, "Minimum", s.minimum, x => s.minimum = x);
                    Number(parent, "Maximum", s.maximum, x => s.maximum = x);
                    Number(parent, "Initial", s.initial, x => s.initial = x);
                    Toggle(parent, "Status / condition", s.isStatus, x => s.isStatus = x);
                    break;
                case ActivityDefinition a:
                    EnumValue(parent, "Category", a.category, x => a.category = x);
                    EnumValue(parent, "Success check frequency", a.checkFrequency, x => a.checkFrequency = x);
                    parent.Add(new HelpBox(StudioText.T("Project default follows Overview. Cost, income and effects apply once per check. Daily activities pause for events and resume afterwards; insufficient money or unmet entry conditions stops the remaining days."), HelpBoxMessageType.Info));
                    Number(parent, "Duration (days)", a.days, x => a.days = x);
                    Number(parent, "Cost", a.cost, x => a.cost = x);
                    Number(parent, "Income", a.income, x => a.income = x);
                    Text(parent, "Module ID", a.moduleId, x => a.moduleId = x);
                    DrawModuleDrop(parent, p, a);
                    DrawConditions(parent, a.conditions);
                    DrawEffects(parent, a.effects, "Success effects");
                    DrawEffects(parent, a.failureEffects, "Failure effects");
                    DrawActivityRules(parent, p, a);
                    DrawEvaluation(parent, p, a);
                    break;
                case ScheduleDefinition s:
                    Number(parent, "Period (7 / 30 days)", s.periodDays, x => s.periodDays = x);
                    StringList(parent, "Ordered activity IDs", s.activityIds);
                    break;
                case EventDefinition e:
                    DrawEventPresentation(parent, p, e);
                    e.triggerChance ??= new RaiseArc.Core.SuccessChance();
                    DrawSuccessChance(parent, p, e.triggerChance);
                    parent.Add(new HelpBox(StudioText.T("Event probability is checked after its entry conditions. A skipped event can be tried on the next time advance. Direct shared-flow calls bypass the trigger probability."), HelpBoxMessageType.Info));
                    Number(parent, "Priority", e.priority, x => e.priority = x);
                    Toggle(parent, "Once only", e.once, x => e.once = x);
                    DrawConditions(parent, e.conditions);
                    DrawEffects(parent, e.effects);
                    foreach (var choice in e.choices.ToArray())
                    {
                        var card = new Foldout { text = StudioText.T("Choice / " + choice.id), value = false };
                        parent.Add(card);
                        DrawEntryText(card, p, choice);
                        Text(card, "ID", choice.id, x => choice.id = x);
                        Text(card, "Text key", choice.nameKey, x => choice.nameKey = x);
                        DrawConditions(card, choice.conditions);
                        DrawEffects(card, choice.effects);
                        card.Add(Button("Remove choice", () => { e.choices.Remove(choice); card.RemoveFromHierarchy(); }));
                    }
                    parent.Add(Button("+ Choice", () => { var choice = new ChoiceDefinition { id = e.id + ".choice-" + Guid.NewGuid().ToString("N").Substring(0, 6) }; AuthoringService.GenerateKeys(choice, "choice"); e.choices.Add(choice); parent.Clear(); DrawDefinition(parent, p, d); }));
                    break;
                case NpcDefinition n:
                    Number(parent, "Minimum", n.minimum, x => n.minimum = x);
                    Number(parent, "Maximum", n.maximum, x => n.maximum = x);
                    Number(parent, "Initial", n.initial, x => n.initial = x);
                    foreach (var stage in n.stages)
                    {
                        var row = Row();
                        Number(row, "Threshold", stage.minimum, x => stage.minimum = x);
                        Text(row, "Stage key", stage.nameKey, x => stage.nameKey = x);
                        parent.Add(row);
                    }
                    parent.Add(Button("+ Relationship stage", () => { n.stages.Add(new RelationshipStage { nameKey = "npc." + n.id + ".stage." + n.stages.Count }); parent.Clear(); DrawDefinition(parent, p, d); }));
                    break;
                case ItemDefinition i:
                    Number(parent, "Maximum stack", i.maximum, x => i.maximum = x);
                    break;
                case FlagDefinition f:
                    Toggle(parent, "Initially enabled", f.initial, x => f.initial = x);
                    Toggle(parent, "Permanent qualification", f.permanent, x => f.permanent = x);
                    break;
                case RaiseArc.Core.TimedModifierDefinition modifier:
                    DrawModifier(parent, p, modifier);
                    break;
                case EndingDefinition e:
                    Number(parent, "Priority", e.priority, x => e.priority = x);
                    DrawConditions(parent, e.conditions);
                    parent.Add(Button(StudioText.Language == "ko" ? "저장된 엔딩으로 가는 경로 찾기" : "Find a path to the saved ending", () => RaiseArc.Editor.EndingTests.Open(asset, e.id)));
                    break;
                case GrowthDefinition g:
                    Number(parent, "Minimum age", g.minimumAge, x => g.minimumAge = x);
                    Number(parent, "Priority", g.priority, x => g.priority = x);
                    Text(parent, "Appearance asset key", g.appearanceKey, x => g.appearanceKey = x);
                    DrawConditions(parent, g.conditions);
                    break;
                case ModuleDefinition m:
                    DrawModule(parent, p, m);
                    break;
            }
            parent.Add(Button("Apply changes", () =>
            {
                if (codec.ToJson(p) == codec.ToJson(service.Snapshot()))
                {
                    Notify("No changes to save.");
                    return;
                }
                selectedId = d.id;
                if (d is ActivityDefinition changedActivity && activityCategoryFilter.Length > 0)
                    activityCategoryFilter = string.IsNullOrEmpty(changedActivity.categoryId) ? changedActivity.category.ToString() : changedActivity.categoryId;
                Mutate(() => service.Replace(p, service.Revision));
            }, true));
            if (!(d is CharacterDefinition))
                parent.Add(Button("Delete entry", () =>
                {
                    p.stats.Remove(d as StatDefinition);
                    p.actors.Remove(d as ActorDefinition);
                    p.appearanceProfiles.Remove(d as AppearanceProfile);
                    p.appearanceRules.Remove(d as AppearanceRule);
                    p.stageSlots.Remove(d as StageSlot);
                    p.activities.Remove(d as ActivityDefinition);
                    p.schedules.Remove(d as ScheduleDefinition);
                    p.events.Remove(d as EventDefinition);
                    p.npcs.Remove(d as NpcDefinition);
                    p.items.Remove(d as ItemDefinition);
                    p.flags.Remove(d as FlagDefinition);
                    p.modifiers.Remove(d as RaiseArc.Core.TimedModifierDefinition);
                    p.endings.Remove(d as EndingDefinition);
                    p.growth.Remove(d as GrowthDefinition);
                    p.modules.Remove(d as ModuleDefinition);
                    Mutate(() => service.Replace(p, service.Revision));
                }));
        }
        private Action DrawEntryText(VisualElement parent, ProjectDefinition p, Definition d)
        {
            if (!p.locales.Contains(entryLocale)) entryLocale = p.defaultLocale;
            var language = new PopupField<string>(StudioText.T("Content language"), p.locales, Math.Max(0, p.locales.IndexOf(entryLocale))) { name = "entry-locale" };
            parent.Add(language);
            var name = new TextField(StudioText.T("Name")) { name = "entry-display-name" };
            var description = new TextField(StudioText.T("Description")) { name = "entry-description", multiline = true };
            var draft = new Toggle(StudioText.T("Draft / review required"));
            parent.Add(name); parent.Add(description); parent.Add(draft);
            TranslationEntry Find(string key) => p.translations.Find(t => t.key == key && t.locale == language.value);
            void Refresh()
            {
                name.SetValueWithoutNotify(Find(d.nameKey)?.text ?? "");
                description.SetValueWithoutNotify(Find(d.descriptionKey)?.text ?? "");
                draft.SetValueWithoutNotify((Find(d.nameKey)?.draft ?? true) || (Find(d.descriptionKey)?.draft ?? false));
            }
            void Write(bool isName, string text)
            {
                if (isName && string.IsNullOrEmpty(d.nameKey)) d.nameKey = "content." + d.id + ".name";
                if (!isName && string.IsNullOrEmpty(d.descriptionKey)) d.descriptionKey = "content." + d.id + ".description";
                var key = isName ? d.nameKey : d.descriptionKey;
                var entry = Find(key);
                if (entry == null) { entry = new TranslationEntry { key = key, locale = language.value }; p.translations.Add(entry); }
                entry.text = text; entry.draft = true;
                draft.SetValueWithoutNotify(true);
            }
            name.RegisterValueChangedCallback(e => Write(true, e.newValue));
            description.RegisterValueChangedCallback(e => Write(false, e.newValue));
            draft.RegisterValueChangedCallback(e => { foreach (var key in new[] { d.nameKey, d.descriptionKey }) { var entry = Find(key); if (entry != null) entry.draft = e.newValue; } });
            language.RegisterValueChangedCallback(e => { entryLocale = e.newValue; Refresh(); });
            Refresh();
            parent.Add(new HelpBox(StudioText.T("Name and description use Localization. Apply changes to save; sync Unity tables before playing."), HelpBoxMessageType.Info));
            return Refresh;
        }
        private void DrawRuleTarget(VisualElement parent, ConditionSpec c)
        {
            if (c.kind == ValueKind.Custom)
                RaiseArc.Editor.RaiseArcExtensionFields.Draw(parent, service.Snapshot(), asset.CreateExtensions(), true, c.target, x => c.target = x, c.parameters, null);
            else DrawScopedTarget(parent, c.kind, c.actorId, c.target, x => c.actorId = x, x => c.target = x);
        }
        private void DrawRuleTarget(VisualElement parent, EffectSpec e)
        {
            if (e.kind == ValueKind.Custom)
                RaiseArc.Editor.RaiseArcExtensionFields.Draw(parent, service.Snapshot(), asset.CreateExtensions(), false, e.target, x => e.target = x, e.parameters, null);
            else DrawScopedTarget(parent, e.kind, e.actorId, e.target, x => e.actorId = x, x => e.target = x);
        }
        private void DrawConditions(VisualElement parent, List<ConditionSpec> conditions)
        {
            var foldout = new Foldout { text = StudioText.T("Conditions • all groups must match"), value = true };
            parent.Add(foldout);
            var rows = new VisualElement();
            foldout.Add(rows);
            void Draw(ConditionSpec c)
            {
                var card = new VisualElement();
                card.AddToClassList("card");
                var target = new VisualElement();
                EnumValue(card, "Value", c.kind, x => { c.kind = x; c.target = ""; DrawRuleTarget(target, c); });
                card.Add(target);
                DrawRuleTarget(target, c);
                EnumValue(card, "Comparison", c.comparison, x => c.comparison = x);
                Number(card, "Threshold", c.value, x => c.value = x);
                Text(card, "OR group (optional)", c.anyGroup, x => c.anyGroup = x);
                card.Add(new HelpBox(StudioText.T("Empty OR group: required condition. Same group name: at least one alternative must match."), HelpBoxMessageType.Info));
                card.Add(Button("Remove", () => { conditions.Remove(c); card.RemoveFromHierarchy(); }));
                rows.Add(card);
            }
            foreach (var c in conditions)
                Draw(c);
            foldout.Add(Button("+ Condition", () => { var c = new ConditionSpec { kind = ValueKind.Day }; conditions.Add(c); Draw(c); }));
        }
        private void DrawEffects(VisualElement parent, List<EffectSpec> effects, string title = "Effects • applied in order")
        {
            var foldout = new Foldout { text = StudioText.T(title), value = true };
            parent.Add(foldout);
            var rows = new VisualElement();
            foldout.Add(rows);
            void Draw(EffectSpec e)
            {
                var card = new VisualElement();
                card.AddToClassList("card");
                var target = new VisualElement();
                EnumValue(card, "Value", e.kind, x => { e.kind = x; e.target = ""; DrawRuleTarget(target, e); });
                card.Add(target);
                DrawRuleTarget(target, e);
                EnumValue(card, "Operation", e.operation, x => e.operation = x);
                var fixedAmount = new VisualElement(); card.Add(fixedAmount);
                Number(fixedAmount, "Amount", e.value, x => e.value = x);
                fixedAmount.SetEnabled(!e.randomRange);
                var range = new VisualElement();
                Toggle(card, "Random integer range", e.randomRange, x => { e.randomRange = x; range.SetEnabled(x); fixedAmount.SetEnabled(!x); });
                Number(range, "Minimum amount (inclusive)", e.minimumValue, x => e.minimumValue = x);
                Number(range, "Maximum amount (inclusive)", e.maximumValue, x => e.maximumValue = x);
                range.SetEnabled(e.randomRange); card.Add(range);
                card.Add(Button("Remove", () => { effects.Remove(e); card.RemoveFromHierarchy(); }));
                rows.Add(card);
            }
            foreach (var e in effects)
                Draw(e);
            foldout.Add(Button("+ Effect", () => { var e = new EffectSpec { kind = ValueKind.Money }; effects.Add(e); Draw(e); }));
        }
        private void DrawModule(VisualElement parent, ProjectDefinition p, ModuleDefinition m)
        {
            Text(parent, "Scene path", m.scenePath, x => m.scenePath = x);
            var scene = new UnityEditor.UIElements.ObjectField(StudioText.T("Scene")) { objectType = typeof(SceneAsset), value = AssetDatabase.LoadAssetAtPath<SceneAsset>(m.scenePath) };
            scene.RegisterValueChangedCallback(e => m.scenePath = AssetDatabase.GetAssetPath(e.newValue));
            parent.Add(scene);
            Number(parent, "Maximum elapsed days", m.maximumDays, x => m.maximumDays = x);
            Number(parent, "Maximum total per value", m.maximumAbsoluteDelta, x => m.maximumAbsoluteDelta = x);
            StringList(parent, "Allowed stats", m.allowedStats);
            StringList(parent, "Allowed items", m.allowedItems);
            StringList(parent, "Allowed NPCs", m.allowedNpcs);
            StringList(parent, "Allowed flags", m.allowedFlags);
            foreach (var encounter in m.encounters.ToArray())
            {
                var card = new Foldout { text = StudioText.T("Encounter / " + encounter.id), value = false };
                parent.Add(card);
                Text(card, "ID", encounter.id, x => encounter.id = x);
                Text(card, "Name key", encounter.nameKey, x => encounter.nameKey = x);
                Text(card, "Map / event text key", encounter.descriptionKey, x => encounter.descriptionKey = x);
                Number(card, "Difficulty", encounter.difficulty, x => encounter.difficulty = x);
                DrawEffects(card, encounter.rewards);
                card.Add(Button("Remove encounter", () => { m.encounters.Remove(encounter); card.RemoveFromHierarchy(); }));
            }
            parent.Add(Button("+ Encounter / reward", () => { var e = new ModuleEncounter { id = m.id + ".encounter-" + Guid.NewGuid().ToString("N").Substring(0, 6) }; AuthoringService.GenerateKeys(e, "encounter"); m.encounters.Add(e); parent.Clear(); DrawDefinition(parent, p, m); }));
            foreach (var location in m.locations.ToArray())
            {
                var card = new Foldout { text = StudioText.T("Map location / " + location.id), value = false };
                parent.Add(card);
                Text(card, "ID", location.id, x => location.id = x);
                Text(card, "Name key", location.nameKey, x => location.nameKey = x);
                Number(card, "Map X", location.x, x => location.x = x);
                Number(card, "Map Y", location.y, x => location.y = x);
                StringList(card, "Connections", location.connections);
                StringList(card, "Encounter IDs", location.encounterIds);
                StringList(card, "Enemy IDs", location.enemyIds);
                card.Add(Button("Remove location", () => { m.locations.Remove(location); card.RemoveFromHierarchy(); }));
            }
            parent.Add(Button("+ Map location", () => { var l = new ModuleLocation { id = m.id + ".location-" + Guid.NewGuid().ToString("N").Substring(0, 6) }; AuthoringService.GenerateKeys(l, "location"); m.locations.Add(l); parent.Clear(); DrawDefinition(parent, p, m); }));
            foreach (var enemy in m.enemies.ToArray())
            {
                var card = new Foldout { text = StudioText.T("Enemy / " + enemy.id), value = false };
                parent.Add(card);
                Text(card, "ID", enemy.id, x => enemy.id = x);
                Text(card, "Name key", enemy.nameKey, x => enemy.nameKey = x);
                Number(card, "Health", enemy.health, x => enemy.health = x);
                Number(card, "Attack", enemy.attack, x => enemy.attack = x);
                DrawEffects(card, enemy.rewards);
                card.Add(Button("Remove enemy", () => { m.enemies.Remove(enemy); card.RemoveFromHierarchy(); }));
            }
            parent.Add(Button("+ Enemy", () => { var e = new ModuleEnemy { id = m.id + ".enemy-" + Guid.NewGuid().ToString("N").Substring(0, 6) }; AuthoringService.GenerateKeys(e, "enemy"); m.enemies.Add(e); parent.Clear(); DrawDefinition(parent, p, m); }));
        }
        private void DrawModuleDrop(VisualElement parent, ProjectDefinition p, ActivityDefinition activity)
        {
            var palette = Row();
            foreach (var module in p.modules)
            {
                var source = StyledLabel("↗ " + module.id, "card");
                source.RegisterCallback<MouseDownEvent>(e =>
                {
                    if (e.button != 0)
                        return;
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData("PrincessStudio.Module", module.id);
                    DragAndDrop.StartDrag(module.id);
                    e.StopPropagation();
                });
                palette.Add(source);
            }
            parent.Add(palette);
            var target = StyledLabel("Drop a module here → " + (activity.moduleId.Length > 0 ? activity.moduleId : "not connected"), "drop-target");
            parent.Add(target);
            target.RegisterCallback<DragUpdatedEvent>(e => { if (DragAndDrop.GetGenericData("PrincessStudio.Module") is string) DragAndDrop.visualMode = DragAndDropVisualMode.Link; });
            target.RegisterCallback<DragPerformEvent>(e =>
            {
                if (DragAndDrop.GetGenericData("PrincessStudio.Module") is string id && p.modules.Exists(m => m.id == id))
                {
                    DragAndDrop.AcceptDrag();
                    activity.moduleId = id;
                    activity.category = ActivityKind.GameMode;
                    target.text = "Connected → " + id;
                    DragAndDrop.SetGenericData("PrincessStudio.Module", null);
                    e.StopPropagation();
                }
            });
        }
    }
}
