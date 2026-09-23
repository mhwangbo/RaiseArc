using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Localization;
using NUnit.Framework;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Editor;
using RaiseArc.Analysis;
using RaiseArc.Unity;
using PrincessStudio.Samples;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace PrincessStudio.Editor.Graph.Tests
{
    public sealed class PlaythroughSharedFlowTests
    {
        [Test]
        public void ComposerLoadClearsOnlySuccessfullyRestoredLocalPlanDraft()
        {
            var project = new ProjectDefinition { id = "composer-load-draft", durationDays = 2 };
            project.activities.Add(new ActivityDefinition { id = "rest", nameKey = "rest", days = 1 });
            project.endings.Add(new EndingDefinition { id = "complete", nameKey = "complete" });
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(project);
            var directory = System.IO.Path.Combine(Application.temporaryCachePath, "composer-load-draft-" + Guid.NewGuid().ToString("N"));
            var go = new GameObject("ComposerLoadDraft");
            try
            {
                var host = new RaiseArcSessionHost(asset, directory, 17);
                host.Save("slot1");
                var controller = go.AddComponent<SampleGameController>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(SampleGameController);
                type.GetField("host", flags).SetValue(controller, host);
                var draft = (List<string>)type.GetField("plannedActivities", flags).GetValue(controller);
                draft.Add("rest");
                var load = type.GetMethod("LoadAndClearDraft", flags);
                Assert.Throws<TargetInvocationException>(() => load.Invoke(controller, new object[] { "missing" }));
                Assert.That(draft, Is.EqualTo(new[] { "rest" }));
                load.Invoke(controller, new object[] { "slot1" });
                Assert.That(draft, Is.Empty);
                Assert.That(host.State.Day, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(asset);
                if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
            }
        }
        [Test]
        public void ComposerCompositionPreviewShowsExpandedSharedChoice()
        {
            var project = new ProjectDefinition { id = "shared-composer-preview", durationDays = 2 };
            project.activities.Add(new ActivityDefinition { id = "rest", nameKey = "rest", days = 1 });
            project.endings.Add(new EndingDefinition { id = "complete", nameKey = "complete" });
            project.translations.Add(new TranslationEntry { key = "shared-line", locale = project.defaultLocale, text = "Shared choice line" });
            project.translations.Add(new TranslationEntry { key = "accept-label", locale = project.defaultLocale, text = "Accept invitation" });
            var shared = new EventDefinition { id = "shared", nameKey = "shared", callOnly = true };
            var line = new PresentationStep { id = "line", nameKey = "shared-line", kind = PresentationStepKind.Choice,
                nextStepId = EventSequence.End };
            line.choices.Add(new ChoiceDefinition { id = "accept", nameKey = "accept-label" });
            shared.presentation.Add(line); project.events.Add(shared);
            var main = new EventDefinition { id = "main", nameKey = "main" };
            main.presentation.Add(new PresentationStep { id = "call", nameKey = "call", sharedEventId = "shared",
                nextStepId = EventSequence.End });
            project.events.Add(main);
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(project);
            var go = new GameObject("SharedComposerPreview");
            const string tableFolder = "Assets/SharedComposerPreviewLocalization";
            try
            {
                AssetDatabase.CreateFolder("Assets", "SharedComposerPreviewLocalization");
                var collection = LocalizationEditorSettings.CreateStringTableCollection("Princess." + project.id, tableFolder);
                var localeId = new LocaleIdentifier(LocalizationSettings.SelectedLocale.Identifier.Code);
                var table = (StringTable)(collection.GetTable(localeId) ?? collection.AddNewTable(localeId));
                table.AddEntry("shared-line", "Shared choice line");
                table.AddEntry("accept-label", "Accept invitation");
                AssetDatabase.SaveAssets();
                var host = new RaiseArcSessionHost(asset, System.IO.Path.Combine(Application.temporaryCachePath, "shared-composer-preview"), 17);
                host.StartActivity("rest");
                Assert.That(host.State.PresentationStepId, Is.EqualTo("call.call.line"));
                var controller = go.AddComponent<SampleGameController>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(SampleGameController);
                var root = new VisualElement();
                type.GetField("project", flags).SetValue(controller, project);
                type.GetField("host", flags).SetValue(controller, host);
                type.GetField("playbackEvents", flags).SetValue(controller,
                    project.events.ConvertAll(e => RaiseArc.Core.RaiseArcFlowReuse.Expand(project, e)));
                type.GetField("screenDefinition", flags).SetValue(controller,
                    new GameScreenDefinition { compositionPreview = true, parts = ScreenComposition.WeeklyStarter() });
                type.GetField("root", flags).SetValue(controller, root);
                type.GetMethod("Draw", flags).Invoke(controller, null);
                Assert.That(root.Query<Label>().ToList().Any(l => l.text == "Shared choice line"), Is.True);
                Assert.That(root.Query<Button>().ToList().Any(b =>
                    b.Query<Label>().ToList().Any(l => l.text == "Accept invitation")), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(asset);
                AssetDatabase.DeleteAsset(tableFolder);
            }
        }
        [Test]
        public void StudioPreviewShowsExpandedSharedChoice()
        {
            var project = new ProjectDefinition { id = "shared-studio-preview", durationDays = 2 };
            project.activities.Add(new ActivityDefinition { id = "rest", nameKey = "rest", days = 1 });
            project.endings.Add(new EndingDefinition { id = "complete", nameKey = "complete" });
            project.translations.Add(new TranslationEntry { key = "shared-line", locale = project.defaultLocale, text = "Shared choice line" });
            project.translations.Add(new TranslationEntry { key = "accept-label", locale = project.defaultLocale, text = "Accept invitation" });
            var shared = new EventDefinition { id = "shared", nameKey = "shared", callOnly = true };
            var line = new PresentationStep { id = "line", nameKey = "shared-line", kind = PresentationStepKind.Choice,
                nextStepId = EventSequence.End };
            line.choices.Add(new ChoiceDefinition { id = "accept", nameKey = "accept-label" });
            shared.presentation.Add(line); project.events.Add(shared);
            var main = new EventDefinition { id = "main", nameKey = "main" };
            main.presentation.Add(new PresentationStep { id = "call", nameKey = "call", sharedEventId = "shared",
                nextStepId = EventSequence.End });
            project.events.Add(main);
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(project);
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/SharedPreviewFixture.asset");
            AssetDatabase.CreateAsset(asset, path);
            StudioWindow window = null;
            try
            {
                var session = new GameSession(project, null, 17);
                session.PerformActivity("rest");
                Assert.That(session.State.PendingEventId, Is.EqualTo("main"));
                Assert.That(session.State.PresentationStepId, Is.EqualTo("call.call.line"));
                Assert.That(session.AvailableChoices(), Does.Contain("call.call.accept"));
                window = StudioWindow.OpenProject(asset, "Test play");
                typeof(StudioWindow).GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, session);
                typeof(StudioWindow).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                Assert.That(window.rootVisualElement.Query<Label>().ToList().Any(l => l.text == "Shared choice line"), Is.True);
                Assert.That(window.rootVisualElement.Query<Button>().ToList().Any(b => b.text == "Accept invitation"), Is.True);
            }
            finally
            {
                if (window != null) window.Close();
                AssetDatabase.DeleteAsset(path);
            }
        }
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
