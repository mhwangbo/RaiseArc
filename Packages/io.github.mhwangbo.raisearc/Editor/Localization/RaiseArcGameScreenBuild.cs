using System;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Editor;
using PrincessStudio.Editor.Localization;
using PrincessStudio.Unity;
using RaiseArc.Unity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RaiseArc.Editor
{
    public sealed partial class BasicGameSetup
    {
        private static void ValidateLocalizationOwnership(GameProjectAsset asset)
        {
            var id = asset.Read().id;
            var root = "Assets/PrincessStudioContent/Localization/" + id + "/";
            var strings = LocalizationEditorSettings.GetStringTableCollection("Princess." + id);
            var assets = LocalizationEditorSettings.GetAssetTableCollection("Princess." + id + ".Assets");
            foreach (var collection in new UnityEngine.Object[] { strings, assets })
                if (collection != null && !AssetDatabase.GetAssetPath(collection).StartsWith(root, StringComparison.Ordinal))
                    throw new InvalidOperationException(T("This project's localization collection is managed elsewhere. Use Studio Localization to synchronize it first.", "이 프로젝트의 현지화 테이블은 다른 위치에서 관리됩니다. 먼저 Studio 현지화에서 연결을 확인하세요."));
        }
        private static void SyncOwnedLocalization(GameProjectAsset asset)
        {
            ValidateLocalizationOwnership(asset);
            LocalizationTableBridge.Sync(asset);
        }
        private void ApplyToGame()
        {
            if (project == null) throw new InvalidOperationException(T("Select or create a game first.", "게임을 선택하거나 먼저 만드세요."));
            var saved = GameScreenAuthoring.Find(project);
            if ((saved == null ? 0 : saved.Revision) != screenRevision)
                throw new InvalidOperationException(T("Screen settings changed elsewhere. Reload saved screen settings before applying.", "다른 곳에서 화면 설정을 변경했습니다. 저장된 화면 설정을 다시 읽은 뒤 적용하세요."));
            GameScreenAuthoring.Validate(project, screenDraft);
            if (EditorApplication.isPlaying && (saved == null || background != null || character != null || activityImage != null))
                throw new InvalidOperationException(T("Stop Play before creating a screen or importing images.", "화면 생성이나 이미지 교체 전에 Play를 중지하세요."));
            var folder = Folder(project);
            var created = !File.Exists(folder + "/Game.unity");
            if (activityImage != null && !project.Read().activities.Exists(a => a.id == activityImageId)) throw new ArgumentException("Activity no longer exists.");
            if (!File.Exists(folder + "/Game.unity"))
            {
                if (string.IsNullOrEmpty(CreateConfigured(project, background, character, screenDraft))) throw new OperationCanceledException();
                saved = GameScreenAuthoring.Find(project);
            }
            else
            {
                if (saved == null) throw new InvalidOperationException(T("This older screen has no screen settings. Keep its custom scene and use Studio Game screen to edit it.", "이전 방식의 화면에는 화면 설정이 없습니다. 기존 Scene을 보존하고 Studio 게임 화면에서 편집하세요."));
                if (!EditorApplication.isPlaying) SyncOwnedLocalization(project);
                saved = GameScreenAuthoring.Save(project, saved.Skin, screenDraft, screenRevision);
            }
            var skin = saved.Skin;
            void SetImage(string key, Texture2D image)
            {
                if (image == null) return;
                var source = AssetDatabase.GetAssetPath(image);
                var target = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + key.Replace('.', '-') + Path.GetExtension(source));
                if (!AssetDatabase.CopyAsset(source, target)) throw new IOException("Could not copy image: " + source);
                var importer = AssetImporter.GetAtPath(target) as TextureImporter ?? throw new ArgumentException("Use an imported image file.");
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.SaveAndReimport();
                Undo.RecordObject(skin, "Change RaiseArc screen image");
                var binding = skin.sprites.Find(x => x.key == key && (x.locale ?? "") == imageLocale);
                if (binding == null) { binding = new SpriteBinding { key = key, locale = imageLocale }; skin.sprites.Add(binding); }
                binding.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(target); EditorUtility.SetDirty(skin);
            }
            if (!created || imageLocale.Length > 0) { SetImage("screen.background", background); SetImage("screen.character", character); }
            if (activityImage != null)
            {
                var service = new AuthoringService(project.Read(), new UnityProjectCodec());
                var activity = service.Snapshot().activities.Find(a => a.id == activityImageId) ?? throw new ArgumentException("Activity no longer exists.");
                var key = string.IsNullOrEmpty(activity.imageKey) ? "activity-image." + activity.id : activity.imageKey;
                SetImage(key, activityImage);
                if (activity.imageKey != key)
                {
                    service.Edit(p => p.activities.Find(a => a.id == activityImageId).imageKey = key, service.Revision);
                    Undo.RecordObject(project, "Assign activity image"); project.Write(service.Snapshot()); EditorUtility.SetDirty(project); AssetDatabase.SaveAssetIfDirty(project);
                }
            }
            AssetDatabase.SaveAssetIfDirty(skin);
            screenRevision = saved.Revision; background = character = activityImage = null;
            CreateGUI(); status.text = T("Applied screen revision ", "화면 설정 저장 · revision ") + screenRevision;
        }
        private void OpenGameScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException(T("Stop Play before opening a scene.", "Scene을 열기 전에 Play를 중지하세요."));
            var path = Folder(project) + "/Game.unity";
            if (!File.Exists(path)) throw new InvalidOperationException(T("Apply to game / create screen first.", "먼저 게임에 적용 / 화면 만들기를 누르세요."));
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) throw new OperationCanceledException();
            EditorSceneManager.OpenScene(path); EditorApplication.ExecuteMenuItem("Window/General/Game");
        }
        private void BuildWindows()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException(T("Stop Play before building.", "빌드 전에 Play를 중지하세요."));
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException(T("Install Windows Build Support for this Editor in Unity Hub.", "Unity Hub에서 이 Editor의 Windows Build Support를 설치하세요."));
            ApplyToGame();
            var target = EditorUtility.SaveFilePanel(T("Build into a new empty folder", "새 빈 폴더에 빌드"), "", "MyGame", "exe");
            if (string.IsNullOrEmpty(target)) return;
            var directory = Path.GetDirectoryName(target);
            if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException(T("Choose an empty folder to preserve previous builds.", "기존 빌드를 보존하도록 빈 폴더를 선택하세요."));
            var scenes = new[] { Folder(project) + "/Game.unity" }.Concat(project.Read().modules.Select(m => m.scenePath).Where(s => !string.IsNullOrEmpty(s))).Distinct().ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = target, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            if (report.summary.result != BuildResult.Succeeded) throw new IOException("Build failed: " + report.summary.result + "; errors: " + report.summary.totalErrors);
            lastBuild = target; CreateGUI(); status.text = T("Built: ", "빌드 완료: ") + target;
        }
    }
}
