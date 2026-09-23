using System;
using System.Collections.Generic;
using PrincessStudio.Core;
using UnityEngine;

namespace PrincessStudio.Unity
{
    [Serializable]
    public sealed class SpriteBinding
    {
        public string key = "", locale = "";
        public Sprite sprite;
    }
    [CreateAssetMenu(menuName = "RaiseArc/Presentation Skin")]
    public sealed class PresentationSkin : ScriptableObject
    {
        public Color background = new Color(0.075f, 0.067f, 0.11f);
        public Color foreground = new Color(0.93f, 0.9f, 0.96f);
        public Color accent = new Color(0.94f, 0.82f, 0.63f);
        [Range(180, 720)] public int stageHeight = 320;
        public List<SpriteBinding> sprites = new List<SpriteBinding>();
        public Sprite Find(string key, string locale, string fallback)
        {
            var entry = sprites.Find(x => x.key == key && x.locale == locale)
                ?? sprites.Find(x => x.key == key && x.locale == fallback)
                ?? sprites.Find(x => x.key == key && string.IsNullOrEmpty(x.locale));
            return entry?.sprite;
        }
        public List<string> ValidateBindings(ProjectDefinition project)
        {
            var issues = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in sprites)
                if (entry == null || string.IsNullOrEmpty(entry.key) || entry.sprite == null || !seen.Add(entry.locale + ":" + entry.key)) issues.Add("Missing or duplicate sprite binding.");
            void Check(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                foreach (var locale in project.locales)
                    if (Find(key, locale, project.fallbackLocale) == null) issues.Add("Missing sprite: " + key + " (" + locale + ")");
            }
            void Slots(AppearanceSlots s) { Check(s.body); Check(s.hair); Check(s.outfit); Check(s.expression); }
            foreach (var profile in project.appearanceProfiles) Slots(profile.slots);
            foreach (var rule in project.appearanceRules) Slots(rule.slots);
            foreach (var growth in project.growth) Check(growth.appearanceKey);
            foreach (var e in project.events)
                foreach (var step in e.presentation) { Check(step.backgroundKey); foreach (var actor in step.actors) Slots(actor.overrides); foreach (var image in step.images) Check(image.resourceKey); }
            return issues;
        }
    }
}
