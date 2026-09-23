using System;
using System.Collections.Generic;
using PrincessStudio.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Unity
{
    /// <summary>Renderer-neutral boundary. Implementations own and release their rendering resources.</summary>
    public interface IActorView : IDisposable
    {
        void Apply(ActorPresentationState state, StageSlot slot, string locale);
        void Hide();
    }
    /// <summary>Optional scene component factory for Live2D, Animator or other renderer integrations.</summary>
    public abstract class ActorViewProvider : MonoBehaviour
    {
        public abstract IActorView CreateView(VisualElement parent, string actorId);
    }
    public sealed class SpriteActorView : IActorView
    {
        private readonly VisualElement root;
        private readonly Image[] layers = new Image[4];
        private readonly PresentationSkin skin;
        private readonly string fallback;
        public SpriteActorView(VisualElement parent, PresentationSkin skin, string fallback)
        {
            this.skin = skin; this.fallback = fallback;
            root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute; root.style.top = 0; root.style.bottom = 0;
            for (var i = 0; i < layers.Length; i++)
            {
                layers[i] = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                layers[i].style.position = Position.Absolute;
                layers[i].style.left = 0; layers[i].style.right = 0; layers[i].style.top = 0; layers[i].style.bottom = 0;
                root.Add(layers[i]);
            }
            parent.Add(root);
            root.RegisterCallback<GeometryChangedEvent>(_ => UpdateScaleOrigin());
        }
        public void Apply(ActorPresentationState state, StageSlot slot, string locale)
        {
            root.name = "actor-view-" + state.actorId;
            root.style.display = DisplayStyle.Flex;
            root.style.scale = new Scale(new Vector3(state.scale, state.scale, 1));
            root.style.left = Length.Percent((slot.x - slot.width * slot.scale / 2) * 100);
            root.style.width = Length.Percent(slot.width * slot.scale * 100);
            var keys = new[] { state.slots.body, state.slots.hair, state.slots.outfit, state.slots.expression };
            for (var i = 0; i < layers.Length; i++)
            {
                layers[i].sprite = string.IsNullOrEmpty(keys[i]) ? null : skin.Find(keys[i], locale, fallback);
                layers[i].style.display = layers[i].sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            UpdateScaleOrigin();
        }
        private void UpdateScaleOrigin()
        {
            var sprite = layers[0].sprite;
            var width = root.layout.width; var height = root.layout.height;
            if (sprite == null || float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0) return;
            // ScaleToFit centers the body inside its box. Anchor its rendered bottom, not the box bottom.
            var bodyHeight = Mathf.Min(height, width * sprite.rect.height / sprite.rect.width);
            root.style.transformOrigin = new TransformOrigin(Length.Percent(50), new Length((height + bodyHeight) / 2), 0);
        }
        public void Hide() => root.style.display = DisplayStyle.None;
        public void Dispose() { root.RemoveFromHierarchy(); foreach (var layer in layers) layer.sprite = null; }
    }
    public sealed class PresentationStage : IDisposable
    {
        public VisualElement Element { get; }
        private readonly ProjectDefinition project;
        private readonly PresentationSkin skin;
        private readonly AppearanceResolver resolver;
        private readonly Image background;
        private readonly Image defaultPortrait;
        private Sprite defaultBackground;
        private readonly VisualElement behindImages = new VisualElement(), actorLayer = new VisualElement(), frontImages = new VisualElement();
        private readonly List<Image> imageViews = new List<Image>();
        private readonly Func<VisualElement, string, IActorView> factory;
        private readonly Dictionary<string, IActorView> views = new Dictionary<string, IActorView>(StringComparer.Ordinal);
        public PresentationStage(ProjectDefinition project, PresentationSkin skin, Func<VisualElement, string, IActorView> factory = null, Sprite defaultBackground = null, Sprite defaultPortrait = null)
        {
            this.project = project; this.skin = skin; resolver = new AppearanceResolver(project);
            this.defaultBackground = defaultBackground;
            this.factory = factory;
            Element = new VisualElement(); Element.style.height = skin.stageHeight; Element.style.flexShrink = 0; Element.style.overflow = Overflow.Hidden;
            Element.style.backgroundColor = skin.background;
            background = new Image { scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            background.style.position = Position.Absolute; background.style.left = 0; background.style.right = 0; background.style.top = 0; background.style.bottom = 0;
            Element.Add(background);
            this.defaultPortrait = new Image { sprite = defaultPortrait, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore, name = "default-character-image" };
            this.defaultPortrait.style.position = Position.Absolute; this.defaultPortrait.style.left = 0; this.defaultPortrait.style.right = 0; this.defaultPortrait.style.top = 0; this.defaultPortrait.style.bottom = 0;
            Element.Add(this.defaultPortrait);
            foreach (var layer in new[] { behindImages, actorLayer, frontImages })
            {
                layer.style.position = Position.Absolute; layer.style.left = 0; layer.style.right = 0; layer.style.top = 0; layer.style.bottom = 0;
                layer.pickingMode = PickingMode.Ignore; Element.Add(layer);
            }
        }
        public void SetDefaultImages(Sprite backdrop, Sprite portrait, float portraitScale = 1, bool fillBackground = true)
        {
            defaultBackground = backdrop; defaultPortrait.sprite = portrait;
            defaultPortrait.style.scale = new Scale(new Vector3(portraitScale, portraitScale, 1));
            defaultPortrait.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100), 0);
            background.scaleMode = fillBackground ? ScaleMode.ScaleAndCrop : ScaleMode.ScaleToFit;
        }
        private IActorView View(string actorId)
        {
            if (!views.TryGetValue(actorId, out var view))
            {
                view = factory != null ? factory(actorLayer, actorId) : new SpriteActorView(actorLayer, skin, project.fallbackLocale);
                if (view == null) throw new InvalidOperationException("Actor view factory returned null: " + actorId);
                views.Add(actorId, view);
            }
            return view;
        }
        public void Render(StateSnapshot state, string locale, PresentationStep previewStep = null)
        {
            foreach (var view in views.Values) view.Hide();
            var e = project.events.Find(x => x.id == state.PendingEventId);
            var resolved = state.CaptureStage();
            if (previewStep != null) resolved.Apply(previewStep);
            background.sprite = string.IsNullOrEmpty(resolved.backgroundKey) ? defaultBackground : skin.Find(resolved.backgroundKey, locale, project.fallbackLocale);
            defaultPortrait.style.display = resolved.actors.Count == 0 && !project.actors.Exists(a => a.subjectId == project.character.id) ? DisplayStyle.Flex : DisplayStyle.None;
            RenderImages(resolved.images, locale);
            if (previewStep != null || e != null)
            {
                foreach (var placement in resolved.actors)
                    View(placement.actorId).Apply(resolver.Resolve(state, placement.actorId, placement), project.stageSlots.Find(x => x.id == placement.slotId), locale);
            }
            else if (project.stageSlots.Count > 0)
            {
                var main = project.actors.Find(x => x.subjectId == project.character.id);
                if (main != null) View(main.id).Apply(resolver.Resolve(state, main.id), project.stageSlots[0], locale);
            }
        }
        private void RenderImages(List<StageImage> images, string locale)
        {
            for (var i = 0; i < Math.Max(images.Count, imageViews.Count); i++)
            {
                if (i >= imageViews.Count) imageViews.Add(new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
                var view = imageViews[i];
                if (i >= images.Count) { view.sprite = null; view.RemoveFromHierarchy(); continue; }
                var image = images[i];
                view.name = "event-image-" + image.id;
                var layer = image.plane == StageImagePlane.BehindActors ? behindImages : frontImages;
                if (view.parent != layer) layer.Add(view); else view.BringToFront();
                view.sprite = skin.Find(image.resourceKey, locale, project.fallbackLocale);
                view.style.position = Position.Absolute;
                view.style.left = Length.Percent(image.x * 100); view.style.top = Length.Percent(image.y * 100);
                view.style.width = Length.Percent(image.width * 100); view.style.height = Length.Percent(image.height * 100);
                view.style.opacity = image.opacity;
            }
        }
        public void Dispose() { foreach (var view in views.Values) view.Dispose(); views.Clear(); foreach (var image in imageViews) image.sprite = null; imageViews.Clear(); background.sprite = null; Element.RemoveFromHierarchy(); }
    }
}
