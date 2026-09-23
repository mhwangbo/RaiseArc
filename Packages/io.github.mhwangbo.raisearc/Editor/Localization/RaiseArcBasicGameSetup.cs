using System;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Editor;
using PrincessStudio.Editor.Localization;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public sealed partial class BasicGameSetup : EditorWindow
    {
        [SerializeField] private GameProjectAsset project;
        [SerializeField] private Texture2D background, character;
        private Label status;
        private static string T(string en, string ko) => StudioText.Language == "ko" ? ko : en;
        [InitializeOnLoadMethod] private static void Register() => StudioIntegrations.CreateBasicGameScreen = Open;
        [MenuItem("Window/RaiseArc/Create basic game screen")]
        public static void OpenMenu() => Open(Selection.activeObject as GameProjectAsset);
        public static void Open(GameProjectAsset asset)
        {
            var w = GetWindow<BasicGameSetup>(typeof(SceneView));
            if (w.HasUnsavedScreen() && w.project != asset) { w.Show(); if (w.status != null) w.status.text = T("Apply the screen before switching games.", "게임을 바꾸기 전에 화면 설정을 적용하세요."); return; }
            if (w.project != asset) { w.project = asset; w.screenDraft = null; w.background = w.character = null; w.lastBuild = ""; }
            w.titleContent = new GUIContent("RaiseArc · Make a game");
            w.minSize = new Vector2(480, 330); w.CreateGUI(); w.Show();
        }
        public void CreateGUI() => DrawSetup();
        public static string Folder(GameProjectAsset asset)
        {
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
            if (string.IsNullOrEmpty(guid)) throw new ArgumentException("Save the GameProjectAsset under Assets first.");
            return "Assets/RaiseArcGames/" + guid;
        }
        public static string Create(GameProjectAsset project, Texture2D background, Texture2D character) => CreateConfigured(project, background, character, new RaiseArc.Unity.GameScreenDefinition());
        public static string CreateConfigured(GameProjectAsset project, Texture2D background, Texture2D character, RaiseArc.Unity.GameScreenDefinition settings)
        {
            var folder = Folder(project); var scenePath = folder + "/Game.unity";
            if (File.Exists(scenePath)) return scenePath;
            GameScreenAuthoring.Validate(project, settings ?? new RaiseArc.Unity.GameScreenDefinition());
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "";
            CreatePresentationAssets(project, background, character);
            var skin = AssetDatabase.LoadAssetAtPath<PresentationSkin>(folder + "/ScreenSkin.asset");
            var bg = skin.Find("screen.background", "en", "en");
            var portrait = skin.Find("screen.character", "en", "en");
            var screen = GameScreenAuthoring.Save(project, skin, settings ?? new RaiseArc.Unity.GameScreenDefinition(), 0);
            if (!AssetDatabase.CopyAsset("Packages/io.github.mhwangbo.raisearc/Editor/Templates/Composer/GameScreen.uss", folder + "/GameScreen.uss"))
                throw new IOException("Could not copy the Composer style template.");
            if (!AssetDatabase.CopyAsset("Packages/io.github.mhwangbo.raisearc/Editor/Templates/Composer/GameScreen.uxml", folder + "/GameScreen.uxml"))
                throw new IOException("Could not copy the Composer screen template.");
            var projectPath = AssetDatabase.GetAssetPath(project);
            var backgroundPath = AssetDatabase.GetAssetPath(bg); var portraitPath = AssetDatabase.GetAssetPath(portrait);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // NewScene unloads unused assets; reacquire references before serializing the new screen.
            project = AssetDatabase.LoadAssetAtPath<GameProjectAsset>(projectPath);
            skin = AssetDatabase.LoadAssetAtPath<PresentationSkin>(folder + "/ScreenSkin.asset");
            bg = AssetDatabase.LoadAssetAtPath<Sprite>(backgroundPath); portrait = AssetDatabase.LoadAssetAtPath<Sprite>(portraitPath);
            var root = new GameObject("RaiseArc Game Screen", typeof(UIDocument)); SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("Screen Camera", typeof(Camera), typeof(AudioListener)); cameraObject.transform.SetParent(root.transform);
            var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.cullingMask = 0;
            try
            {
                root.GetComponent<UIDocument>().panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(folder + "/Panel.asset");
                var type = Type.GetType("PrincessStudio.Samples.SampleGameController, PrincessStudio.Samples") ?? throw new InvalidOperationException("Sample runtime unavailable.");
                var controller = root.AddComponent(type); var serialized = new SerializedObject(controller);
                serialized.FindProperty("projectAsset").objectReferenceValue = project;
                serialized.FindProperty("presentationSkin").objectReferenceValue = skin;
                serialized.FindProperty("screenSettings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RaiseArc.Unity.RaiseArcGameScreenSettings>(folder + "/ScreenSettings.asset");
                serialized.FindProperty("defaultBackground").objectReferenceValue = bg;
                serialized.FindProperty("defaultPortrait").objectReferenceValue = portrait;
                serialized.FindProperty("screenTemplate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "/GameScreen.uxml");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, folder + "/GameScreen.prefab", InteractionMode.AutomatedAction);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Scene was not saved.");
            }
            catch { UnityEngine.Object.DestroyImmediate(root); throw; }
            AssetDatabase.SaveAssets(); return scenePath;
        }
        public static string CreatePresentationAssets(GameProjectAsset project, Texture2D background = null, Texture2D character = null)
        {
            var folder = Folder(project);
            var validation = ProjectValidator.Validate(project.Read()); if (validation.HasErrors) throw new ArgumentException(validation.Summary);
            ValidateLocalizationOwnership(project);
            if (Directory.Exists(folder)) throw new IOException(T("An unfinished setup already exists at ", "미완료 생성 폴더가 있습니다: ") + folder + T(". Inspect it before retrying; nothing was overwritten.", ". 확인 후 다시 시도하세요. 기존 파일은 덮어쓰지 않았습니다."));
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            Sprite CopySprite(Texture2D image, string name)
            {
                var source = image != null ? AssetDatabase.GetAssetPath(image) : null;
                var target = folder + "/" + name + (string.IsNullOrEmpty(source) ? ".png" : Path.GetExtension(source));
                if (string.IsNullOrEmpty(source))
                {
                    var generated = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                    var color = name == "Background" ? new Color(0.28f, 0.36f, 0.48f) : new Color(0.88f, 0.72f, 0.58f);
                    var pixels = Enumerable.Repeat(color, 32 * 32).ToArray();
                    generated.SetPixels(pixels); generated.Apply();
                    File.WriteAllBytes(target, generated.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(generated);
                    AssetDatabase.ImportAsset(target);
                }
                else if (!AssetDatabase.CopyAsset(source, target)) throw new IOException("Could not copy image: " + source);
                var importer = AssetImporter.GetAtPath(target) as TextureImporter ?? throw new ArgumentException("Use an imported image file.");
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(target);
            }
            var bg = CopySprite(background, "Background"); var portrait = CopySprite(character, "Character");
            var codec = new UnityProjectCodec(); var authoring = new AuthoringService(project.Read(), codec);
            authoring.Edit(p =>
            {
                var font = AssetDatabase.AssetPathToGUID("Packages/io.github.mhwangbo.raisearc/Editor/Templates/NativeUI/NotoSansKR-Regular.otf");
                foreach (var locale in p.locales)
                    if (!string.IsNullOrEmpty(font) && !p.localizedAssets.Exists(a => a.key == "ui.font" && a.locale == locale)) p.localizedAssets.Add(new LocalizedAssetEntry { key = "ui.font", locale = locale, assetGuid = font });
            }, authoring.Revision);
            Undo.RecordObject(project, "Set up RaiseArc game screen"); project.Write(authoring.Snapshot()); EditorUtility.SetDirty(project); AssetDatabase.SaveAssetIfDirty(project);
            SyncOwnedLocalization(project);
            var skin = ScriptableObject.CreateInstance<PresentationSkin>(); AssetDatabase.CreateAsset(skin, folder + "/ScreenSkin.asset");
            skin.sprites.Add(new SpriteBinding { key = "screen.background", sprite = bg });
            skin.sprites.Add(new SpriteBinding { key = "screen.character", sprite = portrait }); EditorUtility.SetDirty(skin);
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            AssetDatabase.CreateAsset(panel, folder + "/Panel.asset");
            AssetDatabase.SaveAssets();
            return folder;
        }
        public static void CreateSixTurnGame()
        {
            throw new NotSupportedException("The legacy six-turn sample is not included in the separated package.");
        }
        public static ProjectDefinition SixTurnDefinition()
        {
            throw new NotSupportedException("The legacy six-turn sample is not included in the separated package.");
        }
    }
}
