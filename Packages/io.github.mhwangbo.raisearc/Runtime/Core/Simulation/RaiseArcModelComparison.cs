using System;
using System.Collections.Generic;
using PrincessStudio.Core;

namespace RaiseArc.Analysis
{
    [Serializable] public sealed class ModelComparison
    {
        public bool matches;
        public string moduleId, matchedOutcome, detail;
        public string observationId, source;
    }
    public static class AnalysisModelComparison
    {
        // Results pass the same core transaction validator. A mismatch never edits the model or its probabilities.
        public static ModelComparison Compare(ProjectDefinition ownedProject, StateData before, string activityId, ModuleResult observed, IModuleAnalysis model)
        {
            if (model == null || model.Contract.kind != ModuleAnalysisKind.ResultModel) throw new ArgumentException("An explicit result model is required.");
            var actual = new GameSession(ownedProject); actual.Restore(before); var activity = ownedProject.activities.Find(a => a.id == activityId);
            if (activity == null || activity.moduleId != model.Contract.moduleId) throw new ArgumentException("Model does not match the activity module.");
            var context = actual.BeginModule(activityId);
            actual.CompleteModule(new ModuleResult { sessionId = context.SessionId, elapsedDays = observed.elapsedDays, messageKey = observed.messageKey, effects = observed.effects });
            var actualKey = SimulationIdentity.StateKey(actual.Capture(), new ModuleState[0]);
            var start = new GameSession(ownedProject); start.Restore(before);
            foreach (var outcome in model.Outcomes(start.State, activityId, ""))
            {
                var candidate = new GameSession(ownedProject); candidate.Restore(before); var candidateContext = candidate.BeginModule(activityId);
                candidate.CompleteModule(new ModuleResult { sessionId = candidateContext.SessionId, elapsedDays = outcome.result.elapsedDays, messageKey = outcome.result.messageKey, effects = outcome.result.effects });
                if (SimulationIdentity.StateKey(candidate.Capture(), new ModuleState[0]) == actualKey)
                    return new ModelComparison { matches = true, moduleId = model.Contract.moduleId, matchedOutcome = outcome.id, detail = "Observed committed core state matches this modeled outcome. Internal module behavior was not compared." };
            }
            return new ModelComparison { moduleId = model.Contract.moduleId, detail = "Model mismatch: observed committed core state lies outside the supplied outcome model. Probabilities were not changed." };
        }
    }
}
