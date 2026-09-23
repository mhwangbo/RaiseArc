using System;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor.Localization
{
    public static class PresentationSampleBuilder
    {
        public const string Root = "Assets/PrincessStudio/Samples/Presentation";
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root + "/Sprites"); AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<GameProjectAsset>(Root + "/PresentationDemo.asset");
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<GameProjectAsset>();
                var data = new UnityProjectCodec().FromJson(File.ReadAllText("Assets/PrincessStudio/Samples/Data/PresentationDemo.json"));
                var fontGuid = AssetDatabase.AssetPathToGUID("Assets/PrincessStudio/Samples/Fonts/NotoSansKR.ttf");
                foreach (var locale in data.locales) data.localizedAssets.Add(new LocalizedAssetEntry { key = "ui.font", locale = locale, assetGuid = fontGuid });
                asset.Write(data); AssetDatabase.CreateAsset(asset, Root + "/PresentationDemo.asset");
            }
            var source = new UnityProjectCodec().FromJson(File.ReadAllText("Assets/PrincessStudio/Samples/Data/PresentationDemo.json"));
            var content = asset.Read();
            // Add the new demonstration without replacing an author's existing event edits.
            var sequence = source.events.Find(e => e.id == "gallery-event");
            if (sequence != null && !content.events.Exists(e => e.id == sequence.id)) content.events.Add(sequence);
            foreach (var entry in source.translations)
                if (!content.translations.Exists(t => t.key == entry.key && t.locale == entry.locale)) content.translations.Add(entry);
            asset.Write(content); EditorUtility.SetDirty(asset);
            var skin = AssetDatabase.LoadAssetAtPath<PresentationSkin>(Root + "/DemoSkin.asset");
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<PresentationSkin>();
                foreach (var key in new[] { "body.young", "body.adult", "hair.brown", "hair.silver", "outfit.blue", "outfit.mentor", "outfit.festival", "expression.smile", "expression.tired", "background.garden" })
                    skin.sprites.Add(new SpriteBinding { key = key, sprite = CreateSprite(key) });
                AssetDatabase.CreateAsset(skin, Root + "/DemoSkin.asset");
            }
            LocalizationTableBridge.Sync(asset);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/DemoPanel.asset");
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>(); panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1280, 800);
                const string themePath = Root + "/DemoTheme.tss";
                File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n"); AssetDatabase.ImportAsset(themePath);
                panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
                AssetDatabase.CreateAsset(panel, Root + "/DemoPanel.asset");
            }
            AssetDatabase.SaveAssets();
            const string scenePath = Root + "/PresentationDemo.unity";
            if (!File.Exists(scenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                asset = AssetDatabase.LoadAssetAtPath<GameProjectAsset>(Root + "/PresentationDemo.asset");
                skin = AssetDatabase.LoadAssetAtPath<PresentationSkin>(Root + "/DemoSkin.asset");
                panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/DemoPanel.asset");
                var screen = new GameObject("RaiseArc Game Screen", typeof(UIDocument)); SceneManager.MoveGameObjectToScene(screen, scene);
                screen.GetComponent<UIDocument>().panelSettings = panel;
                var type = Type.GetType("PrincessStudio.Samples.SampleGameController, PrincessStudio.Samples") ?? throw new InvalidOperationException("Sample runtime assembly unavailable.");
                var controller = screen.AddComponent(type); var serialized = new SerializedObject(controller);
                serialized.FindProperty("projectAsset").objectReferenceValue = asset;
                serialized.FindProperty("presentationSkin").objectReferenceValue = skin;
                serialized.FindProperty("screenTemplate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/PrincessStudio/Samples/Runtime/GameScreen.uxml");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(screen, Root + "/GameScreen.prefab");
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            else
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var type = Type.GetType("PrincessStudio.Samples.SampleGameController, PrincessStudio.Samples");
                foreach (var root in scene.GetRootGameObjects())
                {
                    var controller = root.GetComponent(type); if (controller == null) continue;
                    var serialized = new SerializedObject(controller);
                    if (serialized.FindProperty("screenTemplate").objectReferenceValue != null) continue;
                    serialized.FindProperty("screenTemplate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/PrincessStudio/Samples/Runtime/GameScreen.uxml");
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, Root + "/GameScreen.prefab");
                }
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log("RAISEARC_PRESENTATION_READY " + scenePath);
        }

        // Original geometric demo art; all layers share a 256x384 canvas and centered pivot.
        private static Sprite CreateSprite(string key)
        {
            var path = Root + "/Sprites/" + key + ".png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(256, 384, TextureFormat.RGBA32, false);
                var pixels = new Color[256 * 384];
                bool Ellipse(float x, float y, float cx, float cy, float rx, float ry) => (x-cx)*(x-cx)/(rx*rx)+(y-cy)*(y-cy)/(ry*ry)<=1;
                var skin = new Color(1f, .81f, .66f); var ink = new Color(.20f, .15f, .25f);
                for (var y = 0; y < 384; y++) for (var x = 0; x < 256; x++)
                {
                    var color = Color.clear;
                    if (key.StartsWith("body."))
                    {
                        var adult = key == "body.adult";
                        if (Ellipse(x,y,128,282,adult?43:48,adult?55:50) || x>110&&x<146&&y>206&&y<252 || Ellipse(x,y,82,170,13,57) || Ellipse(x,y,174,170,13,57)) color=skin;
                        if ((x>96&&x<120 || x>138&&x<162)&&y>34&&y<115) color=skin;
                        if (Ellipse(x,y,107,32,20,10)||Ellipse(x,y,151,32,20,10)) color=ink;
                    }
                    if (key.StartsWith("hair."))
                    {
                        if (Ellipse(x,y,128,289,57,65) && (y>310 || x<86 || x>170) || x>76&&x<92&&y>204&&y<296 || x>164&&x<180&&y>204&&y<296)
                            color=key=="hair.silver"?new Color(.67f,.72f,.83f):new Color(.30f,.17f,.22f);
                    }
                    if (key.StartsWith("outfit."))
                    {
                        var dress=key=="outfit.festival"?new Color(.81f,.30f,.45f):key=="outfit.mentor"?new Color(.30f,.40f,.39f):new Color(.35f,.47f,.77f);
                        if(y>72&&y<219&&Math.Abs(x-128)<32+(219-y)*.23f) color=dress;
                        if(y>185&&y<199&&Math.Abs(x-128)<40) color=new Color(.96f,.80f,.45f);
                        if(key=="outfit.festival"&&y>80&&y<178&&(x+y)%31<4&&Math.Abs(x-128)<32+(219-y)*.23f) color=new Color(1,.88f,.65f);
                    }
                    if (key.StartsWith("expression."))
                    {
                        if(key=="expression.tired") { if(y>279&&y<283&&(x>104&&x<117||x>139&&x<152)||y>255&&y<259&&x>121&&x<136) color=ink; }
                        else if(Ellipse(x,y,111,284,4,7)||Ellipse(x,y,145,284,4,7)||Ellipse(x,y,128,262,10,7)&&y<262) color=ink;
                    }
                    if(key=="background.garden")
                    {
                        color=Color.Lerp(new Color(.27f,.36f,.40f),new Color(.76f,.75f,.63f),y/384f);
                        if(y<95+18*Math.Sin(x*.03)) color=new Color(.23f,.35f,.29f);
                        if(x%64<6) color=new Color(.18f,.27f,.25f);
                        if(Ellipse(x%64,y,32,305,7,12)) color=new Color(1,.82f,.48f);
                    }
                    pixels[y*256+x]=color;
                }
                texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture); AssetDatabase.ImportAsset(path);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path); importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
                importer.alphaIsTransparency=true; importer.mipmapEnabled=false; importer.textureCompression=TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        public static void PrepareAndExit()
        {
            try
            {
                Create();
                var scenes = EditorBuildSettings.scenes.ToList();
                if (!scenes.Exists(x => x.path == Root + "/PresentationDemo.unity")) scenes.Add(new EditorBuildSettingsScene(Root + "/PresentationDemo.unity", true));
                EditorBuildSettings.scenes = scenes.ToArray();
                UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.BuildPlayerContent(out var result);
                if (!string.IsNullOrEmpty(result.Error)) throw new InvalidOperationException(result.Error);
                EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void BuildAndExit()
        {
            try
            {
                var output = Path.GetFullPath("Builds/PresentationDemo/RaiseArcPresentation.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new[] { Root + "/PresentationDemo.unity" }, output, BuildTarget.StandaloneWindows64, BuildOptions.None);
                Debug.Log("RAISEARC_PRESENTATION_BUILD " + report.summary.result + " errors=" + report.summary.totalErrors);
                if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    Directory.CreateDirectory("Builds/Distribution");
                    AssetDatabase.ExportPackage("Assets/PrincessStudio", "Builds/Distribution/RaiseArc-0.3.0-preview.2.unitypackage", ExportPackageOptions.Recurse);
                }
                EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
            }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
