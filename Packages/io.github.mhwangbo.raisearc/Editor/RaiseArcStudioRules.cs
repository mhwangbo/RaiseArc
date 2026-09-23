using System.Linq;
using PrincessStudio.Core;
using RaiseArc.Core;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public sealed partial class StudioWindow
    {
        private void DrawRuleState(VisualElement parent, ProjectDefinition project, StateSnapshot state)
        {
            foreach (var activity in project.activities.Where(a => a.evaluation?.enabled == true))
                parent.Add(new Label(PreviewText(project, activity.nameKey) + " · " + (StudioText.Language == "ko" ? "최고 / 합계 / 시도 / 통과: " : "best / total / attempts / passes: ") +
                    state.Read(ValueKind.RecordBest, activity.id) + " / " + state.Read(ValueKind.RecordTotal, activity.id) + " / " +
                    state.Read(ValueKind.RecordAttempts, activity.id) + " / " + state.Read(ValueKind.RecordPasses, activity.id)));
            foreach (var flag in project.flags.Where(f => f.permanent && state.Flags[f.id] == 1))
                parent.Add(new Label((StudioText.Language == "ko" ? "취득 자격: " : "Qualification: ") + PreviewText(project, flag.nameKey)));
            foreach (var modifier in project.modifiers.Where(m => state.Read(ValueKind.ModifierDays, m.id) > 0))
                parent.Add(new Label(PreviewText(project, modifier.nameKey) + " · " + state.Read(ValueKind.ModifierDays, modifier.id) + (StudioText.Language == "ko" ? "일 남음" : " game days remaining")));
        }

        private void DrawEvaluation(VisualElement parent, ProjectDefinition project, ActivityDefinition activity)
        {
            activity.evaluation ??= new EvaluationDefinition();
            var value = activity.evaluation;
            var fold = new Foldout { text = StudioText.T("Evaluation"), value = value.enabled };
            parent.Add(fold);
            Toggle(fold, "Enabled", value.enabled, x => value.enabled = x);
            SelectReference(fold, "Score stat", project.stats, value.statId, x => value.statId = x);
            Number(fold, "Passing score", value.passingScore, x => value.passingScore = x);
            SelectReference(fold, "Qualification flag", project.flags.Where(f => f.permanent).ToList(), value.qualificationFlagId, x => value.qualificationFlagId = x, true);
            fold.Add(new HelpBox(StudioText.T("Score is captured before activity effects. Each check records best, total, attempts and passes. A pass grants the selected permanent flag."), HelpBoxMessageType.Info));
        }

        private void DrawModifier(VisualElement parent, ProjectDefinition project, TimedModifierDefinition modifier)
        {
            SelectReference(parent, "Score stat", project.stats, modifier.statId, x => modifier.statId = x);
            SelectReference(parent, "Activity (empty = all)", project.activities, modifier.activityId, x => modifier.activityId = x, true);
            Number(parent, "Bonus per additive stat effect", modifier.additiveBonus, x => modifier.additiveBonus = x);
            parent.Add(new HelpBox(StudioText.T("Activate with ModifierDays: Set game days. Reapplying replaces the remaining duration; 0 removes it. Only additive activity stat effects are adjusted."), HelpBoxMessageType.Info));
        }
    }
}
