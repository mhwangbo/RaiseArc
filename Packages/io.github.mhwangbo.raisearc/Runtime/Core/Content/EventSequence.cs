using System;
using System.Collections.Generic;

namespace PrincessStudio.Core
{
    public enum PresentationStepKind { Dialogue, Image, Choice, Condition, Effect, Merge }
    public enum StageChange { Replace, Keep, Clear }
    public enum StageImagePlane { BehindActors, InFrontOfActors }

    [Serializable]
    public sealed class StageImage
    {
        public string id = "", resourceKey = "";
        // Normalized top-left rectangle. List order is draw order within each plane.
        public float x, y, width = 1, height = 1, opacity = 1;
        public StageImagePlane plane = StageImagePlane.InFrontOfActors;
        public StageImage Copy() => (StageImage)MemberwiseClone();
    }

    public sealed class StageState
    {
        public string backgroundKey = "";
        public List<ActorPlacement> actors = new List<ActorPlacement>();
        public List<StageImage> images = new List<StageImage>();
        public void Apply(PresentationStep step)
        {
            if (step.backgroundChange != StageChange.Keep)
                backgroundKey = step.backgroundChange == StageChange.Clear ? "" : step.backgroundKey;
            if (step.actorsChange != StageChange.Keep)
            {
                actors.Clear();
                if (step.actorsChange == StageChange.Replace)
                    foreach (var actor in step.actors) actors.Add(new ActorPlacement { actorId = actor.actorId, slotId = actor.slotId, overrides = actor.overrides.Copy() });
            }
            if (step.imagesChange != StageChange.Keep)
            {
                images.Clear();
                if (step.imagesChange == StageChange.Replace)
                    foreach (var image in step.images) images.Add(image.Copy());
            }
        }
        public StageState Copy()
        {
            var result = new StageState();
            result.Apply(new PresentationStep { backgroundKey = backgroundKey, actors = actors, images = images });
            return result;
        }
    }

    public static class EventSequence
    {
        public const string End = "$end";
        public const string TerminalChoices = "$choices";
        public static string Entry(EventDefinition e) => e.entryStepId == TerminalChoices ? "" :
            !string.IsNullOrEmpty(e.entryStepId) ? e.entryStepId : e.presentation.Count > 0 ? e.presentation[0].id : "";
        /// <summary>Preview only: first structural path, without evaluating conditions or applying effects.</summary>
        public static List<string> PreviewPath(EventDefinition e, string target)
        {
            var path = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            bool Visit(string id)
            {
                if (!visited.Add(id)) return false;
                var step = e.presentation.Find(x => x.id == id);
                if (step == null) return false;
                path.Add(id);
                if (id == target) return true;
                if (step.kind == PresentationStepKind.Choice)
                {
                    foreach (var choice in step.choices) if (Visit(Next(e, step, choice.nextStepId))) return true;
                }
                else if (Visit(Next(e, step)) || step.kind == PresentationStepKind.Condition && Visit(Next(e, step, step.falseStepId))) return true;
                path.RemoveAt(path.Count - 1); return false;
            }
            Visit(Entry(e));
            return path;
        }
        public static string Next(EventDefinition e, PresentationStep step, string choiceTarget = null)
        {
            var target = string.IsNullOrEmpty(choiceTarget) ? step.nextStepId : choiceTarget;
            if (target == TerminalChoices) return "";
            if (!string.IsNullOrEmpty(target)) return target;
            var index = e.presentation.IndexOf(step);
            return index + 1 < e.presentation.Count ? e.presentation[index + 1].id : "";
        }
        public static StageState Resolve(EventDefinition e, IReadOnlyList<string> path, string currentId)
        {
            var result = new StageState();
            if (e == null) return result;
            if (path.Count > 0)
            {
                var steps = new Dictionary<string, PresentationStep>(StringComparer.Ordinal);
                foreach (var step in e.presentation) steps.Add(step.id, step);
                foreach (var id in path) result.Apply(steps[id]);
            }
            else
            {
                if (!string.IsNullOrEmpty(e.entryStepId)) return result;
                // Legacy saves stored only the current complete stage snapshot.
                var step = e.presentation.Find(x => x.id == currentId);
                if (step == null && e.presentation.Count > 0) step = e.presentation[e.presentation.Count - 1];
                if (step != null) result.Apply(step);
            }
            return result;
        }
    }
}
