using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PrincessStudio.Core;
using PrincessStudio.Editor;
using PrincessStudio.Unity;
using RaiseArc.Unity;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;
using RaiseArc.Editor;

namespace RaiseArc.Editor.Tests
{
    public sealed class ScreenCompositionTests
    {
        [Test]
        public void SyncingSecondGameDoesNotChangeFirstGamesTablesOrSharedLocaleSettings()
        {
            var idA = "isolation-a-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var idB = "isolation-b-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var first = ScriptableObject.CreateInstance<GameProjectAsset>();
            var second = ScriptableObject.CreateInstance<GameProjectAsset>();
            var a = GameCreation.Build(new NewGameDefinition()); a.id = idA;
            var b = GameCreation.Build(new NewGameDefinition()); b.id = idB;
            b.defaultLocale = b.fallbackLocale = "ko";
            first.Write(a); second.Write(b);
            var originalSettings = LocalizationEditorSettings.ActiveLocalizationSettings;
            var originalEnglish = LocalizationEditorSettings.GetLocale("en");
            var originalKorean = LocalizationEditorSettings.GetLocale("ko");
            try
            {
                LocalizationTableBridge.Sync(first);
                var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
                var english = LocalizationEditorSettings.GetLocale("en");
                var korean = LocalizationEditorSettings.GetLocale("ko");
                var firstTables = LocalizationEditorSettings.GetStringTableCollection("Princess." + idA);
                Assert.That(firstTables, Is.Not.Null);
                var settingsBefore = EditorJsonUtility.ToJson(settings);
                var englishBefore = EditorJsonUtility.ToJson(english);
                var koreanBefore = EditorJsonUtility.ToJson(korean);
                var tablesBefore = EditorJsonUtility.ToJson(firstTables);
                var sharedBefore = EditorJsonUtility.ToJson(firstTables.SharedData);

                LocalizationTableBridge.Sync(second);

                Assert.That(EditorJsonUtility.ToJson(settings), Is.EqualTo(settingsBefore));
                Assert.That(EditorJsonUtility.ToJson(english), Is.EqualTo(englishBefore));
                Assert.That(EditorJsonUtility.ToJson(korean), Is.EqualTo(koreanBefore));
                Assert.That(EditorJsonUtility.ToJson(firstTables), Is.EqualTo(tablesBefore));
                Assert.That(EditorJsonUtility.ToJson(firstTables.SharedData), Is.EqualTo(sharedBefore));
                Assert.That(LocalizationEditorSettings.GetStringTableCollection("Princess." + idB), Is.Not.Null);
            }
            finally
            {
                if (originalEnglish == null)
                {
                    var locale = LocalizationEditorSettings.GetLocale("en");
                    if (locale != null) LocalizationEditorSettings.RemoveLocale(locale);
                }
                if (originalKorean == null)
                {
                    var locale = LocalizationEditorSettings.GetLocale("ko");
                    if (locale != null) LocalizationEditorSettings.RemoveLocale(locale);
                }
                LocalizationEditorSettings.ActiveLocalizationSettings = originalSettings;
                AssetDatabase.DeleteAsset("Assets/PrincessStudioContent/Localization/" + idA);
                AssetDatabase.DeleteAsset("Assets/PrincessStudioContent/Localization/" + idB);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }
        [Test]
        public void BasicSetupPreservesUnsavedScreenAcrossUndoRefreshAndProjectSwitch()
        {
            var first = ScriptableObject.CreateInstance<GameProjectAsset>(); first.Write(GameCreation.Build(new NewGameDefinition()));
            var second = ScriptableObject.CreateInstance<GameProjectAsset>(); second.Write(GameCreation.Build(new NewGameDefinition()));
            var firstPath = AssetDatabase.GenerateUniqueAssetPath("Assets/RaiseArcBasicSetupFirst.asset");
            var secondPath = AssetDatabase.GenerateUniqueAssetPath("Assets/RaiseArcBasicSetupSecond.asset");
            AssetDatabase.CreateAsset(first, firstPath); AssetDatabase.CreateAsset(second, secondPath);
            var window = ScriptableObject.CreateInstance<BasicGameSetup>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(BasicGameSetup).GetField("project", flags).SetValue(window, first);
                window.Show(); window.CreateGUI();
                var draft = (GameScreenDefinition)typeof(BasicGameSetup).GetField("screenDraft", flags).GetValue(window);
                var originalSize = draft.fontSize;
                draft.fontSize = originalSize + 1;
                typeof(BasicGameSetup).GetMethod("ReloadSetup", flags).Invoke(window, null);
                Assert.That(typeof(BasicGameSetup).GetField("screenDraft", flags).GetValue(window), Is.SameAs(draft));
                window.rootVisualElement.Q<ObjectField>().value = second;
                Assert.That(typeof(BasicGameSetup).GetField("project", flags).GetValue(window), Is.SameAs(first));
                Assert.That(draft.fontSize, Is.EqualTo(originalSize + 1));
                draft.fontSize = originalSize;
                typeof(BasicGameSetup).GetMethod("ReloadSetup", flags).Invoke(window, null);
                window.rootVisualElement.Q<ObjectField>().value = second;
                Assert.That(typeof(BasicGameSetup).GetField("project", flags).GetValue(window), Is.SameAs(second));
            }
            finally { window.Close(); AssetDatabase.DeleteAsset(firstPath); AssetDatabase.DeleteAsset(secondPath); }
        }
        [Test]
        public void UnsavedLayoutSurvivesReloadAndRejectsSettingsSwitch()
        {
            var project = ScriptableObject.CreateInstance<GameProjectAsset>(); project.Write(GameCreation.Build(new NewGameDefinition()));
            var first = ScriptableObject.CreateInstance<RaiseArcGameScreenSettings>();
            var second = ScriptableObject.CreateInstance<RaiseArcGameScreenSettings>();
            var definition = new GameScreenDefinition { compositionPreview = true, parts = ScreenComposition.WeeklyStarter() };
            first.Write(project, null, definition); second.Write(project, null, definition);
            var window = ScriptableObject.CreateInstance<ScreenComposer>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(ScreenComposer).GetField("settings", flags).SetValue(window, first);
                window.Show(); window.CreateGUI();
                var draft = (GameScreenDefinition)typeof(ScreenComposer).GetField("draft", flags).GetValue(window);
                draft.parts[0].bounds.x += 12;
                typeof(ScreenComposer).GetMethod("Changed", flags).Invoke(window, null);
                typeof(ScreenComposer).GetMethod("Reload", flags).Invoke(window, null);
                Assert.That(typeof(ScreenComposer).GetField("draft", flags).GetValue(window), Is.SameAs(draft));
                window.rootVisualElement.Q<ObjectField>().value = second;
                Assert.That(typeof(ScreenComposer).GetField("settings", flags).GetValue(window), Is.SameAs(first));
                Assert.That(draft.parts[0].bounds.x, Is.EqualTo(definition.parts[0].bounds.x + 12));
            }
            finally { window.Close(); UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); UnityEngine.Object.DestroyImmediate(project); }
        }
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
