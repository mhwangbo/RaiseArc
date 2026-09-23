using System;
using System.Linq;
using PrincessStudio.Unity;
using RaiseArc.Unity;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    public static class GameScreenAuthoring
    {
        public static string Folder(GameProjectAsset project)
        {
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(project));
            if (string.IsNullOrEmpty(guid)) throw new ArgumentException("Save the Game Project asset first. / 먼저 게임 프로젝트를 저장하세요.");
            return "Assets/RaiseArcGames/" + guid;
        }
        public static RaiseArcGameScreenSettings Find(GameProjectAsset project) => project == null ? null : AssetDatabase.LoadAssetAtPath<RaiseArcGameScreenSettings>(Folder(project) + "/ScreenSettings.asset");
        public static void Validate(GameProjectAsset project, GameScreenDefinition value)
        {
            if (project == null || value == null || value.version != 1) throw new ArgumentException("Select a project and supported screen definition.");
            if (!Enum.IsDefined(typeof(GameProgression), value.progression) || !Enum.IsDefined(typeof(GameScreenLayout), value.layout) || !Enum.IsDefined(typeof(GameScreenTheme), value.theme)) throw new ArgumentException("Unknown screen preset.");
            if (value.fontSize < 14 || value.fontSize > 32 || value.portraitScale < .25f || value.portraitScale > 2 || float.IsNaN(value.portraitScale)) throw new ArgumentException("Font size: 14–32; character size: 0.25–2. / 글자 크기: 14~32, 캐릭터 크기: 0.25~2.");
            var data = project.Read();
            ScreenComposition.Validate(value.parts);
            if (value.parts.Any(p => !string.IsNullOrEmpty(p.category) && !data.activities.Any(a => (string.IsNullOrEmpty(a.categoryId) ? a.category.ToString() : a.categoryId) == p.category)))
                throw new ArgumentException("A screen part refers to a deleted activity category.");
            if (value.visibleStats == null || value.activityCategories == null || value.visibleStats.Any(id => !data.stats.Any(s => s.id == id)) || value.activityCategories.Any(id => !data.activities.Any(a => (string.IsNullOrEmpty(a.categoryId) ? a.category.ToString() : a.categoryId) == id)))
                throw new ArgumentException("A displayed stat or activity category no longer exists. Reload and update the screen selection. / 삭제된 능력치·분류가 있습니다. 화면 선택을 다시 확인하세요.");
        }
        public static RaiseArcGameScreenSettings Save(GameProjectAsset project, PresentationSkin skin, GameScreenDefinition value, int expectedRevision, RaiseArcGameScreenSettings target = null)
        {
            var existing = target != null ? target : Find(project);
            if ((existing == null ? 0 : existing.Revision) != expectedRevision) throw new InvalidOperationException("Screen revision conflict. Reload screen settings. / 화면 설정이 변경되었습니다. 다시 읽어 주세요.");
            Validate(project, value);
            if (skin == null) throw new ArgumentException("The screen skin is missing.");
            if (existing == null)
            {
                existing = ScriptableObject.CreateInstance<RaiseArcGameScreenSettings>(); existing.Write(project, skin, value);
                AssetDatabase.CreateAsset(existing, Folder(project) + "/ScreenSettings.asset");
                Undo.RegisterCreatedObjectUndo(existing, "Create RaiseArc screen settings");
            }
            else
            {
                if (existing.Project != project) throw new ArgumentException("Screen belongs to another game.");
                Undo.RecordObject(existing, "Edit RaiseArc screen settings"); existing.Write(project, skin, value); EditorUtility.SetDirty(existing);
            }
            AssetDatabase.SaveAssetIfDirty(existing); return existing;
        }
    }
}
