using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    /// <summary>Tool language is independent of authored content and Unity's player locale.</summary>
    public static class StudioText
    {
        private const string Preference = "PrincessStudio.Editor.Language";
        private const string CatalogPath = "Packages/io.github.mhwangbo.raisearc/Editor/UI/EditorTranslations.json";
        [Serializable] public sealed class Entry { public string key, en, ko; }
        [Serializable] private sealed class Catalog { public List<Entry> entries = new List<Entry>(); }
        private static Dictionary<string, Entry> byEnglish, byKey;
        private static Entry[] fragments;
        private static Font font;
        public static string Language { get => EditorPrefs.GetString(Preference, "ko"); set => EditorPrefs.SetString(Preference, value == "ko" ? "ko" : "en"); }
        public static IReadOnlyCollection<Entry> Entries { get { Load(); return byKey.Values; } }
        private static void Load()
        {
            if (byEnglish != null) return;
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath);
            if (source == null) throw new FileNotFoundException("RaiseArc editor translation catalog is missing", CatalogPath);
            var catalog = JsonUtility.FromJson<Catalog>(source.text);
            if (catalog?.entries == null) throw new InvalidDataException("RaiseArc editor translation catalog is invalid: " + CatalogPath);
            byEnglish = new Dictionary<string, Entry>(StringComparer.Ordinal); byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var entry in catalog.entries)
            {
                if (byEnglish.ContainsKey(entry.en) || byKey.ContainsKey(entry.key)) throw new InvalidDataException("Duplicate editor translation key: " + entry.key);
                byEnglish.Add(entry.en, entry); byKey.Add(entry.key, entry);
            }
            fragments = catalog.entries.Where(e => e.en.Length > 1 && (char.IsWhiteSpace(e.en[0]) || char.IsWhiteSpace(e.en[e.en.Length - 1]))).OrderByDescending(e => e.en.Length).ToArray();
        }
        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english) || Language != "ko") return english;
            Load();
            if (byEnglish.TryGetValue(english, out var entry)) return string.IsNullOrEmpty(entry.ko) ? english : entry.ko;
            var trimmed = english.Trim();
            if (byEnglish.TryGetValue(trimmed, out entry)) return english.Replace(trimmed, entry.ko);
            foreach (var separator in new[] { "\n", " · ", " • " })
                if (english.Contains(separator)) return string.Join(separator, english.Split(new[] { separator }, StringSplitOptions.None).Select(T));
            foreach (var part in fragments)
                if (english.StartsWith(part.en, StringComparison.Ordinal)) return part.ko + T(english.Substring(part.en.Length));
                else if (english.EndsWith(part.en, StringComparison.Ordinal)) return T(english.Substring(0, english.Length - part.en.Length)) + part.ko;
            return english;
        }
        public static string Format(string key, params object[] arguments)
        {
            Load(); if (!byKey.TryGetValue(key, out var entry)) return key;
            return string.Format(Language == "ko" ? entry.ko : entry.en, arguments);
        }
        public static PopupField<Enum> EnumField(string label, Enum value)
        {
            var values = Enum.GetValues(value.GetType()).Cast<Enum>().ToList();
            return new PopupField<Enum>(T(label), values, values.IndexOf(value), x => T(x.ToString()), x => T(x.ToString()));
        }
        public static DropdownField Dropdown(string label, List<string> values, int selected)
        {
            var field = new DropdownField(T(label), values, selected);
            field.formatListItemCallback = T; field.formatSelectedValueCallback = T; return field;
        }
        public static DropdownField Dropdown(List<string> values, int selected) => Dropdown("", values, selected);
        public static VisualElement LanguagePicker(Action rebuild, Func<bool> canChange = null)
        {
            var picker = new DropdownField(new List<string> { "한국어", "English" }, Language == "ko" ? 0 : 1) { name = "editor-language" };
            picker.tooltip = "제작 도구 언어 / Editor language"; picker.style.width = 108;
            picker.RegisterValueChangedCallback(e =>
            {
                if (canChange != null && !canChange()) { picker.SetValueWithoutNotify(Language == "ko" ? "한국어" : "English"); return; }
                Language = e.newValue == "한국어" ? "ko" : "en"; rebuild();
            });
            return picker;
        }
        public static void ApplyFont(VisualElement root)
        {
            if (font == null) font = AssetDatabase.LoadAssetAtPath<Font>("Packages/io.github.mhwangbo.raisearc/Editor/Templates/NativeUI/NotoSansKR-Regular.otf");
            if (font != null) root.style.unityFont = font;
        }
        public static bool Dialog(string title, string message, string ok, string cancel = "") =>
            EditorUtility.DisplayDialog(T(title), T(message), T(ok), T(cancel));
        public static int DialogComplex(string title, string message, string ok, string cancel, string alternate) =>
            EditorUtility.DisplayDialogComplex(T(title), T(message), T(ok), T(cancel), T(alternate));
        public static string Issue(ValidationIssue issue) => T(issue.severity.ToString()) + " · " + issue.target + " · " + T(issue.message);
        public static string Report(ValidationReport report) => report.issues.Count == 0 ? T("No content validation issues.") : string.Join("\n", report.issues.Select(Issue));
    }
}
