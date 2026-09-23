using System.IO;
using PrincessStudio.Core;
using RaiseArc.Core;
using RaiseArc.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RaiseArc.Editor
{
    public static class WeeklyPlannerExample
    {
        [MenuItem("Window/RaiseArc/Samples/Create weekly planner")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var asset = CreateProject();
            OpenComposer(asset);
        }
        public static PrincessStudio.Unity.GameProjectAsset CreateProject()
        {
            var asset = GameCreation.Create(new NewGameDefinition { title = "A week of possibilities", durationDays = 7 });
            var p = asset.Read();
            p.time.periodNameKeys.AddRange(new[] { "time.morning", "time.afternoon", "time.evening" });
            var en = new[] { "Morning", "Afternoon", "Evening" }; var ko = new[] { "오전", "오후", "저녁" };
            for (var i = 0; i < en.Length; i++)
                foreach (var locale in p.locales) p.translations.Add(new TranslationEntry { key = p.time.periodNameKeys[i], locale = locale, text = locale == "ko" ? ko[i] : en[i] });
            var descriptionsEn = new[] { "Learn and improve Wisdom.", "Earn money through work.", "Recover with a quiet break." };
            var descriptionsKo = new[] { "공부하며 지혜를 키웁니다.", "일해서 돈을 법니다.", "조용히 쉬며 회복합니다." };
            for (var i = 0; i < p.activities.Count; i++)
            {
                var activity = p.activities[i]; activity.periods = 1; activity.checkFrequency = ActivityCheckFrequency.OncePerActivity;
                if (i >= descriptionsEn.Length) continue;
                foreach (var locale in p.locales)
                    p.translations.Find(t => t.key == activity.descriptionKey && t.locale == locale).text = locale == "ko" ? descriptionsKo[i] : descriptionsEn[i];
            }
            p.events[0].conditions[0].value = 2;
            TimePlanEditor.Save(asset, p, p.revision);
            return asset;
        }
        private static void OpenComposer(PrincessStudio.Unity.GameProjectAsset asset)
        {
            var definition = new GameScreenDefinition { compositionPreview = true, parts = ScreenComposition.WeeklyStarter() };
            var scene = BasicGameSetup.CreateConfigured(asset, null, null, definition);
            EditorSceneManager.OpenScene(scene);
            var settings = AssetDatabase.LoadAssetAtPath<RaiseArcGameScreenSettings>(Path.GetDirectoryName(scene).Replace('\\', '/') + "/ScreenSettings.asset");
            ScreenComposer.OpenSettings(settings);
            Selection.activeObject = settings;
        }
    }
}
