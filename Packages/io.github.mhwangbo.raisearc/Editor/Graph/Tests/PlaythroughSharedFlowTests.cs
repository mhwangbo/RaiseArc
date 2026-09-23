using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Analysis;

namespace PrincessStudio.Editor.Graph.Tests
{
    public sealed class PlaythroughSharedFlowTests
    {
        [Test]
        public void DiscardedRandomEventEffectDoesNotCommitOrAdvanceRandomState()
        {
            var project = new ProjectDefinition { id = "discard-random-event", durationDays = 3 };
            project.activities.Add(new ActivityDefinition { id = "rest", nameKey = "rest", days = 1 });
            project.endings.Add(new EndingDefinition { id = "complete", nameKey = "complete" });
            var baseline = new UnityProjectCodec().Clone(project);
            var ev = new EventDefinition { id = "random", nameKey = "random" };
            ev.effects.Add(new EffectSpec { id = "roll", kind = ValueKind.Money, randomRange = true, minimumValue = 1, maximumValue = 2 });
            var locked = new ChoiceDefinition { id = "locked", nameKey = "locked" };
            locked.conditions.Add(new ConditionSpec { id = "impossible", kind = ValueKind.Money, comparison = Comparison.AtLeast, value = 9999 });
            ev.choices.Add(locked); project.events.Add(ev);
            var session = new GameSession(project, null, 17);
            var withoutEvent = new GameSession(baseline, null, 17);
            var bufferType = typeof(GameSession).Assembly.GetType("RaiseArc.Analysis.RuntimeObservations", true);
            var buffer = Activator.CreateInstance(bufferType, true);
            typeof(GameSession).GetProperty("Observations", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, buffer);

            session.PerformActivity("rest"); withoutEvent.PerformActivity("rest");
            bufferType.GetMethod("CommitFrom").Invoke(buffer, new object[] { 0 });
            var observations = (List<RuntimeObservation>)bufferType.GetField("Entries").GetValue(buffer);
            var random = observations.Single(o => o.kind == "RandomEffect" && o.id == "roll");
            Assert.That(random.outcome, Is.EqualTo("Discarded"));
            Assert.That(random.committed, Is.False);
            Assert.That(session.Capture().randomState, Is.EqualTo(withoutEvent.Capture().randomState));
            Assert.That(session.Capture().money, Is.EqualTo(withoutEvent.Capture().money));
        }
        [Test]
        public void RandomizedPlaythroughAdvancesExpandedSharedFlow()
        {
            var project = new ProjectDefinition { id = "shared-flow-playthrough", durationDays = 2 };
            project.activities.Add(new ActivityDefinition { id = "rest", nameKey = "rest", days = 1 });
            project.endings.Add(new EndingDefinition { id = "complete", nameKey = "complete" });
            var shared = new EventDefinition { id = "shared", nameKey = "shared", callOnly = true };
            shared.presentation.Add(new PresentationStep { id = "line", nameKey = "line", kind = PresentationStepKind.Dialogue,
                nextStepId = EventSequence.End });
            project.events.Add(shared);
            var main = new EventDefinition { id = "main", nameKey = "main" };
            main.presentation.Add(new PresentationStep { id = "call", nameKey = "call", sharedEventId = "shared",
                nextStepId = EventSequence.End });
            project.events.Add(main);

            var report = PlaythroughSimulator.Run(project, new UnityProjectCodec(), 17, 1, 20);

            Assert.That(report.failures, Is.Empty);
            Assert.That(report.completed, Is.EqualTo(1));
            Assert.That(report.blocked, Is.Zero);
        }
    }
}
