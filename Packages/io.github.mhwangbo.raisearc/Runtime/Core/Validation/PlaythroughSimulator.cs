using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PrincessStudio.Core
{
    [Serializable]
    public sealed class SimulationReport
    {
        public int seed, runs, completed, blocked, steps, minMoney = int.MaxValue, maxMoney;
        public long elapsedMilliseconds;
        public List<IntEntry> endingCounts = new List<IntEntry>();
        public List<string> unobservedEndings = new List<string>();
        public List<string> failures = new List<string>();
        public string interpretation = "Bounded randomized exploration. Unobserved endings are not proof of unreachability. External modules require a supplied simulation policy and are excluded here.";
    }
    public static class PlaythroughSimulator
    {
        public static SimulationReport Run(ProjectDefinition project, IProjectCodec codec, int seed, int runs, int maxSteps, ExtensionRegistry extensions = null)
        {
            if (runs < 1 || runs > 1000 || maxSteps < 1 || maxSteps > 100000 || (long)runs * maxSteps > 2000000)
                throw new ArgumentOutOfRangeException(nameof(runs), "Simulation work budget exceeded.");
            var result = new SimulationReport { seed = seed, runs = runs };
            var random = new Random(seed);
            var watch = Stopwatch.StartNew();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var run = 0; run < runs; run++)
            {
                var session = new GameSession(codec.Clone(project), extensions, unchecked((uint)seed + (uint)run * 0x9e3779b9U));
                var candidates = new List<string>();
                try
                {
                    for (var step = 0; step < maxSteps; step++)
                    {
                        var state = session.State;
                        result.minMoney = Math.Min(result.minMoney, state.Money);
                        result.maxMoney = Math.Max(result.maxMoney, state.Money);
                        if (state.EndingId.Length > 0)
                            break;
                        if (state.PendingEventId.Length > 0)
                        {
                            if (state.PresentationStepId.Length > 0 && !session.PendingPresentationIsChoice)
                            {
                                session.AdvancePresentation(state.PresentationStepId);
                                result.steps++;
                                continue;
                            }
                            var choices = session.AvailableChoices();
                            if (choices.Count == 0)
                                break;
                            session.Choose(choices[random.Next(choices.Count)]);
                        }
                        else
                        {
                            candidates.Clear();
                            foreach (var a in project.activities)
                                if (string.IsNullOrEmpty(a.moduleId) && session.CanPerform(a.id))
                                    candidates.Add(a.id);
                            if (candidates.Count == 0)
                                break;
                            session.PerformActivity(candidates[random.Next(candidates.Count)]);
                        }
                        result.steps++;
                        if (step % 31 == 0)
                        {
                            var saved = session.Capture();
                            var restored = new GameSession(codec.Clone(project), extensions);
                            restored.Restore(saved);
                            session = restored;
                        }
                    }
                    var ending = session.State.EndingId;
                    if (ending.Length > 0)
                    {
                        result.completed++;
                        counts.TryGetValue(ending, out var n);
                        counts[ending] = n + 1;
                    }
                    else
                        result.blocked++;
                }
                catch (Exception e) { result.blocked++; if (result.failures.Count < 32) result.failures.Add("Run " + run + ": " + e.Message); }
            }
            foreach (var ending in project.endings)
                if (counts.TryGetValue(ending.id, out var count))
                    result.endingCounts.Add(new IntEntry { id = ending.id, value = count });
                else
                    result.unobservedEndings.Add(ending.id);
            watch.Stop();
            result.elapsedMilliseconds = watch.ElapsedMilliseconds;
            return result;
        }
    }
}
