using System.IO;
using System.Linq;
using PrincessStudio.Core;
using RaiseArc.Core;
using RaiseArc.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public static class MonthlyUIExample
    {
        private const string Templates = "Packages/io.github.mhwangbo.raisearc/Editor/Templates/MonthlyUI";

        [MenuItem("Window/RaiseArc/Samples/Create monthly UI example")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var asset = GameCreation.Create(new NewGameDefinition { title = "A month of choices", durationDays = 30 });
            var content = asset.Read();
            content.time.planningDays = 30;
            content.time.periodNameKeys.Add("time.day");
            Text(content, "time.day", "Day", "하루");
            for (var i = 0; i < content.activities.Count; i++)
            {
                var activity = content.activities[i];
                activity.periods = 1;
                activity.checkFrequency = ActivityCheckFrequency.OncePerActivity;
                var en = new[] { "Study and build wisdom.", "Work and earn money.", "Rest and recover." };
                var ko = new[] { "공부하며 지혜를 쌓습니다.", "일해서 돈을 법니다.", "쉬면서 회복합니다." };
                Text(content, activity.descriptionKey, en[i], ko[i]);
            }
            var invitation = content.events[0];
            invitation.presentation.Add(new PresentationStep { id = "month.invitation.first", nameKey = "month.invitation.first", kind = PresentationStepKind.Dialogue, nextStepId = "month.invitation.second" });
            invitation.presentation.Add(new PresentationStep { id = "month.invitation.second", nameKey = "month.invitation.second", kind = PresentationStepKind.Dialogue, nextStepId = EventSequence.TerminalChoices });
            Text(content, "month.invitation.first", "A letter arrives in the garden.", "정원에 편지가 도착했어요.");
            Text(content, "month.invitation.second", "A friend invites you to spend tomorrow together.", "친구가 내일 함께 시간을 보내자고 초대합니다.");
            TimePlanEditor.Save(asset, content, content.revision);

            var projectPath = AssetDatabase.GetAssetPath(asset);
            var scenePath = NativeUIExample.CreateFor(asset);
            asset = AssetDatabase.LoadAssetAtPath<PrincessStudio.Unity.GameProjectAsset>(projectPath);
            var folder = Path.GetDirectoryName(scenePath).Replace('\\', '/');
            foreach (var name in new[] { "NativeGame.uxml", "NativeGame.uss", "PlanSlot.uxml" })
            {
                File.Copy(Templates + "/" + name, folder + "/" + name, true);
                AssetDatabase.ImportAsset(folder + "/" + name, ImportAssetOptions.ForceUpdate);
            }
            var main = AssetDatabase.LoadAssetAtPath<RaiseArcUIBindings>(folder + "/UIBindings.asset");
            var slots = AssetDatabase.LoadAssetAtPath<RaiseArcUIBindings>(folder + "/SlotBindings.asset");
            main.document = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "/NativeGame.uxml");
            slots.document = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "/PlanSlot.uxml");
            main.connections.RemoveAll(c => c.target == "period" || c.target == "morning-title" || c.target == "afternoon-title" ||
                c.target == "evening-title" || c.target == "copy-day" || c.target == "plan-run-next");
            slots.connections.RemoveAll(c => c.target == "slot-period");
            main.connections.Add(new UIConnection { target = "run-plan", data = UIData.LocalizedText, command = UICommand.RunPlan, contentId = "sample.month.run-plan" });
            main.connections.Add(new UIConnection { target = "plan-run-plan", data = UIData.LocalizedText, command = UICommand.RunPlan, contentId = "sample.month.run-plan" });
            content = asset.Read();
            Text(content, "sample.native.day-label", "DATE", "날짜");
            Text(content, "sample.native.room-kicker", "THIRTY DAYS", "서른 날의 이야기");
            Text(content, "sample.native.room-title", "A month of choices", "선택의 한 달");
            Text(content, "sample.native.room-description", "Plan each day, then follow the story as it unfolds.", "하루씩 편성하고, 이어지는 이야기를 만나 보세요.");
            Text(content, "sample.native.open-plan", "Open monthly calendar", "월간 달력 열기");
            Text(content, "sample.native.run-next", "Run one activity", "한 활동 진행");
            Text(content, "sample.native.plan-title", "Monthly calendar", "월간 달력");
            Text(content, "sample.native.plan-instruction", "Choose a date, then an activity. The plan stays fixed after confirmation.", "날짜를 고른 뒤 활동을 선택하세요. 확정 후에는 계획이 고정됩니다.");
            Text(content, "sample.native.new-plan", "New month", "새 달 계획");
            Text(content, "sample.native.confirm", "Confirm month", "월간 계획 확정");
            Text(content, "sample.month.run-plan", "Run planned month", "계획한 한 달 진행");
            TimePlanEditor.Save(asset, content, content.revision);
            EditorUtility.SetDirty(main); EditorUtility.SetDirty(slots); AssetDatabase.SaveAssets();
            ValidateBindings(main);
            EditorSceneManager.OpenScene(scenePath);
            Selection.activeObject = main;
        }

        public static void ValidateSamples()
        {
            var maps = AssetDatabase.FindAssets("t:RaiseArcUIBindings", new[] { "Assets/RaiseArcGames" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith("/UIBindings.asset"))
                .Select(AssetDatabase.LoadAssetAtPath<RaiseArcUIBindings>);
            foreach (var map in maps)
                if (map != null && map.connections.Any(c => c.command == UICommand.RunPlan))
                    ValidateBindings(map);
        }

        private static void ValidateBindings(RaiseArcUIBindings map)
        {
            var errors = map.Validate(map.document.CloneTree());
            if (errors.Count > 0) throw new System.InvalidOperationException(string.Join("\n", errors));
            Debug.Log("RaiseArc monthly UI bindings valid: " + AssetDatabase.GetAssetPath(map));
        }

        private static void Text(ProjectDefinition content, string key, string en, string ko)
        {
            foreach (var locale in new[] { "en", "ko" })
            {
                var entry = content.translations.Find(t => t.key == key && t.locale == locale);
                if (entry == null) content.translations.Add(new TranslationEntry { key = key, locale = locale, text = locale == "ko" ? ko : en });
                else entry.text = locale == "ko" ? ko : en;
            }
        }
    }
}
