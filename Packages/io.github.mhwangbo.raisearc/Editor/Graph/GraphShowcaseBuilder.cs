using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEngine;

namespace PrincessStudio.Editor.Graph
{
    public static class GraphShowcaseBuilder
    {
        public const string AssetPath = "Assets/PrincessStudio/Samples/Showcase/RoyalEvening.asset";
        public const string EventId = "royal-evening";
        [MenuItem("Window/RaiseArc/왕실 초대 그래프 샘플 만들기")]
        public static void OpenSample() { StudioText.Language = "ko"; GraphWorkbenchWindow.Open(AssetDatabase.LoadAssetAtPath<GameProjectAsset>(AssetPath) ?? Create()); }
        public static GameProjectAsset Create()
        {
            var codec = new UnityProjectCodec();
            var p = codec.FromJson(File.ReadAllText("Assets/PrincessStudio/Samples/Data/PresentationDemo.json"));
            p.id = "royal-evening-showcase"; p.defaultLocale = "ko"; p.fallbackLocale = "en";
            string Text(string key, string ko, string en)
            {
                p.translations.RemoveAll(t => t.key == key);
                p.translations.Add(new TranslationEntry { key = key, locale = "ko", text = ko });
                p.translations.Add(new TranslationEntry { key = key, locale = "en", text = en }); return key;
            }
            var e = new EventDefinition { id = EventId, priority = 100, once = true, nameKey = Text("royal.event", "별빛 아래의 초대", "A royal invitation beneath the stars"), descriptionKey = Text("royal.description", "세 갈래의 선택이 하나의 밤으로 이어집니다.", "Three choices meet in one unforgettable evening.") };
            e.tags.AddRange(new[] { "main-story", "royal", "showcase" });
            e.conditions.Add(new ConditionSpec { kind = ValueKind.Stat, target = "wisdom", value = 10 });
            e.conditions.Add(new ConditionSpec { kind = ValueKind.Money, value = 100 });
            e.effects.Add(new EffectSpec { kind = ValueKind.Money, value = -5 });
            p.flags.Add(new FlagDefinition { id = "royal-scholarship", nameKey = Text("royal.flag", "왕실 장학생 후보", "Royal scholarship candidate") });
            PresentationStep Step(string id, PresentationStepKind kind, string ko, string en, string next)
            {
                var step = new PresentationStep { id = "royal." + id, kind = kind, nameKey = Text("royal.text." + id, ko, en), nextStepId = next == EventSequence.End ? next : "royal." + next, actorsChange = StageChange.Keep, imagesChange = StageChange.Keep, backgroundChange = StageChange.Keep };
                e.presentation.Add(step); return step;
            }
            var intro = Step("invitation", PresentationStepKind.Dialogue, "왕실에서 온 초대장", "An invitation from the palace", "mentor");
            intro.speakerActorId = "actor-daughter"; intro.backgroundChange = StageChange.Replace; intro.backgroundKey = "background.garden";
            Step("mentor", PresentationStepKind.Dialogue, "오늘은 네가 걸어온 길을 보여주는 날이란다.", "Tonight, show them how far you have come.", "answer").speakerActorId = "actor-mentor";
            Step("answer", PresentationStepKind.Dialogue, "떨리지만, 제 방식대로 해 볼게요.", "I am nervous, but I will find my own way.", "readiness").speakerActorId = "actor-daughter";
            var gate = Step("readiness", PresentationStepKind.Condition, "연회에 나설 준비", "Ready for the reception?", "choice"); gate.falseStepId = "royal.recover";
            gate.conditions.Add(new ConditionSpec { kind = ValueKind.Stat, target = "wisdom", value = 15 });
            gate.conditions.Add(new ConditionSpec { kind = ValueKind.Stat, target = "vitality", value = 40 });
            var recover = Step("recover", PresentationStepKind.Effect, "스승의 격려와 휴식", "Encouragement and a short rest", "choice");
            recover.effects.Add(new EffectSpec { kind = ValueKind.Stat, target = "vitality", value = 8 });
            recover.effects.Add(new EffectSpec { kind = ValueKind.Relationship, target = "mentor", value = 2 });
            var choice = Step("choice", PresentationStepKind.Choice, "오늘 밤, 어떤 모습을 보여줄까?", "Who will you be tonight?", "merge");
            void Choice(string id, string ko, string en, string next) => choice.choices.Add(new ChoiceDefinition { id = "royal.choice." + id, nameKey = Text("royal.choice." + id, ko, en), nextStepId = "royal." + next });
            Choice("court", "정중하게 인사를 올린다", "Greet the court", "court");
            Choice("study", "배운 것을 이야기한다", "Share what you learned", "study");
            Choice("help", "준비하는 이들을 돕는다", "Help the hosts", "help");
            void Reward(string id, string ko, string en, string stat, int amount, int money, int relationship)
            {
                var step = Step(id, PresentationStepKind.Effect, ko, en, "merge");
                step.effects.Add(new EffectSpec { kind = ValueKind.Stat, target = stat, value = amount });
                step.effects.Add(new EffectSpec { kind = ValueKind.Money, value = money });
                step.effects.Add(new EffectSpec { kind = ValueKind.Relationship, target = "mentor", value = relationship });
                step.effects.Add(new EffectSpec { kind = ValueKind.Flag, target = "royal-scholarship", operation = EffectOperation.Set, value = 1 });
            }
            Reward("court", "첫인상은 오래 남는다", "A lasting first impression", "wisdom", 2, -25, 5);
            Reward("study", "배움이 빛나는 순간", "A moment for learning to shine", "wisdom", 4, -15, 3);
            Reward("help", "작은 친절의 보답", "Kindness finds its reward", "vitality", -4, 20, 6);
            Step("merge", PresentationStepKind.Merge, "다시 만난 정원", "Together in the garden", "memories");
            var images = Step("memories", PresentationStepKind.Image, "별빛 아래 남긴 세 장의 기억", "Three memories beneath the stars", "epilogue");
            images.imagesChange = StageChange.Replace; images.actorsChange = StageChange.Clear;
            images.images.Add(new StageImage { id = "garden", resourceKey = "background.garden", x = .03f, y = .15f, width = .3f, height = .7f });
            images.images.Add(new StageImage { id = "dress", resourceKey = "outfit.festival", x = .35f, y = .15f, width = .3f, height = .7f });
            images.images.Add(new StageImage { id = "smile", resourceKey = "expression.smile", x = .67f, y = .15f, width = .3f, height = .7f });
            Step("epilogue", PresentationStepKind.Dialogue, "오늘의 선택이 내일을 바꾼다", "Today's choice shapes tomorrow", "promise").speakerActorId = "actor-daughter";
            Step("promise", PresentationStepKind.Dialogue, "다음 초대에는 더 자란 모습으로 올게요.", "Next time, I will have grown a little more.", EventSequence.End).speakerActorId = "actor-daughter";
            p.events.Add(e);
            var api = new AuthoringService(p, codec); var report = api.ValidateProject();
            if (report.HasErrors) throw new System.InvalidOperationException(report.Summary);
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath)); AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<GameProjectAsset>(AssetPath);
            if (asset == null) { asset = ScriptableObject.CreateInstance<GameProjectAsset>(); AssetDatabase.CreateAsset(asset, AssetPath); }
            asset.Write(api.Snapshot()); EditorUtility.SetDirty(asset);
            File.WriteAllText("Assets/PrincessStudio/Samples/Showcase/RoyalEvening.json", codec.ToJson(api.Snapshot()));
            var guid = AssetDatabase.AssetPathToGUID(AssetPath); Directory.CreateDirectory("Assets/PrincessStudioWorkbench/Layouts"); AssetDatabase.Refresh();
            var layoutPath = "Assets/PrincessStudioWorkbench/Layouts/" + guid + ".asset";
            var layout = AssetDatabase.LoadAssetAtPath<GraphLayoutAsset>(layoutPath);
            if (layout == null) { layout = ScriptableObject.CreateInstance<GraphLayoutAsset>(); AssetDatabase.CreateAsset(layout, layoutPath); }
            layout.nodeWidth = 198;
            var positions = new Dictionary<string, Vector2> {
                [EventId] = new Vector2(0, 1), ["royal.invitation"] = new Vector2(1, 1), ["royal.readiness"] = new Vector2(2, 1),
                ["royal.recover"] = new Vector2(2, 2), ["royal.choice"] = new Vector2(3, 1),
                ["royal.court"] = new Vector2(4, 0), ["royal.study"] = new Vector2(4, 1), ["royal.help"] = new Vector2(4, 2),
                ["royal.merge"] = new Vector2(5, 1), ["royal.memories"] = new Vector2(6, 1), ["royal.epilogue"] = new Vector2(7, 1), [EventSequence.End] = new Vector2(7, 2)
            };
            foreach (var node in GraphProjection.Build(api.Snapshot(), EventId, "ko").nodes)
            {
                var entry = layout.Get(EventId, node.id, 0); var position = positions[node.id];
                entry.x = 20 + position.x * 224; entry.y = 35 + position.y * 300; entry.color = new Color(.105f, .17f, .235f);
            }
            EditorUtility.SetDirty(layout);
            var templatePath = AssetPath.Replace(".asset", ".layout.asset");
            var template = AssetDatabase.LoadAssetAtPath<GraphLayoutAsset>(templatePath);
            if (template == null) AssetDatabase.CreateAsset(Object.Instantiate(layout), templatePath);
            else { EditorUtility.CopySerialized(layout, template); EditorUtility.SetDirty(template); }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); return asset;
        }
    }
}
