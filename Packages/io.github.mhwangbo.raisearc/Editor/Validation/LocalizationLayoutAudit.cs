using System.Collections.Generic;
using PrincessStudio.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public static class LocalizationLayoutAudit
    {
        /// <summary>Measures actual UI Toolkit text with a chosen font and target width after panel attachment.</summary>
        public static ValidationReport Inspect(ProjectDefinition project, string locale, Font font, Label probe, float width, float height, int fontSize)
        {
            var report = new ValidationReport();
            if (probe.panel == null || width <= 0 || height <= 0)
            {
                report.Add(IssueSeverity.Error, "layout.probe", locale, "Attach the preview to a panel and choose positive bounds.");
                return report;
            }
            probe.style.fontSize = fontSize;
            probe.style.whiteSpace = WhiteSpace.Normal;
            if (font != null)
                probe.style.unityFont = font;
            foreach (var entry in project.translations)
            {
                if (entry.locale != locale || string.IsNullOrEmpty(entry.text))
                    continue;
                var measured = probe.MeasureTextSize(entry.text, width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                if (measured.y > height)
                    report.Add(IssueSeverity.Warning, "layout.overflow", entry.key, $"Measured {measured.y:0}px high for a {width:0} × {height:0}px box.");
                if (font != null)
                {
                    font.RequestCharactersInTexture(entry.text, fontSize, FontStyle.Normal);
                    var missing = new HashSet<char>();
                    foreach (var c in entry.text)
                        if (!char.IsWhiteSpace(c) && !font.HasCharacter(c))
                            missing.Add(c);
                    if (missing.Count > 0)
                        report.Add(IssueSeverity.Warning, "font.glyph", entry.key, "Missing glyphs in the selected font: " + new string(new List<char>(missing).ToArray()));
                }
                if (entry.text.IndexOf('\u202E') >= 0 || entry.text.IndexOf('\u202D') >= 0)
                    report.Add(IssueSeverity.Warning, "text.bidi-override", entry.key, "Contains explicit bidirectional override marks; review reading order.");
            }
            if (font == null)
                report.Add(IssueSeverity.Info, "font.unverified", locale, "Select the intended locale font to inspect glyph coverage.");
            return report;
        }
    }
}
