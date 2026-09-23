using System;
using System.Linq;
using PrincessStudio.Core;
using RaiseArc.Core;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public sealed partial class StudioWindow
    {
        private void DrawActivityRules(VisualElement parent, ProjectDefinition project, ActivityDefinition activity)
        {
            Text(parent, "Custom category ID (blank uses Category)", activity.categoryId, x => activity.categoryId = x);
            Text(parent, "Activity image localization key", activity.imageKey, x => activity.imageKey = x);
            activity.successChance ??= new SuccessChance();
            activity.progression ??= new ActivityProgression();
            DrawSuccessChance(parent, project, activity.successChance);
            parent.Add(new HelpBox(StudioText.T("Entry conditions decide whether an activity can start. Success chance = base + positive stat excess × bonus + level bonus, clamped to 0–100%. Failure still consumes time and cost; income and success effects are skipped."), HelpBoxMessageType.Info));

            var progression = activity.progression;
            var levels = new Foldout { text = StudioText.T("Activity progression"), value = false }; parent.Add(levels);
            Toggle(levels, "Track activity experience", progression.enabled, x => progression.enabled = x);
            Number(levels, "Experience per success", progression.experienceOnSuccess, x => progression.experienceOnSuccess = x);
            Number(levels, "Experience per failure", progression.experienceOnFailure, x => progression.experienceOnFailure = x);
            levels.Add(new HelpBox(StudioText.T("The last eligible level in this list applies. Experience and conditions must both match. Use 0 experience for stat-only unlocks, or no conditions for experience-only growth. Level effects are added to successful activity effects; new experience affects the next check."), HelpBoxMessageType.Info));
            var entries = new VisualElement(); levels.Add(entries);
            void DrawLevels()
            {
                entries.Clear();
                foreach (var level in progression.levels.ToArray())
                {
                    var card = new Foldout { text = PreviewText(project, level.nameKey), value = true }; entries.Add(card);
                    DrawEntryText(card, project, level);
                    Number(card, "Required experience", level.requiredExperience, x => level.requiredExperience = x);
                    Number(card, "Success bonus (%)", level.successBonusPercent, x => level.successBonusPercent = x);
                    DrawConditions(card, level.conditions);
                    DrawEffects(card, level.effects, "Additional success effects");
                    var buttons = Row(); card.Add(buttons);
                    buttons.Add(Button("Move up", () => { var i = progression.levels.IndexOf(level); if (i > 0) { progression.levels.RemoveAt(i); progression.levels.Insert(i - 1, level); DrawLevels(); } }));
                    buttons.Add(Button("Move down", () => { var i = progression.levels.IndexOf(level); if (i < progression.levels.Count - 1) { progression.levels.RemoveAt(i); progression.levels.Insert(i + 1, level); DrawLevels(); } }));
                    buttons.Add(Button("Remove", () => { progression.levels.Remove(level); DrawLevels(); }));
                }
            }
            DrawLevels();
            levels.Add(Button("+ Activity level", () =>
            {
                var level = new ActivityLevel { id = activity.id + ".level-" + Guid.NewGuid().ToString("N").Substring(0, 8) };
                AuthoringService.GenerateKeys(level, "activity-level"); progression.levels.Add(level); DrawLevels();
            }));
            var references = new Foldout { text = StudioText.T("Activity references (saved content)"), value = false }; parent.Add(references);
            var index = new ContentIndex(project, extensions: asset.CreateExtensions(project));
            var incoming = index.References(activity.id);
            foreach (var reference in incoming)
            {
                var owner = reference.sourceId;
                while (owner.Length > 0 && !(index.Find(owner) is Definition)) owner = index.Owner(owner);
                var definition = index.Find(owner) as Definition;
                references.Add(new Label(definition == null ? reference.sourceId : PreviewText(project, definition.nameKey)) { tooltip = reference.sourcePropertyPath });
            }
            if (incoming.Count == 0) references.Add(new Label(StudioText.T("No incoming references.")));
        }
        private void DrawSuccessChance(VisualElement parent, ProjectDefinition project, SuccessChance chance)
        {
            var success = new Foldout { text = StudioText.T("Success probability"), value = true };
            parent.Add(success);
            Toggle(success, "Use success probability", chance.enabled, x => chance.enabled = x);
            Number(success, "Base success (%)", chance.basePercent, x => chance.basePercent = x);
            var bonuses = new VisualElement(); success.Add(bonuses);
            void DrawBonus(StatChanceBonus bonus)
            {
                var row = new VisualElement(); row.AddToClassList("card"); bonuses.Add(row);
                SelectReference(row, "Stat", project.stats, bonus.statId, x => bonus.statId = x);
                Number(row, "Bonus starts above", bonus.threshold, x => bonus.threshold = x);
                Number(row, "Percentage points per stat point", bonus.percentPerPoint, x => bonus.percentPerPoint = x);
                row.Add(Button("Remove", () => { chance.statBonuses.Remove(bonus); row.RemoveFromHierarchy(); }));
            }
            foreach (var bonus in chance.statBonuses) DrawBonus(bonus);
            success.Add(Button("+ Stat bonus", () => { var bonus = new StatChanceBonus { statId = project.stats.FirstOrDefault()?.id ?? "" }; chance.statBonuses.Add(bonus); DrawBonus(bonus); }));
        }
    }
}
