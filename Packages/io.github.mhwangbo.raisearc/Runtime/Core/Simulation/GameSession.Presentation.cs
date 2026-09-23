using System;

namespace PrincessStudio.Core
{
    public sealed partial class GameSession
    {
        public void AdvancePresentation(string expectedStepId)
        {
            if (pendingModule != null || string.IsNullOrEmpty(expectedStepId) || state.PresentationStepId != expectedStepId)
                throw new InvalidOperationException("Dialogue changed; refresh the view.");
            var definition = events.Find(x => x.id == state.PendingEventId) ?? throw new InvalidOperationException("No pending event.");
            var index = definition.presentation.FindIndex(x => x.id == expectedStepId);
            if (index < 0) throw new InvalidOperationException("Unknown presentation step.");
            var step = definition.presentation[index];
            if (step.kind == PresentationStepKind.Choice) throw new InvalidOperationException("Choose an option to advance this step.");
            var next = state.Clone();
            if (IsAutomatic(step)) RunAutomaticStep(next, definition, step);
            else MovePresentation(next, definition, EventSequence.Next(definition, step));
            Commit(next);
        }

        private void MovePresentation(GameState next, EventDefinition definition, string target, string sourceId = null, string port = null)
        {
            Observations?.Entries.Add(new RaiseArc.Analysis.RuntimeObservation { kind = "Edge", ownerId = definition.id,
                id = sourceId ?? (next.PresentationStepId.Length == 0 ? definition.id : next.PresentationStepId), relatedId = target.Length == 0 ? (definition.choices.Count == 0 ? EventSequence.End : EventSequence.TerminalChoices) : target,
                outcome = port ?? (next.PresentationStepId.Length == 0 ? "Entry" : "Next"), day = next.Day });
            if (target == EventSequence.End || target.Length == 0 && definition.choices.Count == 0)
            { FinishPresentation(next); return; }
            next.PresentationStepId = target;
            if (target.Length > 0) Observations?.Record("Node", target, "Entered", next.Day, definition.id);
            if (target.Length > 0) next.PresentationPath.Add(target);
        }

        private void FinishPresentation(GameState next)
        {
            next.PendingEventId = "";
            next.PresentationStepId = "";
            next.PresentationPath.Clear();
            ResolveEnding(next);
        }

        private void ValidatePresentationState(GameState value)
        {
            var count = 0;
            foreach (var actor in project.actors)
                foreach (var condition in actor.conditions)
                {
                    count++;
                    if (!value.ActorValues.TryGetValue(StateSnapshot.ActorValueKey(actor.id, condition.id), out var n) || n < condition.minimum || n > condition.maximum)
                        throw new ArgumentException("Invalid saved actor condition.");
                }
            if (value.ActorValues.Count != count) throw new ArgumentException("Unknown saved actor condition.");
            var definition = events.Find(x => x.id == value.PendingEventId);
            if (value.PresentationPath.Count > 512) throw new ArgumentException("Saved event path exceeds budget.");
            if (value.PresentationPath.Count > 0)
            {
                if (definition == null || value.PresentationPath[0] != EventSequence.Entry(definition))
                    throw new ArgumentException("Invalid saved event path entry.");
                var visited = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                PresentationStep previous = null;
                foreach (var id in value.PresentationPath)
                {
                    var step = definition.presentation.Find(x => x.id == id);
                    if (step == null || !visited.Add(id)) throw new ArgumentException("Unknown or repeated saved event step.");
                    if (previous != null && !CanReach(definition, previous, id)) throw new ArgumentException("Invalid saved event path edge.");
                    previous = step;
                }
                if (value.PresentationStepId.Length > 0 ? previous.id != value.PresentationStepId : !CanReach(definition, previous, ""))
                    throw new ArgumentException("Saved event path does not match current step.");
            }
            else if (definition != null && !(EventSequence.Entry(definition) == "" && value.PresentationStepId == "") && definition.presentation.Exists(x => x.kind != PresentationStepKind.Dialogue || !string.IsNullOrEmpty(x.nextStepId) || x.backgroundChange == StageChange.Keep || x.actorsChange == StageChange.Keep || x.imagesChange == StageChange.Keep))
                throw new ArgumentException("This event requires saved stage history.");
            if (value.PresentationStepId.Length > 0 && (definition == null || !definition.presentation.Exists(x => x.id == value.PresentationStepId)))
                throw new ArgumentException("Unknown saved presentation step.");
            if (definition != null && definition.choices.Count == 0 && value.PresentationStepId.Length == 0)
                throw new ArgumentException("Completed dialogue cannot remain pending.");
            // Expand old linear saves before their next transition so subsequent saves have a full path.
            if (definition != null && string.IsNullOrEmpty(definition.entryStepId) && value.PresentationPath.Count == 0)
                foreach (var step in definition.presentation)
                {
                    value.PresentationPath.Add(step.id);
                    if (step.id == value.PresentationStepId) break;
                }
        }

        private static bool CanReach(EventDefinition e, PresentationStep from, string target)
        {
            if (from.kind == PresentationStepKind.Condition && EventSequence.Next(e, from, from.falseStepId) == target) return true;
            if (from.kind == PresentationStepKind.Choice)
                return from.choices.Exists(c => EventSequence.Next(e, from, c.nextStepId) == target);
            return EventSequence.Next(e, from) == target;
        }
    }
}
