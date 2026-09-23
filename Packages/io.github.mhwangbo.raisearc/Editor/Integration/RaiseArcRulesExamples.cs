using System;
using System.Collections.Generic;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Core;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    public static class RulesExamples
    {
        public static ProjectDefinition Definition(string theme)
        {
            var p = new ProjectDefinition { id = "rules-" + theme, durationDays = theme == "fitness" ? 6 : 4, startingMoney = 20 };
            p.stats.Add(new StatDefinition { id = "score", nameKey = "score", initial = theme == "athlete" ? 2 : 0, maximum = 100 });
            p.flags.Add(new FlagDefinition { id = "qualified", nameKey = "qualified", permanent = true });
            var rest = new ActivityDefinition { id = "rest", nameKey = "rest" }; p.activities.Add(rest);
            var train = new ActivityDefinition { id = "train", nameKey = "train" };
            train.effects.Add(new EffectSpec { kind = ValueKind.Stat, target = "score", value = theme == "school" ? 5 : theme == "athlete" ? 2 : 4 });
            p.activities.Add(train);
            var goal = new EndingDefinition { id = "goal", nameKey = "goal", priority = 10 };
            p.endings.Add(goal); p.endings.Add(new EndingDefinition { id = "fallback", nameKey = "fallback" });
            if (theme == "school")
            {
                p.items.Add(new ItemDefinition { id = "letter", nameKey = "letter" });
                p.activities.Add(new ActivityDefinition { id = "letter-request", nameKey = "letter-request", effects = new List<EffectSpec> {
                    new EffectSpec { kind = ValueKind.Item, target = "letter", operation = EffectOperation.Set, value = 1 } } });
                p.activities.Add(new ActivityDefinition { id = "exam", nameKey = "exam", evaluation = new EvaluationDefinition {
                    enabled = true, statId = "score", passingScore = 0, qualificationFlagId = "qualified" }, conditions = new List<ConditionSpec> {
                    new ConditionSpec { anyGroup = "admission", kind = ValueKind.Stat, target = "score", value = 5 },
                    new ConditionSpec { anyGroup = "admission", kind = ValueKind.Item, target = "letter", value = 1 } } });
                p.activities.Add(new ActivityDefinition { id = "reset-entry", nameKey = "reset-entry", effects = new List<EffectSpec> {
                    new EffectSpec { kind = ValueKind.Stat, target = "score", operation = EffectOperation.Set, value = 0 },
                    new EffectSpec { kind = ValueKind.Item, target = "letter", operation = EffectOperation.Set, value = 0 } } });
                goal.conditions.Add(new ConditionSpec { kind = ValueKind.Flag, target = "qualified", value = 1 });
                goal.conditions.Add(new ConditionSpec { kind = ValueKind.Stat, target = "score", comparison = Comparison.Equal, value = 0 });
                goal.conditions.Add(new ConditionSpec { kind = ValueKind.Item, target = "letter", comparison = Comparison.Equal, value = 0 });
            }
            else if (theme == "athlete")
            {
                p.activities.Add(new ActivityDefinition { id = "trial", nameKey = "trial", evaluation = new EvaluationDefinition { enabled = true, statId = "score", passingScore = 4 } });
                p.activities.Add(new ActivityDefinition { id = "final", nameKey = "final", evaluation = new EvaluationDefinition {
                    enabled = true, statId = "score", passingScore = 4, qualificationFlagId = "qualified" }, conditions = new List<ConditionSpec> {
                    new ConditionSpec { kind = ValueKind.RecordBest, target = "trial", value = 4 },
                    new ConditionSpec { kind = ValueKind.RecordTotal, target = "trial", value = 8 },
                    new ConditionSpec { kind = ValueKind.RecordPasses, target = "trial", value = 2 } } });
                goal.conditions.Add(new ConditionSpec { kind = ValueKind.Flag, target = "qualified", value = 1 });
            }
            else if (theme == "fitness")
            {
                p.modifiers.Add(new TimedModifierDefinition { id = "fatigue", nameKey = "fatigue", statId = "score", activityId = "train", additiveBonus = -3 });
                p.activities.Add(new ActivityDefinition { id = "fatigue-start", nameKey = "fatigue-start", effects = new List<EffectSpec> {
                    new EffectSpec { kind = ValueKind.ModifierDays, target = "fatigue", operation = EffectOperation.Set, value = 3 },
                    new EffectSpec { kind = ValueKind.Flag, target = "qualified", operation = EffectOperation.Set, value = 1 } } });
                goal.conditions.Add(new ConditionSpec { kind = ValueKind.Flag, target = "qualified", value = 1 });
                goal.conditions.Add(new ConditionSpec { kind = ValueKind.Stat, target = "score", comparison = Comparison.Equal, value = 13 });
                goal.conditions.Add(new ConditionSpec { kind = ValueKind.ModifierDays, target = "fatigue", comparison = Comparison.Equal, value = 0 });
            }
            else throw new ArgumentException("Unknown rules example: " + theme);
            var labels = new Dictionary<string, string[]> {
                ["character.daughter.name"] = new[] { "Learner", "육성 대상" }, ["score"] = new[] { "Score", "점수" },
                ["qualified"] = new[] { "Permanent qualification", "영구 자격" }, ["rest"] = new[] { "Rest", "휴식" },
                ["train"] = new[] { "Train", "훈련" }, ["goal"] = new[] { "Goal reached", "목표 달성" }, ["fallback"] = new[] { "Keep practicing", "다음 기회에" },
                ["letter"] = new[] { "Recommendation letter", "추천서" }, ["letter-request"] = new[] { "Obtain a letter", "추천서 받기" },
                ["exam"] = new[] { "Qualification exam", "자격 시험" }, ["reset-entry"] = new[] { "Remove entry requirements", "응시 요건 제거" },
                ["trial"] = new[] { "Record trial", "기록 평가" }, ["final"] = new[] { "Final evaluation", "최종 평가" },
                ["fatigue"] = new[] { "Temporary fatigue", "기간 피로" }, ["fatigue-start"] = new[] { "Activate fatigue (3 days)", "피로 시작 (3일)" }
            };
            foreach (var pair in labels) for (var i = 0; i < 2; i++) p.translations.Add(new TranslationEntry { key = pair.Key, locale = i == 0 ? "en" : "ko", text = pair.Value[i] });
            new ContentIndex(p, assignIdentities: true);
            return p;
        }

        [MenuItem("Window/RaiseArc/Samples/Rules/Magic school")]
        public static void School() => Create("school");
        [MenuItem("Window/RaiseArc/Samples/Rules/Athlete")]
        public static void Athlete() => Create("athlete");
        [MenuItem("Window/RaiseArc/Samples/Rules/Fitness")]
        public static void Fitness() => Create("fitness");
        private static void Create(string theme)
        {
            if (!AssetDatabase.IsValidFolder("Assets/RaiseArcRules")) AssetDatabase.CreateFolder("Assets", "RaiseArcRules");
            var definition = Definition(theme); definition.id += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(definition);
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath("Assets/RaiseArcRules/" + theme + ".asset"));
            var test = EndingTests.New(asset, "goal"); test.name = theme + " rules";
            EndingTests.Save(asset, test, null, 0);
            AssetDatabase.SaveAssets(); Selection.activeObject = asset;
            PrincessStudio.Editor.StudioWindow.OpenProject(asset);
        }
    }
}
