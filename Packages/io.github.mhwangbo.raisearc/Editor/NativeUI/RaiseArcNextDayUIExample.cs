using System;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Core;
using RaiseArc.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public static class RaiseArcNextDayUIExample
    {
        private const string Templates = "Packages/io.github.mhwangbo.raisearc/Editor/Templates/NextDayUI";

        [MenuItem("Window/RaiseArc/Samples/Create next-day UI example")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var asset = GameCreation.Create(new NewGameDefinition { title = "Days ahead", durationDays = 5 });
            var projectPath = AssetDatabase.GetAssetPath(asset);
            var content = asset.Read();
            content.time.planningDays = 1;
            content.time.periodNameKeys.AddRange(new[] { "time.morning", "time.afternoon", "time.evening" });
            Text(content, "time.morning", "Morning", "오전");
            Text(content, "time.afternoon", "Afternoon", "오후");
            Text(content, "time.evening", "Evening", "저녁");
            for (var i = 0; i < content.activities.Count; i++)
            {
                var activity = content.activities[i];
                activity.days = 1;
                activity.periods = 1;
                activity.checkFrequency = ActivityCheckFrequency.OncePerActivity;
                var en = new[] { "Study and build wisdom.", "Work and earn money.", "Rest and recover." };
                var ko = new[] { "공부하며 지혜를 쌓습니다.", "일해서 돈을 법니다.", "쉬면서 회복합니다." };
                Text(content, activity.descriptionKey, en[i], ko[i]);
            }
            var invitation = content.events[0];
            invitation.evaluateEachPeriod = true;
            invitation.conditions.Clear();
            invitation.conditions.Add(new ConditionSpec { id = "next-day-wisdom", kind = ValueKind.Stat,
                target = content.stats[0].id, comparison = Comparison.AtLeast, value = content.stats[0].initial + 5 });
            invitation.presentation.Add(new PresentationStep { id = "next-day-first", nameKey = "next-day.first",
                kind = PresentationStepKind.Dialogue, nextStepId = "next-day-second" });
            invitation.presentation.Add(new PresentationStep { id = "next-day-second", nameKey = "next-day.second",
                kind = PresentationStepKind.Dialogue, nextStepId = EventSequence.TerminalChoices });
            Text(content, "next-day.first", "A letter arrives during the afternoon.", "오후에 편지가 도착했어요.");
            Text(content, "next-day.second", "A friend invites you to spend time together.", "친구가 함께 시간을 보내자고 초대합니다.");
            TimePlanEditor.Save(asset, content, content.revision);

            asset = AssetDatabase.LoadAssetAtPath<GameProjectAsset>(projectPath);
            var scenePath = NativeUIExample.CreateFor(asset);
            asset = AssetDatabase.LoadAssetAtPath<GameProjectAsset>(projectPath);
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
            main.connections.RemoveAll(c => c.target == "day-label" || c.target == "day" || c.target == "period" ||
                c.target == "morning-title" || c.target == "afternoon-title" || c.target == "evening-title" ||
                c.target == "copy-day" || c.target == "plan-run-next");
            main.connections.Add(new UIConnection { target = "date-position", data = UIData.DayPosition });
            main.connections.Add(new UIConnection { target = "target-date", data = UIData.PlanTargetDate });
            main.connections.Add(new UIConnection { target = "plan-target-date", data = UIData.PlanTargetDate });
            main.connections.Add(new UIConnection { target = "run-plan", data = UIData.LocalizedText,
                command = UICommand.RunPlan, contentId = "sample.next-day.run-plan" });
            main.connections.Add(new UIConnection { target = "plan-run-plan", data = UIData.LocalizedText,
                command = UICommand.RunPlan, contentId = "sample.next-day.run-plan" });

            content = asset.Read();
            Text(content, "sample.native.room-kicker", "FIVE DAYS", "이어지는 닷새");
            Text(content, "sample.native.room-title", "Days ahead", "이어지는 하루들");
            Text(content, "sample.native.room-description",
                "Before day 1, plan its three periods. Confirming costs no time. Starting the plan begins morning; after evening, plan the following day.",
                "첫날 전에는 오전·오후·저녁을 편성하세요. 확정은 시간을 쓰지 않습니다. 계획을 시작하면 오전이 진행되고, 저녁이 끝나면 다음 날을 편성합니다.");
            Text(content, "sample.native.open-plan", "Open next-day plan", "다음 날 계획 열기");
            Text(content, "sample.native.run-next", "Run one period", "한 시간대 진행");
            Text(content, "sample.native.plan-title", "Next-day plan", "다음 날 계획");
            Text(content, "sample.native.plan-instruction", "Choose morning, afternoon and evening, then confirm. The confirmed slots stay locked.",
                "오전·오후·저녁을 편성한 뒤 확정하세요. 확정된 칸은 바꿀 수 없습니다.");
            Text(content, "sample.native.new-plan", "Plan following day", "그다음 날 편성");
            Text(content, "sample.native.confirm", "Confirm this day", "하루 계획 확정");
            Text(content, "sample.next-day.run-plan", "Start or resume planned day", "계획한 하루 시작·재개");
            Text(content, "sample.next-day.date-position-label", "CURRENT POSITION", "현재 진행 위치");
            Text(content, "sample.next-day.target-date-label", "PLAN DATE", "계획 대상 날짜");
            foreach (var pair in new[] { ("date-position-label", "sample.next-day.date-position-label"),
                ("target-date-label", "sample.next-day.target-date-label") })
                main.connections.Add(new UIConnection { target = pair.Item1, data = UIData.LocalizedText, contentId = pair.Item2 });
            TimePlanEditor.Save(asset, content, content.revision);
            EditorUtility.SetDirty(main); EditorUtility.SetDirty(slots); AssetDatabase.SaveAssets();
            var errors = main.Validate(main.document.CloneTree());
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            Debug.Log("RaiseArc next-day UI bindings valid: " + AssetDatabase.GetAssetPath(main));
            EditorSceneManager.OpenScene(scenePath);
            Selection.activeObject = main;
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
