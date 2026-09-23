using System;
using System.Collections.Generic;

namespace PrincessStudio.Core
{
    [Serializable] public sealed class ConditionExplanation
    {
        public string id, target, actorId, comparison, error, anyGroup;
        public int expected, actual;
        public bool passed;
    }
    [Serializable] public sealed class SequenceTraceFrame
    {
        public string nodeId, nextStepId, choiceId;
        public StateData before, after;
        public List<ConditionExplanation> conditions = new List<ConditionExplanation>();
    }
    [Serializable] public sealed class ExecutionTrace
    {
        public List<SequenceTraceFrame> frames = new List<SequenceTraceFrame>();
        public List<ConditionExplanation> entryConditions = new List<ConditionExplanation>();
        public string status = "", error = "";
        public StateData finalState;
    }
    public sealed partial class GameSession
    {
        public bool TraceEnabled { get; set; }
        private readonly List<SequenceTraceFrame> traceFrames = new List<SequenceTraceFrame>();
        internal List<SequenceTraceFrame> TakeTrace() { var result = new List<SequenceTraceFrame>(traceFrames); traceFrames.Clear(); return result; }
        private static bool IsAutomatic(PresentationStep step) => step != null && (step.kind == PresentationStepKind.Condition || step.kind == PresentationStepKind.Effect || step.kind == PresentationStepKind.Merge);
        private void RunAutomaticStep(GameState next, EventDefinition e, PresentationStep step)
        {
            var target = EventSequence.Next(e, step);
            var port = "Next";
            if (step.kind == PresentationStepKind.Condition)
            {
                var passed = rules.Matches(new StateSnapshot(next, project), step.conditions); port = passed ? "True" : "False";
                if (!passed) target = EventSequence.Next(e, step, step.falseStepId);
            }
            if (step.kind == PresentationStepKind.Effect) rules.Apply(next, step.effects);
            Observations?.Record("Branch", step.id, port, next.Day, target);
            MovePresentation(next, e, target, step.id, port);
        }
        internal List<ConditionExplanation> Explain(IReadOnlyList<ConditionSpec> conditions, StateSnapshot snapshot)
        {
            var result = new List<ConditionExplanation>();
            foreach (var c in conditions)
            {
                var explanation = new ConditionExplanation { id = c.id, actorId = c.actorId, anyGroup = c.anyGroup, target = c.kind + ":" + c.target, expected = c.value, comparison = c.comparison.ToString() };
                try { explanation.passed = rules.Matches(snapshot, new[] { c }); if (c.kind != ValueKind.Custom) explanation.actual = snapshot.Read(c.kind, c.target, c.actorId); }
                catch (Exception ex) { explanation.error = ex.Message; }
                result.Add(explanation);
            }
            return result;
        }
        private SequenceTraceFrame MakeTrace(string id, GameState before, GameState after)
        {
            var frame = new SequenceTraceFrame { nodeId = id, nextStepId = after.PresentationStepId, before = before.ToData(), after = after.ToData() };
            foreach (var e in project.events)
            {
                var step = e.presentation.Find(x => x.id == id);
                if (step != null) { frame.conditions = Explain(step.conditions, new StateSnapshot(before, project)); break; }
                if (e.id == id) frame.conditions = Explain(e.conditions, new StateSnapshot(before, project));
            }
            return frame;
        }
        internal bool StartPreview(string eventId, string nodeId)
        {
            var e = events.Find(x => x.id == eventId) ?? throw new ArgumentException("Unknown preview event.");
            var next = state.Clone();
            if (string.IsNullOrEmpty(nodeId) || nodeId == e.id)
            {
                if (!rules.Matches(State, e.conditions)) return false;
                rules.Apply(next, e.effects); next.SeenEvents.Add(e.id);
                next.PendingEventId = e.id; next.PresentationPath.Clear();
                MovePresentation(next, e, EventSequence.Entry(e));
                Commit(next);
            }
            else
            {
                var path = EventSequence.PreviewPath(e, nodeId);
                if (path.Count == 0) throw new ArgumentException("Node is not reachable from entry.");
                next.PendingEventId = e.id; next.PresentationStepId = nodeId; next.PresentationPath = path;
                // A selected-node preset explicitly supplies state after preceding steps; no earlier effects are replayed.
                state = next; cachedSnapshot = null;
            }
            return true;
        }
    }
}
