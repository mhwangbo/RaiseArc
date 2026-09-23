using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public sealed partial class StudioWindow
    {
        private void DrawNextStep(VisualElement host, EventDefinition e, PresentationStep step, string current, Action<string> changed)
        {
            var entries = e.presentation.Where(x => x != step).Cast<Definition>().ToList();
            entries.Add(new ItemDefinition { id = EventSequence.End, nameKey = "End event" });
            SelectReference(host, "Next step (default = list order)", entries, current, changed, true);
        }

        private void DrawSequenceImages(VisualElement host, PresentationStep step, List<ItemDefinition> assets, Action rebuild)
        {
            foreach (var image in step.images.ToArray())
            {
                var card = new Foldout { text = StudioText.T("Image · " + image.id )}; host.Add(card);
                SelectReference(card, "Sprite resource", assets, image.resourceKey, x => image.resourceKey = x);
                EnumValue(card, "Layer", image.plane, x => image.plane = x);
                Float(card, "Left (0–1)", image.x, x => image.x = x);
                Float(card, "Top (0–1)", image.y, x => image.y = x);
                Float(card, "Width (0–1)", image.width, x => image.width = x);
                Float(card, "Height (0–1)", image.height, x => image.height = x);
                Float(card, "Opacity (0–1)", image.opacity, x => image.opacity = x);
                card.Add(Button("Move backward", () => { var i = step.images.IndexOf(image); if (i > 0) { step.images.RemoveAt(i); step.images.Insert(i - 1, image); rebuild(); } }));
                card.Add(Button("Remove image", () => { step.images.Remove(image); rebuild(); }));
            }
            host.Add(Button("+ Image layer", () => { step.imagesChange = StageChange.Replace; step.images.Add(new StageImage { id = "image-" + Guid.NewGuid().ToString("N").Substring(0, 8), resourceKey = assets.FirstOrDefault()?.id ?? "" }); rebuild(); }));
        }

        private void DrawSequenceChoices(VisualElement host, ProjectDefinition p, EventDefinition e, PresentationStep step, Action rebuild)
        {
            host.Add(new HelpBox(StudioText.T("Keep at least one option without conditions so the player can always continue."), HelpBoxMessageType.Info));
            foreach (var choice in step.choices.ToArray())
            {
                var card = new Foldout { text = StudioText.T("Choice · " + choice.id )}; host.Add(card);
                Text(card, "Text key", choice.nameKey, x => choice.nameKey = x);
                DrawNextStep(card, e, step, choice.nextStepId, x => choice.nextStepId = x);
                DrawConditions(card, choice.conditions);
                DrawEffects(card, choice.effects);
                card.Add(Button("Remove choice", () => { step.choices.Remove(choice); rebuild(); }));
            }
            host.Add(Button("+ Branch choice", () => { var choice = new ChoiceDefinition { id = step.id + ".choice-" + Guid.NewGuid().ToString("N").Substring(0, 6) }; AuthoringService.GenerateKeys(choice, "choice"); step.choices.Add(choice); rebuild(); }));
        }
    }
}
