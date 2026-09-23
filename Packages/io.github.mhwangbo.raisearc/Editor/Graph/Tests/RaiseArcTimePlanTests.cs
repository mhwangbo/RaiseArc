using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using RaiseArc.Core;
using RaiseArc.Unity;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace RaiseArc.Editor.Tests
{
    public sealed class TimePlanTests
    {
        [Test] public void UnsavedTimeRulesSurviveUndoRefreshAndRejectProjectSwitch()
        {
            var first = ScriptableObject.CreateInstance<GameProjectAsset>(); first.Write(Weekly());
            var second = ScriptableObject.CreateInstance<GameProjectAsset>(); second.Write(Weekly());
            var window = ScriptableObject.CreateInstance<TimePlanEditor>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(TimePlanEditor).GetField("asset", flags).SetValue(window, first);
                window.Show();
                window.CreateGUI();
                var planningDays = window.rootVisualElement.Q<IntegerField>();
                var changed = planningDays.value + 1;
                planningDays.value = changed;
                var draft = (ProjectDefinition)typeof(TimePlanEditor).GetField("draft", flags).GetValue(window);
                Assert.That(draft.time.planningDays, Is.EqualTo(changed));
                window.CreateGUI(); // same refresh path used by Unity Undo/Redo
                Assert.That(typeof(TimePlanEditor).GetField("draft", flags).GetValue(window), Is.SameAs(draft));
                window.rootVisualElement.Q<ObjectField>().value = second;
                Assert.That(typeof(TimePlanEditor).GetField("asset", flags).GetValue(window), Is.SameAs(first));
                Assert.That(draft.time.planningDays, Is.EqualTo(changed));
            }
            finally { window.Close(); UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); }
        }
        public static ProjectDefinition Weekly()
        {
            var p = GameCreation.Build(new NewGameDefinition { durationDays = 7 });
            p.time.periodNameKeys.AddRange(new[] { "time.morning", "time.afternoon", "time.evening" });
            foreach (var key in p.time.periodNameKeys)
                foreach (var locale in p.locales) p.translations.Add(new TranslationEntry { key = key, locale = locale, text = key });
            foreach (var a in p.activities) { a.days = 1; a.periods = 1; }
            p.events[0].conditions[0].value = 2;
            return p;
        }
        private static RaiseArcSessionHost Host(ProjectDefinition p, string directory = null) => new RaiseArcSessionHost(p,
            directory ?? Path.Combine(Application.temporaryCachePath, "RaiseArcTimePlan", Guid.NewGuid().ToString("N")), 31);
        private static void Fill(RaiseArcSessionHost host, ProjectDefinition p)
        { for (var i = 0; i < host.Plan.capacity; i++) host.PlacePlanActivity(i, p.activities[i % 3].id); }

        [Test] public void ThreePeriodsAreOneDayAndCostsApplyOnce()
        {
            var p = Weekly(); p.events.Clear(); var h = Host(p); Fill(h, p); h.ConfirmPlan();
            Assert.That(h.State.Day, Is.Zero); h.Resume();
            Assert.That(h.State.Period, Is.EqualTo(1)); Assert.That(h.State.Day, Is.Zero); Assert.That(h.State.Money, Is.EqualTo(55));
            h.Resume(); h.Resume(); Assert.That(h.State.Day, Is.EqualTo(1)); Assert.That(h.State.Period, Is.Zero); Assert.That(h.State.Money, Is.EqualTo(65));
        }
        [Test] public void DraftAndConfirmedWaitSurviveHostSaveLoad()
        {
            var p = Weekly(); var path = Path.Combine(Application.temporaryCachePath, "RaiseArcTimePlan", Guid.NewGuid().ToString("N"));
            var h = Host(p, path); Fill(h, p); h.Save("draft");
            var restored = Host(p, path); restored.Load("draft"); Assert.That(restored.Plan.entries.Count, Is.EqualTo(21));
            restored.ConfirmPlan();
            for (var i = 0; i < 21 && restored.Wait != SessionWait.Event; i++) restored.Resume();
            Assert.That(restored.Wait, Is.EqualTo(SessionWait.Event));
            var day = restored.State.Day; var money = restored.State.Money; restored.Save("event");
            var loaded = Host(p, path); loaded.Load("event"); Assert.That(loaded.Wait, Is.EqualTo(SessionWait.Event));
            Assert.That(loaded.State.Day, Is.EqualTo(day)); Assert.That(loaded.State.Money, Is.EqualTo(money));
            loaded.Choose(p.events[0].choices[0].id); Assert.That(loaded.ScheduleCursor, Is.EqualTo(6));
            for (var i = 0; i < 21 && loaded.Wait != SessionWait.Ended; i++) loaded.Resume();
            Assert.That(loaded.State.Day, Is.EqualTo(7)); Assert.That(loaded.State.Period, Is.Zero);
            Assert.That(loaded.State.Stats[p.stats[0].id], Is.EqualTo(45)); Assert.That(loaded.State.Money, Is.EqualTo(100));
        }
        [Test] public void PlanRejectsGapsOverlapAndPostConfirmationEdits()
        {
            var p = Weekly(); p.activities[0].periods = 2; var h = Host(p);
            h.PlacePlanActivity(1, p.activities[1].id);
            Assert.Throws<ArgumentException>(() => h.PlacePlanActivity(0, p.activities[0].id));
            Assert.That(h.Plan.entries.Single().slot, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => h.ConfirmPlan());
            var q = Weekly(); var other = Host(q); Fill(other, q); other.ConfirmPlan();
            Assert.Throws<InvalidOperationException>(() => other.PlacePlanActivity(0, ""));
        }
        [Test] public void InterruptedMultiPeriodActivityResumesWithoutRepeatingEffects()
        {
            var p = Weekly(); p.activities[0].periods = 4;
            p.events[0].conditions.Clear(); p.events[0].evaluateEachPeriod = true;
            var session = GameFactory.Create(p, 31); session.PerformActivity(p.activities[0].id);
            Assert.That(session.State.Period, Is.EqualTo(1)); Assert.That(session.State.RemainingActivityPeriods, Is.EqualTo(3));
            var copy = GameFactory.Create(p, 31); copy.Restore(session.Capture()); copy.Choose(p.events[0].choices[0].id);
            Assert.That(copy.State.Day, Is.EqualTo(1)); Assert.That(copy.State.Period, Is.EqualTo(1));
            Assert.That(copy.State.Stats[p.stats[0].id], Is.EqualTo(15));
        }
        [Test] public void CommittedSearchAndReplayMatchHostInputs()
        {
            var p = Weekly(); var host = Host(p); Fill(host, p); host.ConfirmPlan();
            var settings = new ExplorationSettings { mode = ExplorationMode.TargetSearch, endingGoal = true, targetId = p.endings[0].id,
                useCommittedPlan = true, committedActivities = host.Schedule.ToList(), separateGameSeed = true, gameSeed = 31, maxSteps = 50, horizonDays = 7 };
            var explorer = new SimulationExplorer(p, new UnityProjectCodec(), new AnalysisManifest { settings = settings, initial = host.Session.Capture() });
            for (var i = 0; i < 100 && explorer.Report.status == ExplorationStatus.Running; i++) explorer.Tick(128, 100);
            Assert.That(explorer.Report.targetRecord, Is.GreaterThanOrEqualTo(0), explorer.Report.manifest.stopReason);
            Assert.That(explorer.VerifyPath(explorer.Report.targetRecord).matches, Is.True);
            var path = explorer.Path(explorer.Report.targetRecord);
            foreach (var step in path.Skip(1))
            {
                if (step.input.kind == "Activity") host.Resume();
                else if (step.input.kind == "Choice") host.Choose(step.input.id);
                else host.AdvanceDialogue(step.input.id);
                Assert.That(SimulationIdentity.StateKey(host.Session.Capture(), Array.Empty<ModuleState>()), Is.EqualTo(SimulationIdentity.StateKey(step.state, Array.Empty<ModuleState>())));
            }
            Assert.That(host.State.EndingId, Is.EqualTo(p.endings[0].id));
        }

        [Test] public void CopyDayDoesNotEraseAnActivityAcrossDestinationBoundary()
        {
            var p = Weekly(); p.activities[0].periods = 4; var h = Host(p);
            h.PlacePlanActivity(2, p.activities[0].id);
            Assert.Throws<ArgumentException>(() => h.CopyPlanDay(2, 1));
            Assert.That(h.Plan.entries.Single().slot, Is.EqualTo(2));
        }
        [Test] public void PeriodCalendarChangeRejectsOldDaySave()
        {
            var p = GameCreation.Build(new NewGameDefinition { durationDays = 7 });
            var old = GameFactory.Create(p, 1).Capture();
            p.time.periodNameKeys.AddRange(new[] { "time.a", "time.b", "time.c" });
            var next = GameFactory.Create(p, 1);
            Assert.Throws<ArgumentException>(() => next.Restore(old));
            Assert.That(next.State.Period, Is.Zero);
        }
        [Test] public void AnalysisRejectsPlanOutsideItsTimeRange()
        {
            var p = Weekly(); p.time.planningDays = 1; p.activities[0].periods = 4;
            var settings = new ExplorationSettings { useCommittedPlan = true, committedActivities = new System.Collections.Generic.List<string> { p.activities[0].id } };
            Assert.Throws<ArgumentException>(() => new SimulationExplorer(p, new UnityProjectCodec(), new AnalysisManifest { settings = settings }));
        }
        [Test] public void CheckpointRejectsInvalidCommittedPlanCursor()
        {
            var p = Weekly(); p.events.Clear(); p.time.planningDays = 1; var host = Host(p); Fill(host, p); host.ConfirmPlan();
            var settings = new ExplorationSettings { mode = ExplorationMode.Exhaustive, useCommittedPlan = true, committedActivities = host.Schedule.ToList() };
            var explorer = new SimulationExplorer(p, new UnityProjectCodec(), new AnalysisManifest { settings = settings });
            for (var i = 0; i < 10 && explorer.Report.status == ExplorationStatus.Running; i++) explorer.Tick(128, 100);
            var checkpoint = explorer.Checkpoint; checkpoint.records[0].planCursor = 4;
            Assert.Throws<ArgumentException>(() => new SimulationExplorer(p, new UnityProjectCodec(), checkpoint.report.manifest, resume: checkpoint));
        }
        [Test] public void CompletedShortPlanDoesNotChooseMoreActivities()
        {
            var p = Weekly(); p.time.planningDays = 1; p.events.Clear();
            var h = Host(p); Fill(h, p); h.ConfirmPlan();
            var settings = new ExplorationSettings { mode = ExplorationMode.Exhaustive, useCommittedPlan = true, committedActivities = h.Schedule.ToList(), horizonDays = 7 };
            var explorer = new SimulationExplorer(p, new UnityProjectCodec(), new AnalysisManifest { settings = settings, initial = h.Session.Capture() });
            for (var i = 0; i < 30 && explorer.Report.status == ExplorationStatus.Running; i++) explorer.Tick(64, 100);
            var checkpoint = explorer.Checkpoint;
            Assert.That(checkpoint.records.Count(r => r.result == PlayResult.PlanComplete), Is.EqualTo(1));
            Assert.That(checkpoint.records.Max(r => r.state.day), Is.EqualTo(1));
        }

        [Test] public void NewActivitiesUseTheSameCardTemplateAndLayoutDoesNotOwnThePlan()
        {
            var p = Weekly();
            var service = new AuthoringService(p, new UnityProjectCodec());
            service.CreateActivity(new ActivityDefinition { id = "extra-work", nameKey = p.activities[1].nameKey, days = 1, periods = 1 }, service.Revision);
            var h = Host(service.Snapshot()); h.PlacePlanActivity(0, "extra-work");
            var before = JsonUtility.ToJson(h.Plan);
            var screen = new ComposedScreen(); var root = new UnityEngine.UIElements.VisualElement();
            var layout = new GameScreenDefinition { compositionPreview = true, parts = ScreenComposition.WeeklyStarter() };
            screen.Draw(root, layout, h, null, "en", null, null);
            Assert.That(root.Q<UnityEngine.UIElements.Button>("activity-card-extra-work"), Is.Not.Null);
            layout.parts.Reverse(); layout.parts.Find(x => x.kind == ScreenPartKind.Activities).horizontalCards = true;
            screen.Draw(root, layout, h, null, "ko", null, null);
            Assert.That(root.Q<UnityEngine.UIElements.Button>("activity-card-extra-work"), Is.Not.Null);
            Assert.That(JsonUtility.ToJson(h.Plan), Is.EqualTo(before));
            Assert.That(h.State.Day, Is.Zero); Assert.That(h.State.Money, Is.EqualTo(p.startingMoney));
        }
        [Test] public void MonthlyPlanRestoresAtDialogueAndFinishesWithTheSameState()
        {
            var p = GameCreation.Build(new NewGameDefinition { durationDays = 30 });
            p.time.planningDays = 30; p.time.periodNameKeys.Add("time.day");
            foreach (var activity in p.activities) { activity.days = 1; activity.periods = 1; activity.checkFrequency = ActivityCheckFrequency.OncePerActivity; }
            var story = p.events[0];
            story.presentation.Add(new PresentationStep { id = "first", nameKey = story.descriptionKey, kind = PresentationStepKind.Dialogue, nextStepId = "second" });
            story.presentation.Add(new PresentationStep { id = "second", nameKey = story.descriptionKey, kind = PresentationStepKind.Dialogue, nextStepId = EventSequence.TerminalChoices });
            var directory = Path.Combine(Application.temporaryCachePath, "RaiseArcMonthly", Guid.NewGuid().ToString("N"));
            var uninterrupted = Host(p);
            var saved = Host(p, directory);
            foreach (var host in new[] { uninterrupted, saved })
            {
                Assert.That(host.Plan.capacity, Is.EqualTo(30));
                for (var day = 0; day < 30; day++) host.PlacePlanActivity(day, p.activities[day % 3].id);
                host.ConfirmPlan();
                while (host.Wait == SessionWait.Paused) host.Resume();
                Assert.That(host.Wait, Is.EqualTo(SessionWait.Event));
                Assert.That(host.State.PresentationStepId, Is.EqualTo("first"));
            }
            saved.Save("dialogue");
            var restored = Host(p, directory); restored.Load("dialogue");
            Assert.That(restored.State.PresentationStepId, Is.EqualTo("first"));
            Assert.That(restored.Plan.confirmed, Is.True);
            Assert.That(restored.ScheduleCursor, Is.EqualTo(saved.ScheduleCursor));
            foreach (var host in new[] { uninterrupted, restored })
            {
                host.AdvanceDialogue("first");
                Assert.That(host.State.PresentationStepId, Is.EqualTo("second"));
                host.AdvanceDialogue("second");
                Assert.That(host.Choices.Count, Is.EqualTo(2));
                host.Choose(story.choices[0].id);
                Assert.That(host.Wait, Is.EqualTo(SessionWait.Paused));
            }
            Assert.That(restored.State.Day, Is.EqualTo(uninterrupted.State.Day));
            while (uninterrupted.Wait == SessionWait.Paused && uninterrupted.ScheduleCursor < uninterrupted.Schedule.Count) uninterrupted.Resume();
            while (restored.Wait == SessionWait.Paused && restored.ScheduleCursor < restored.Schedule.Count) restored.Resume();
            Assert.That(restored.ScheduleCursor, Is.EqualTo(30));
            Assert.That(restored.State.Day, Is.EqualTo(30));
            Assert.That(restored.Session.Capture().randomState, Is.EqualTo(uninterrupted.Session.Capture().randomState));
            Assert.That(restored.State.Money, Is.EqualTo(uninterrupted.State.Money));
            Assert.That(restored.State.EndingId, Is.EqualTo(uninterrupted.State.EndingId));
        }

        [Test] public void ThreeNextDayPlansPreserveDraftEventAndProgress()
        {
            var p = Weekly(); p.durationDays = 5; p.time.planningDays = 1;
            foreach (var activity in p.activities) activity.checkFrequency = ActivityCheckFrequency.OncePerActivity;
            var story = p.events[0]; story.evaluateEachPeriod = true;
            story.conditions.Clear();
            story.conditions.Add(new ConditionSpec { id = "next-day-wisdom", kind = ValueKind.Stat, target = p.stats[0].id,
                comparison = Comparison.AtLeast, value = p.stats[0].initial + 5 });
            story.presentation.Add(new PresentationStep { id = "next-day-first", nameKey = story.descriptionKey,
                kind = PresentationStepKind.Dialogue, nextStepId = "next-day-second" });
            story.presentation.Add(new PresentationStep { id = "next-day-second", nameKey = story.descriptionKey,
                kind = PresentationStepKind.Dialogue, nextStepId = EventSequence.TerminalChoices });
            var directory = Path.Combine(Application.temporaryCachePath, "RaiseArcNextDay", Guid.NewGuid().ToString("N"));
            var reference = Host(p); var restored = Host(p, directory);
            foreach (var host in new[] { reference, restored })
            {
                Assert.That(host.Plan.startTick, Is.Zero);
                Assert.That(host.Plan.capacity, Is.EqualTo(3));
                foreach (var slot in new[] { 0, 1, 2 }) host.PlacePlanActivity(slot, p.activities[slot == 1 ? 2 : 1].id);
                var money = host.State.Money; host.ConfirmPlan(); Assert.That(host.State.Money, Is.EqualTo(money));
                Assert.That(host.State.Day, Is.Zero); Assert.That(host.State.Period, Is.Zero);
                Assert.That(host.NewPlanProblem(), Is.Not.Empty);
                for (var i = 0; i < 3; i++) host.Resume();
                Assert.That(host.State.Day, Is.EqualTo(1)); Assert.That(host.Wait, Is.EqualTo(SessionWait.Paused));
                Assert.That(host.ScheduleCursor, Is.EqualTo(3));
                Assert.That(host.NewPlanProblem(), Is.Empty);
                host.NewPlan(); Assert.That(host.Plan.startTick, Is.EqualTo(3));
                host.PlacePlanActivity(0, p.activities[1].id);
                host.PlacePlanActivity(1, p.activities[0].id);
                host.PlacePlanActivity(2, p.activities[2].id);
            }
            restored.Save("tomorrow-draft");
            restored = Host(p, directory); restored.Load("tomorrow-draft");
            Assert.That(restored.State.Day, Is.EqualTo(1)); Assert.That(restored.Plan.startTick, Is.EqualTo(3));
            Assert.That(restored.Plan.confirmed, Is.False); Assert.That(restored.Plan.entries.Count, Is.EqualTo(3));
            foreach (var host in new[] { reference, restored })
            {
                var before = host.Session.Capture(); host.ConfirmPlan();
                Assert.That(host.Session.Capture().money, Is.EqualTo(before.money));
                Assert.That(host.Session.Capture().day, Is.EqualTo(before.day));
                Assert.That(host.Session.Capture().period, Is.EqualTo(before.period));
                host.Resume(); Assert.That(host.State.Day, Is.EqualTo(1)); Assert.That(host.State.Period, Is.EqualTo(1));
                host.Resume(); Assert.That(host.Wait, Is.EqualTo(SessionWait.Event));
                Assert.That(host.State.Day, Is.EqualTo(1)); Assert.That(host.State.Period, Is.EqualTo(2));
                Assert.Throws<InvalidOperationException>(() => host.PlacePlanActivity(2, p.activities[1].id));
            }
            var eventCursor = restored.ScheduleCursor;
            restored.Save("afternoon-event");
            restored = Host(p, directory); restored.Load("afternoon-event");
            Assert.That(restored.Wait, Is.EqualTo(SessionWait.Event));
            Assert.That(restored.ScheduleCursor, Is.EqualTo(eventCursor));
            Assert.That(restored.State.PresentationStepId, Is.EqualTo("next-day-first"));
            foreach (var host in new[] { reference, restored })
            {
                host.AdvanceDialogue("next-day-first"); host.AdvanceDialogue("next-day-second");
                host.Choose(story.choices[0].id);
                Assert.That(host.Wait, Is.EqualTo(SessionWait.Paused));
                Assert.That(host.State.Period, Is.EqualTo(2));
                host.Resume(); Assert.That(host.State.Day, Is.EqualTo(2));
                Assert.That(host.Wait, Is.EqualTo(SessionWait.Paused));
                host.NewPlan(); Assert.That(host.Plan.startTick, Is.EqualTo(6));
                for (var slot = 0; slot < 3; slot++) host.PlacePlanActivity(slot, p.activities[slot == 1 ? 1 : 2].id);
                host.ConfirmPlan(); for (var i = 0; i < 3; i++) host.Resume();
                Assert.That(host.State.Day, Is.EqualTo(3));
                Assert.That(host.Wait, Is.EqualTo(SessionWait.Paused));
                Assert.That(host.State.EndingId, Is.Empty);
            }
            Assert.That(SimulationIdentity.StateKey(restored.Session.Capture(), Array.Empty<ModuleState>()),
                Is.EqualTo(SimulationIdentity.StateKey(reference.Session.Capture(), Array.Empty<ModuleState>())));
            Assert.That(restored.Session.Capture().randomState, Is.EqualTo(reference.Session.Capture().randomState));
        }

        [Test] public void LongGameAcceptsTheNextMonthlyPlanAfterThirtyDays()
        {
            var p = GameCreation.Build(new NewGameDefinition { durationDays = 35 });
            p.time.planningDays = 30; p.time.periodNameKeys.Add("time.day"); p.events.Clear();
            foreach (var activity in p.activities) { activity.days = 1; activity.periods = 1; activity.checkFrequency = ActivityCheckFrequency.OncePerActivity; }
            p.activities[0].evaluation.enabled = true; p.activities[0].evaluation.statId = p.stats[0].id;
            var host = Host(p);
            Assert.That(host.Plan.capacity, Is.EqualTo(30)); Fill(host, p); host.ConfirmPlan();
            for (var i = 0; i < 30; i++) host.Resume();
            Assert.That(host.State.Day, Is.EqualTo(30)); Assert.That(host.Wait, Is.EqualTo(SessionWait.Paused));
            Assert.That(host.State.EndingId, Is.Empty);
            var money = host.State.Money; var wisdom = host.State.Stats[p.stats[0].id];
            var attempts = host.State.RecordAttempts.ToDictionary(x => x.Key, x => x.Value);
            Assert.That(attempts, Is.Not.Empty);
            host.NewPlan(); Assert.That(host.Plan.startTick, Is.EqualTo(30)); Assert.That(host.Plan.capacity, Is.EqualTo(5));
            Fill(host, p); host.ConfirmPlan();
            Assert.That(host.State.Day, Is.EqualTo(30)); Assert.That(host.State.Money, Is.EqualTo(money));
            Assert.That(host.State.Stats[p.stats[0].id], Is.EqualTo(wisdom));
            Assert.That(host.State.RecordAttempts, Is.EquivalentTo(attempts));
            host.Resume(); Assert.That(host.State.Day, Is.EqualTo(31)); Assert.That(host.Wait, Is.EqualTo(SessionWait.Paused));
            Assert.That(host.State.EndingId, Is.Empty);
        }
    }
}
