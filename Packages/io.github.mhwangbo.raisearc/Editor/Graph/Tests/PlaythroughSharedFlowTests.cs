using NUnit.Framework;
using PrincessStudio.Core;
using PrincessStudio.Unity;

namespace PrincessStudio.Editor.Graph.Tests
{
    public sealed class PlaythroughSharedFlowTests
    {
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
