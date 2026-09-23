using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UIElements;

namespace PrincessStudio.Localization
{
    public sealed class LocalizedFontBinding : IDisposable
    {
        private readonly LocalizedFont reference;
        private readonly VisualElement element;
        public LocalizedFontBinding(VisualElement element, string table, string key)
        {
            this.element = element;
            reference = new LocalizedFont { TableReference = table, TableEntryReference = key };
            reference.AssetChanged += Update;
        }
        private void Update(Font font)
        {
            element.style.unityFont = font;
        }
        public void Dispose()
        {
            reference.AssetChanged -= Update;
        }
    }
    public sealed class LocalizedSpriteBinding : IDisposable
    {
        private readonly LocalizedSprite reference;
        private readonly Image image;
        public LocalizedSpriteBinding(Image image, string table, string key)
        {
            this.image = image;
            reference = new LocalizedSprite { TableReference = table, TableEntryReference = key };
            reference.AssetChanged += Update;
        }
        private void Update(Sprite sprite)
        {
            image.sprite = sprite;
        }
        public void Dispose()
        {
            reference.AssetChanged -= Update;
        }
    }
    public sealed class LocalizedVoiceBinding : IDisposable
    {
        private readonly LocalizedAudioClip reference;
        private readonly AudioSource source;
        public LocalizedVoiceBinding(AudioSource source, string table, string key)
        {
            this.source = source;
            reference = new LocalizedAudioClip { TableReference = table, TableEntryReference = key };
            reference.AssetChanged += Update;
        }
        private void Update(AudioClip clip)
        {
            source.clip = clip;
        }
        public void Dispose()
        {
            reference.AssetChanged -= Update;
        }
    }
}
