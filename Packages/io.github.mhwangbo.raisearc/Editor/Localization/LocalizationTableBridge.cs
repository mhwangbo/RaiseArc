using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.Localization.Metadata;

namespace PrincessStudio.Editor
{
    [InitializeOnLoad]
    public static class LocalizationTableBridge
    {
        static LocalizationTableBridge()
        {
            StudioIntegrations.SyncLocalization = Sync;
        }
        public static void Sync(GameProjectAsset asset)
        {
            var p = asset.Read();
            var root = "Assets/PrincessStudioContent/Localization/" + p.id;
            ValidateOwnership(p.id, root);
            Directory.CreateDirectory(root);
            AssetDatabase.Refresh();
            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            var createdSettings = settings == null;
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                AssetDatabase.CreateAsset(settings, root + "/LocalizationSettings.asset");
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }
            var createdLocales = new List<Locale>();
            foreach (var code in p.locales)
            {
                var locale = LocalizationEditorSettings.GetLocale(code);
                if (locale == null)
                {
                    locale = Locale.CreateLocale(code);
                    AssetDatabase.CreateAsset(locale, root + "/" + code + ".asset");
                    LocalizationEditorSettings.AddLocale(locale);
                    createdLocales.Add(locale);
                }
            }
            // Locale assets and LocalizationSettings are shared by every game in this Unity project.
            // Synchronizing one game's tables must not rewrite another game's locale policy.
            var fallback = LocalizationEditorSettings.GetLocale(p.fallbackLocale);
            foreach (var locale in createdLocales)
            {
                if (locale != fallback)
                {
                    var metadata = new FallbackLocale { Locale = fallback };
                    locale.Metadata.AddMetadata(metadata);
                    EditorUtility.SetDirty(locale);
                }
            }
            if (createdSettings)
            {
                settings.SetSelectedLocale(LocalizationEditorSettings.GetLocale(p.defaultLocale));
                var selectors = settings.GetStartupLocaleSelectors();
                selectors.Insert(0, new SpecificLocaleSelector { LocaleId = new LocaleIdentifier(p.defaultLocale) });
                EditorUtility.SetDirty(settings);
            }
            var tableName = "Princess." + p.id;
            var strings = LocalizationEditorSettings.GetStringTableCollection(tableName) ?? LocalizationEditorSettings.CreateStringTableCollection(tableName, root);
            foreach (var code in p.locales)
            {
                var table = strings.GetTable(code) as StringTable;
                if (table == null)
                    table = strings.AddNewTable(new LocaleIdentifier(code)) as StringTable;
                foreach (var entry in p.translations)
                    if (entry.locale == code)
                        table.AddEntry(entry.key, entry.text);
                EditorUtility.SetDirty(table);
            }
            EditorUtility.SetDirty(strings);
            EditorUtility.SetDirty(strings.SharedData);
            var assets = LocalizationEditorSettings.GetAssetTableCollection(tableName + ".Assets") ?? LocalizationEditorSettings.CreateAssetTableCollection(tableName + ".Assets", root);
            foreach (var code in p.locales)
            {
                var table = assets.GetTable(code) as AssetTable;
                if (table == null)
                    table = assets.AddNewTable(new LocaleIdentifier(code)) as AssetTable;
                // Voice silence is explicit: remove stale table entries after clearing a locale.
                foreach (var voice in p.events.SelectMany(e => e.presentation).Select(s => s.voiceKey).Where(k => !string.IsNullOrEmpty(k)).Distinct())
                    if (!p.localizedAssets.Exists(a => a.key == voice && a.locale == code)) table.RemoveEntry(voice);
                foreach (var entry in p.localizedAssets)
                    if (entry.locale == code)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                        if (string.IsNullOrEmpty(path))
                        {
                            if (p.events.Any(e => e.presentation.Any(s => s.voiceKey == entry.key)))
                            {
                                table.RemoveEntry(entry.key);
                                Debug.LogWarning("RaiseArc: missing voice asset " + entry.key + " (" + code + "). Dialogue will continue silently.");
                                continue;
                            }
                            throw new ArgumentException("Missing localized asset: " + entry.key);
                        }
                        if (path.Contains("/Resources/"))
                            throw new ArgumentException("Move localized assets outside Resources: " + path);
                        var addressables = AddressableAssetSettingsDefaultObject.GetSettings(true);
                        addressables.CreateOrMoveEntry(entry.assetGuid, addressables.DefaultGroup);
                        table.AddEntry(entry.key, entry.assetGuid);
                    }
                EditorUtility.SetDirty(table);
            }
            EditorUtility.SetDirty(assets);
            EditorUtility.SetDirty(assets.SharedData);
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, strings);
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, assets);
            AssetDatabase.SaveAssets();
        }

        public static void ValidateOwnership(string projectId, string root)
        {
            var prefix = root.TrimEnd('/') + "/";
            var strings = LocalizationEditorSettings.GetStringTableCollection("Princess." + projectId);
            var assets = LocalizationEditorSettings.GetAssetTableCollection("Princess." + projectId + ".Assets");
            foreach (var collection in new UnityEngine.Object[] { strings, assets })
            {
                if (collection == null) continue;
                var path = AssetDatabase.GetAssetPath(collection);
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                    throw new InvalidOperationException("RaiseArc localization collection belongs to another folder: " + path);
            }
        }
    }
}
