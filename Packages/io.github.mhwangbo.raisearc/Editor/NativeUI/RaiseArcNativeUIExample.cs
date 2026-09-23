using System;
using System.IO;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public static class NativeUIExample
    {
        private const string Templates = "Packages/io.github.mhwangbo.raisearc/Editor/Templates/NativeUI";
        [MenuItem("Window/RaiseArc/Samples/Create native UI example")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateFor(WeeklyPlannerExample.CreateProject());
        }
        public static string CreateFor(GameProjectAsset project)
        {
            var folder = BasicGameSetup.Folder(project);
            var scenePath = folder + "/NativeGame.unity";
            if (File.Exists(scenePath)) { EditorSceneManager.OpenScene(scenePath); return scenePath; }
            BasicGameSetup.CreatePresentationAssets(project);
            foreach (var name in new[] { "NativeGame.uss", "NativeGame.uxml", "ActivityCard.uxml", "PlanSlot.uxml", "Choice.uxml" })
                if (!AssetDatabase.CopyAsset(Templates + "/" + name, folder + "/" + name)) throw new IOException("Could not copy " + name);
            AssetDatabase.Refresh();
            RaiseArcUIBindings Map(string name, string document)
            {
                var map = ScriptableObject.CreateInstance<RaiseArcUIBindings>(); map.project = project;
                map.document = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "/" + document + ".uxml");
                AssetDatabase.CreateAsset(map, folder + "/" + name + ".asset"); return map;
            }
            void Link(RaiseArcUIBindings map, string target, UIData data = UIData.None, UICommand command = UICommand.None, string id = "", string panel = "")
                => map.connections.Add(new UIConnection { target = target, data = data, command = command, contentId = id, panel = panel });
            var cards = Map("ActivityBindings", "ActivityCard");
            Link(cards, "place-activity", command: UICommand.PlaceActivity);
            Link(cards, "activity-name", UIData.ActivityName); Link(cards, "activity-description", UIData.ActivityDescription);
            Link(cards, "activity-cost", UIData.ActivityCost); Link(cards, "activity-duration", UIData.ActivityDuration); Link(cards, "activity-image", UIData.ActivityImage);
            var slots = Map("SlotBindings", "PlanSlot");
            Link(slots, "select-slot", command: UICommand.SelectSlot); Link(slots, "slot-date", UIData.SlotDate);
            Link(slots, "slot-period", UIData.SlotPeriod); Link(slots, "slot-activity", UIData.SlotActivity);
            var choices = Map("ChoiceBindings", "Choice"); Link(choices, "choose", UIData.ChoiceText, UICommand.Choose);
            var main = Map("UIBindings", "NativeGame");
            Link(main, "background", UIData.BackgroundImage); Link(main, "character", UIData.CharacterImage);
            Link(main, "character-name", UIData.CharacterName); Link(main, "money", UIData.Money);
            Link(main, "day", UIData.Day); Link(main, "period", UIData.Period);
            Link(main, "wisdom", UIData.Stat, id: project.Read().stats[0].id); Link(main, "status", UIData.Status);
            Link(main, "plan-status", UIData.PlanStatus); Link(main, "confirm-reason", UIData.CommandReason, UICommand.ConfirmPlan);
            Link(main, "save", command: UICommand.Save); Link(main, "load", command: UICommand.Load); Link(main, "restart", command: UICommand.Restart);
            Link(main, "open-plan", command: UICommand.OpenPanel, panel: "plan-panel"); Link(main, "close-plan", command: UICommand.ClosePanel, panel: "plan-panel");
            Link(main, "confirm", command: UICommand.ConfirmPlan); Link(main, "run-next", command: UICommand.RunNext);
            Link(main, "plan-run-next", command: UICommand.RunNext);
            Link(main, "clear-slot", command: UICommand.ClearSlot); Link(main, "copy-day", command: UICommand.CopyNextDay); Link(main, "new-plan", command: UICommand.NewPlan);
            Link(main, "dialogue-panel", UIData.EventVisible); Link(main, "dialogue", UIData.Dialogue); Link(main, "speaker", UIData.Speaker);
            Link(main, "continue", command: UICommand.ContinueDialogue); Link(main, "ending-panel", UIData.EndingVisible); Link(main, "ending", UIData.Ending);
            var content = project.Read();
            void Text(RaiseArcUIBindings map, string target, string en, string ko)
            {
                var key = "sample.native." + target;
                var link = map.connections.Find(c => c.target == target);
                if (link == null) { link = new UIConnection { target = target }; map.connections.Add(link); }
                link.data = UIData.LocalizedText; link.contentId = key;
                foreach (var locale in new[] { "en", "ko" })
                    if (!content.translations.Exists(t => t.key == key && t.locale == locale))
                        content.translations.Add(new TranslationEntry { key = key, locale = locale, text = locale == "ko" ? ko : en });
            }
            Text(main, "day-label", "DAY", "날짜"); Text(main, "money-label", "MONEY", "소지금"); Text(main, "wisdom-label", "WISDOM", "지혜");
            Text(main, "save", "Save", "저장"); Text(main, "load", "Load", "불러오기"); Text(main, "restart", "Restart", "새로 시작");
            Text(main, "room-kicker", "YOUR NEXT CHAPTER", "나만의 다음 이야기");
            Text(main, "room-title", "A week of possibilities", "가능성으로 가득한 한 주");
            Text(main, "room-description", "Make room for learning, work and a little rest.", "배우고, 일하고, 쉬면서\n나만의 일주일을 만들어 보세요.");
            Text(main, "open-plan", "View & plan the week", "주간 계획 열기");
            Text(main, "run-next", "Begin next activity →", "다음 활동 시작 →"); Text(main, "plan-run-next", "Begin next activity →", "다음 활동 시작 →");
            Text(main, "plan-title", "Weekly plan", "주간 계획"); Text(main, "close-plan", "Back to garden", "정원으로");
            Text(main, "plan-instruction", "Choose a time slot, then an activity. Day numbers appear on the left.", "시간 칸을 고른 뒤 활동을 누르세요. 왼쪽 숫자는 날짜입니다.");
            Text(main, "activities-title", "ACTIVITIES", "활동 고르기");
            Text(main, "morning-title", "Morning", "오전"); Text(main, "afternoon-title", "Afternoon", "오후"); Text(main, "evening-title", "Evening", "저녁");
            Text(main, "clear-slot", "Clear slot", "선택 칸 비우기"); Text(main, "copy-day", "Copy to next day →", "다음 날로 복사 →");
            Text(main, "new-plan", "New week", "새 계획"); Text(main, "confirm", "Confirm week", "이 계획으로 확정");
            Text(main, "continue", "Continue →", "계속 →"); Text(main, "ending-title", "A NEW CHAPTER", "새로운 시작");
            Text(cards, "cost-label", "Cost", "비용"); Text(cards, "duration-label", "Time slots", "시간 칸");
            TimePlanEditor.Save(project, content, content.revision);
            main.repeats.Add(new UIRepeat { target = "activities", items = UIItems.Activities, template = cards });
            main.repeats.Add(new UIRepeat { target = "slots", items = UIItems.PlanSlots, template = slots });
            main.repeats.Add(new UIRepeat { target = "choices", items = UIItems.Choices, template = choices });
            foreach (var map in new[] { main, cards, slots, choices }) EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
            var projectPath = AssetDatabase.GetAssetPath(project);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var owner = new GameObject("Game — persistent session owner").AddComponent<RaiseArcGame>();
            owner.Configure(AssetDatabase.LoadAssetAtPath<GameProjectAsset>(projectPath), AssetDatabase.LoadAssetAtPath<PresentationSkin>(folder + "/ScreenSkin.asset"));
            var screen = new GameObject("Screen — your UI document");
            var document = screen.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(folder + "/Panel.asset");
            main = AssetDatabase.LoadAssetAtPath<RaiseArcUIBindings>(folder + "/UIBindings.asset");
            document.visualTreeAsset = main.document;
            screen.AddComponent<RaiseArcUIDocument>().Configure(owner, main, true);
            var camera = new GameObject("Screen camera", typeof(AudioListener)).AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 0;
            EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeObject = main;
            return scenePath;
        }
    }
}
