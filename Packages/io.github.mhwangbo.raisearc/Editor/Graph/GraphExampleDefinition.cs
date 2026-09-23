using PrincessStudio.Core;

namespace RaiseArc.Editor.Graph
{
    /// <summary>Deterministic first-party graph example with no imported game assets.</summary>
    public static class GraphExampleDefinition
    {
        public static ProjectDefinition Create()
        {
            var p = new ProjectDefinition { id = "raisearc-graph-example", durationDays = 30, startingMoney = 500 };
            string Text(string key, string en, string ko)
            {
                p.translations.Add(new TranslationEntry { key = key, locale = "en", text = en });
                p.translations.Add(new TranslationEntry { key = key, locale = "ko", text = ko });
                return key;
            }
            p.character.id = "daughter";
            p.character.nameKey = Text("character.name", "Luna", "루나");
            p.stats.Add(new StatDefinition { id = "vitality", nameKey = Text("stat.vitality", "Vitality", "체력"), initial = 70 });
            p.stats.Add(new StatDefinition { id = "wisdom", nameKey = Text("stat.wisdom", "Wisdom", "지혜"), initial = 10 });
            p.npcs.Add(new NpcDefinition { id = "mentor", nameKey = Text("npc.mentor", "Teacher Mira", "미라 선생님") });
            p.flags.Add(new FlagDefinition { id = "festival", nameKey = Text("flag.festival", "Festival invited", "축제 초대") });
            p.activities.Add(new ActivityDefinition { id = "study", nameKey = Text("activity.study", "Study", "공부"), days = 1, cost = 10,
                effects = { new EffectSpec { kind = ValueKind.Stat, target = "vitality", value = -20 }, new EffectSpec { kind = ValueKind.Stat, target = "wisdom", value = 5 } } });
            p.activities.Add(new ActivityDefinition { id = "rest", nameKey = Text("activity.rest", "Rest", "휴식"), days = 1,
                effects = { new EffectSpec { kind = ValueKind.Stat, target = "vitality", value = 15 } } });
            p.appearanceProfiles.Add(new AppearanceProfile { id = "profile-daughter", nameKey = Text("profile.daughter", "Luna profile", "루나 프로필"), slots = new AppearanceSlots { body = "placeholder.luna" } });
            p.appearanceProfiles.Add(new AppearanceProfile { id = "profile-mentor", nameKey = Text("profile.mentor", "Mira profile", "미라 프로필"), slots = new AppearanceSlots { body = "placeholder.mira" } });
            p.actors.Add(new ActorDefinition { id = "actor-daughter", nameKey = Text("actor.daughter", "Luna", "루나"), subjectId = "daughter", profileId = "profile-daughter" });
            p.actors.Add(new ActorDefinition { id = "actor-mentor", nameKey = Text("actor.mentor", "Teacher Mira", "미라 선생님"), subjectId = "mentor", profileId = "profile-mentor" });
            p.stageSlots.Add(new StageSlot { id = "center", nameKey = Text("slot.center", "Center", "중앙") });
            p.stageSlots.Add(new StageSlot { id = "left", nameKey = Text("slot.left", "Left", "왼쪽"), x = .25f });
            p.stageSlots.Add(new StageSlot { id = "right", nameKey = Text("slot.right", "Right", "오른쪽"), x = .75f });
            p.endings.Add(new EndingDefinition { id = "new-horizon", nameKey = Text("ending.horizon", "A new horizon", "새로운 지평") });

            var invitation = new EventDefinition { id = "invitation", nameKey = Text("event.invitation", "An invitation", "초대"), priority = 10 };
            invitation.conditions.Add(new ConditionSpec { kind = ValueKind.Day, value = 1 });
            var line1 = new PresentationStep { id = "invitation.line1", kind = PresentationStepKind.Dialogue,
                nameKey = Text("dialogue.hello", "Mira invites Luna to the studio.", "미라가 루나를 작업실에 초대한다."),
                speakerActorId = "actor-mentor", actorsChange = StageChange.Replace };
            line1.actors.Add(new ActorPlacement { actorId = "actor-mentor", slotId = "left" });
            line1.actors.Add(new ActorPlacement { actorId = "actor-daughter", slotId = "right" });
            invitation.presentation.Add(line1);
            invitation.presentation.Add(new PresentationStep { id = "invitation.line2", kind = PresentationStepKind.Dialogue,
                nameKey = Text("dialogue.reply", "Luna considers the invitation.", "루나가 초대를 생각한다."), speakerActorId = "actor-daughter", actorsChange = StageChange.Keep });
            var accept = new ChoiceDefinition { id = "accept", nameKey = Text("choice.accept", "Accept", "수락") };
            accept.effects.Add(new EffectSpec { kind = ValueKind.Relationship, target = "mentor", value = 5 });
            accept.effects.Add(new EffectSpec { kind = ValueKind.Flag, target = "festival", operation = EffectOperation.Set, value = 1 });
            invitation.choices.Add(accept);
            invitation.choices.Add(new ChoiceDefinition { id = "decline", nameKey = Text("choice.decline", "Stay home", "집에 있기") });
            p.events.Add(invitation);

            var gallery = new EventDefinition { id = "gallery-event", nameKey = Text("sequence.event", "Gallery visit", "전시 방문"), priority = 50 };
            gallery.conditions.Add(new ConditionSpec { kind = ValueKind.Day, value = 2 });
            PresentationStep Dialogue(string id, string en, string ko, string speaker, string next = "") => new PresentationStep
                { id = id, kind = PresentationStepKind.Dialogue, nameKey = Text(id, en, ko), speakerActorId = speaker, nextStepId = next, actorsChange = StageChange.Keep };
            var a1 = Dialogue("gallery.a1", "The gallery opens.", "전시관이 열린다.", "actor-mentor");
            a1.actorsChange = StageChange.Replace;
            a1.actors.Add(new ActorPlacement { actorId = "actor-mentor", slotId = "left" });
            a1.actors.Add(new ActorPlacement { actorId = "actor-daughter", slotId = "right" });
            gallery.presentation.Add(a1);
            gallery.presentation.Add(Dialogue("gallery.b1", "Luna studies the display.", "루나가 전시를 본다.", "actor-daughter"));
            gallery.presentation.Add(Dialogue("gallery.a2", "Mira offers a choice.", "미라가 선택을 제안한다.", "actor-mentor"));
            var branch = new PresentationStep { id = "gallery.choice", kind = PresentationStepKind.Choice, nameKey = Text("gallery.choice", "Choose a display", "전시 선택") };
            branch.choices.Add(new ChoiceDefinition { id = "gallery.choose-garden", nameKey = Text("gallery.choose-garden", "View the garden", "정원 보기"), nextStepId = "gallery.garden" });
            branch.choices.Add(new ChoiceDefinition { id = "gallery.choose-outfit", nameKey = Text("gallery.choose-outfit", "View the costume", "의상 보기"), nextStepId = "gallery.outfit" });
            gallery.presentation.Add(branch);
            gallery.presentation.Add(Dialogue("gallery.garden", "The garden painting is bright.", "정원 그림이 밝다.", "actor-daughter", "gallery.images"));
            gallery.presentation.Add(Dialogue("gallery.outfit", "The costume sketch is detailed.", "의상 스케치가 섬세하다.", "actor-mentor", "gallery.images"));
            var images = new PresentationStep { id = "gallery.images", kind = PresentationStepKind.Image, nameKey = Text("gallery.images", "Three sketches", "세 장의 스케치"), imagesChange = StageChange.Replace };
            images.images.Add(new StageImage { id = "garden", resourceKey = "drawing.garden", x = .03f, y = .15f, width = .3f, height = .7f });
            images.images.Add(new StageImage { id = "costume", resourceKey = "drawing.costume", x = .35f, y = .15f, width = .3f, height = .7f });
            images.images.Add(new StageImage { id = "portrait", resourceKey = "drawing.portrait", x = .67f, y = .15f, width = .3f, height = .7f });
            gallery.presentation.Add(images);
            gallery.presentation.Add(Dialogue("gallery.over-image", "Luna remembers the visit.", "루나가 방문을 기억한다.", "actor-daughter"));
            gallery.presentation.Add(Dialogue("gallery.clear", "The visit ends.", "방문이 끝난다.", "actor-mentor", EventSequence.End));
            p.events.Add(gallery);
            return p;
        }
    }
}
