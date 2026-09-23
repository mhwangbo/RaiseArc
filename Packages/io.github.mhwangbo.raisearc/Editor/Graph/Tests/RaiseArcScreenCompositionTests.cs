using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PrincessStudio.Unity;
using RaiseArc.Unity;
using UnityEngine;

namespace RaiseArc.Editor.Tests
{
    public sealed class ScreenCompositionTests
    {
        [Test]
        public void OldDefinitionDoesNotEnableComposition()
        {
            var d = JsonUtility.FromJson<GameScreenDefinition>("{\"version\":1,\"fontSize\":18}");
            Assert.That(d.compositionPreview, Is.False);
            Assert.That(d.parts, Is.Not.Null.And.Empty);
        }
        [Test]
        public void LayoutRejectsDuplicateIdentityAndOffCanvasParts()
        {
            var parts = ScreenComposition.WeeklyStarter(); ScreenComposition.Validate(parts);
            parts[1].id = parts[0].id;
            Assert.Throws<ArgumentException>(() => ScreenComposition.Validate(parts));
            parts[1].id = "character"; parts[1].bounds = new Rect(float.NaN, 0, 20, 20);
            Assert.Throws<ArgumentException>(() => ScreenComposition.Validate(parts));
        }
        [Test]
        public void InvalidDraftRestorePreservesExistingPlan()
        {
            var d = new PlanningRehearsal(new[] { "study" }); d.Place(0, "study");
            var before = d.Capture();
            Assert.Throws<ArgumentException>(() => d.Restore(1, "{\"slots\":[\"missing\"]}"));
            Assert.That(d.Capture(), Is.EqualTo(before));
            Assert.Throws<ArgumentException>(() => d.Place(1, "missing"));
        }
        [Test]
        public void HostSaveRestoresDraftWithoutAdvancingGameOrRandomState()
        {
            var project = GameCreation.Build(new NewGameDefinition());
            var path = Path.Combine(Application.temporaryCachePath, "RaiseArcPlanningTests", Guid.NewGuid().ToString("N"));
            var host = new RaiseArcSessionHost(project, path, 31);
            var plan = new PlanningRehearsal(project.activities.Select(a => a.id)); host.RegisterSaveParticipant(plan);
            var state = JsonUtility.ToJson(host.Session.Capture());
            plan.Place(0, project.activities[0].id); plan.CopyDay(0, 1); host.Save("draft");
            var restored = new RaiseArcSessionHost(project, path, 31);
            var newPlan = new PlanningRehearsal(project.activities.Select(a => a.id)); restored.RegisterSaveParticipant(newPlan);
            Assert.That(restored.Load("draft"), Is.False);
            Assert.That(newPlan[0], Is.EqualTo(project.activities[0].id));
            Assert.That(newPlan[3], Is.EqualTo(project.activities[0].id));
            Assert.That(JsonUtility.ToJson(restored.Session.Capture()), Is.EqualTo(state));
        }
    }
}
