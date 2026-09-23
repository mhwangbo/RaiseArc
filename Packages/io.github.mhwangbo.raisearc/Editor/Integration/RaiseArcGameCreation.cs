using System;
using System.Collections.Generic;
using System.IO;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    [Serializable] public sealed class NewGameStat
    {
        public string name = "Wisdom";
        public int initial = 10;
    }
    [Serializable] public sealed class NewGameDefinition
    {
        public string title = "My raising game", characterName = "Elara";
        public int startingAge = 10, durationDays = 42, startingMoney = 60;
        public bool includeStarter = true;
        public List<NewGameStat> stats = new List<NewGameStat> { new NewGameStat(), new NewGameStat { name = "Vitality" }, new NewGameStat { name = "Charm" } };
    }

    public static class GameCreation
    {
        public static ProjectDefinition Build(NewGameDefinition input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.title) || string.IsNullOrWhiteSpace(input.characterName))
                throw new ArgumentException("Enter a game title and character name. / 게임 제목과 캐릭터 이름을 입력하세요.");
            if (input.durationDays < 1 || input.durationDays > 36500 || input.startingAge < 0 || input.startingAge > 150 || input.startingMoney < 0)
                throw new ArgumentException("Check duration, age and starting money. / 기간·시작 나이·소지금을 확인하세요.");
            if (input.stats == null || input.stats.Count == 0 || input.stats.Count > 20)
                throw new ArgumentException("Add 1–20 stats. / 능력치를 1~20개 지정하세요.");
            var p = new ProjectDefinition { id = "game-" + Guid.NewGuid().ToString("N"), durationDays = input.durationDays, startingMoney = input.startingMoney };
            string Id(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");
            void Text(string key, string en, string ko)
            {
                p.translations.Add(new TranslationEntry { key = key, locale = "en", text = en });
                p.translations.Add(new TranslationEntry { key = key, locale = "ko", text = ko });
            }
            void Name(Definition d, string en, string ko)
            {
                AuthoringService.GenerateKeys(d, "content"); d.descriptionKey = "content." + d.id + ".description";
                Text(d.nameKey, en, ko); Text(d.descriptionKey, "", "");
            }
            Text("ui.game.title", input.title, input.title);
            p.character.id = Id("character"); p.character.startingAge = input.startingAge;
            Name(p.character, input.characterName, input.characterName);
            foreach (var stat in input.stats)
            {
                if (stat == null || string.IsNullOrWhiteSpace(stat.name) || stat.initial < 0 || stat.initial > 10000)
                    throw new ArgumentException("Each stat needs a name and initial value from 0 to 10000. / 능력치 이름과 0~10000 범위의 초기값을 입력하세요.");
                var definition = new StatDefinition { id = Id("stat"), initial = stat.initial, maximum = Math.Max(100, stat.initial + 30) };
                Name(definition, stat.name, stat.name); p.stats.Add(definition);
            }
            if (!input.includeStarter) return p;
            var days = input.durationDays % 7 == 0 ? 7 : 1;
            var train = new ActivityDefinition { id = Id("activity"), days = days, cost = Math.Min(5, input.startingMoney / 6) };
            Name(train, "Study", "공부"); train.effects.Add(new EffectSpec { id = Id("effect"), kind = ValueKind.Stat, target = p.stats[0].id, value = 5 }); p.activities.Add(train);
            var work = new ActivityDefinition { id = Id("activity"), days = days, category = ActivityKind.Work, income = 10 };
            Name(work, "Work", "일하기"); p.activities.Add(work);
            var rest = new ActivityDefinition { id = Id("activity"), days = days, category = ActivityKind.Rest };
            Name(rest, "Rest", "쉬기"); p.activities.Add(rest);
            var story = new EventDefinition { id = Id("event") }; Name(story, "An invitation", "초대장");
            Text(story.descriptionKey + ".body", "A friend invites you to spend the afternoon together.", "친구가 함께 오후를 보내자고 초대했어요.");
            story.descriptionKey += ".body";
            story.conditions.Add(new ConditionSpec { id = Id("condition"), kind = ValueKind.Day, comparison = Comparison.AtLeast, value = Math.Min(input.durationDays, days * 2) });
            var accept = new ChoiceDefinition { id = Id("choice") }; Name(accept, "Accept the invitation", "초대를 받아들인다");
            accept.effects.Add(new EffectSpec { id = Id("effect"), kind = ValueKind.Money, value = 5 }); story.choices.Add(accept);
            var decline = new ChoiceDefinition { id = Id("choice") }; Name(decline, "Stay home", "집에서 쉰다"); story.choices.Add(decline); p.events.Add(story);
            var success = new EndingDefinition { id = Id("ending"), priority = 10 }; Name(success, "A promising future", "가능성 있는 미래");
            success.conditions.Add(new ConditionSpec { id = Id("condition"), kind = ValueKind.Stat, target = p.stats[0].id, value = p.stats[0].initial + 5 * Math.Min(4, input.durationDays / days), comparison = Comparison.AtLeast }); p.endings.Add(success);
            var fallback = new EndingDefinition { id = Id("ending") }; Name(fallback, "A new beginning", "새로운 시작"); p.endings.Add(fallback);
            return p;
        }

        public static GameProjectAsset Create(NewGameDefinition input)
        {
            var data = Build(input);
            const string folder = "Assets/RaiseArcGames";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var safeName = string.Concat(input.title.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "MyGame";
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(data);
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + safeName + ".asset"));
            Undo.RegisterCreatedObjectUndo(asset, "Create RaiseArc game"); AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
