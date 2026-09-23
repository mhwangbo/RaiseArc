using System.Collections.Generic;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    public static class EndingTestExample
    {
        public static ProjectDefinition Definition()
        {
            var project = new ProjectDefinition { id = "ending-test-school", durationDays = 3, startingMoney = 2 };
            project.stats.Add(new StatDefinition { id = "wisdom", nameKey = "wisdom", initial = 0, maximum = 20 });
            project.activities.Add(new ActivityDefinition { id = "study", nameKey = "study", days = 1, cost = 1,
                effects = new List<EffectSpec> { new EffectSpec { id = "lesson", kind = ValueKind.Stat, target = "wisdom", value = 2 } } });
            project.activities.Add(new ActivityDefinition { id = "work", nameKey = "work", days = 1, income = 2 });
            project.activities.Add(new ActivityDefinition { id = "lottery", nameKey = "lottery", days = 1,
                effects = new List<EffectSpec> { new EffectSpec { id = "shortcut", kind = ValueKind.Stat, target = "wisdom", value = 10 } } });
            project.endings.Add(new EndingDefinition { id = "scholar", nameKey = "scholar", priority = 10,
                conditions = new List<ConditionSpec> { new ConditionSpec { id = "qualified", kind = ValueKind.Stat, target = "wisdom", value = 4 } } });
            project.endings.Add(new EndingDefinition { id = "ordinary", nameKey = "ordinary" });
            return project;
        }
        [MenuItem("Window/RaiseArc/Samples/Create Ending Test Example")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder("Assets/RaiseArcTests")) AssetDatabase.CreateFolder("Assets", "RaiseArcTests");
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(Definition());
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath("Assets/RaiseArcTests/EndingSchool.asset"));
            var definition = EndingTests.New(asset, "scholar"); definition.name = "Scholar without lottery / 복권 없이 학자";
            definition.settings.forbiddenActivities.Add("lottery");
            var test = EndingTests.Save(asset, definition, null, 0);
            Selection.activeObject = test; EndingTests.Open(test);
        }
    }
}
