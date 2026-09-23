using System;
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
            Directory.CreateDirectory(root);
            AssetDatabase.Refresh();
            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                AssetDatabase.CreateAsset(settings, root + "/LocalizationSettings.asset");
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }
            foreach (var code in p.locales)
            {
                var locale = LocalizationEditorSettings.GetLocale(code);
                if (locale == null)
                {
                    locale = Locale.CreateLocale(code);
                    AssetDatabase.CreateAsset(locale, root + "/" + code + ".asset");
                    LocalizationEditorSettings.AddLocale(locale);
                }
            }
            var fallback = LocalizationEditorSettings.GetLocale(p.fallbackLocale);
            foreach (var code in p.locales)
            {
                var locale = LocalizationEditorSettings.GetLocale(code);
                if (locale != fallback)
                {
                    var metadata = locale.Metadata.GetMetadata<FallbackLocale>();
                    if (metadata == null)
                    {
                        metadata = new FallbackLocale();
                        locale.Metadata.AddMetadata(metadata);
                    }
                    metadata.Locale = fallback;
                    EditorUtility.SetDirty(locale);
                }
                else
                {
                    var old = locale.Metadata.GetMetadata<FallbackLocale>();
                    if (old != null)
                    {
                        locale.Metadata.RemoveMetadata(old);
                        EditorUtility.SetDirty(locale);
                    }
                }
            }
            settings.SetSelectedLocale(LocalizationEditorSettings.GetLocale(p.defaultLocale));
            var selectors = settings.GetStartupLocaleSelectors();
            selectors.RemoveAll(s => s is SpecificLocaleSelector);
            selectors.Insert(0, new SpecificLocaleSelector { LocaleId = new LocaleIdentifier(p.defaultLocale) });
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
            EditorUtility.SetDirty(settings);
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, strings);
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, assets);
            AssetDatabase.SaveAssets();
        }
    }
}
