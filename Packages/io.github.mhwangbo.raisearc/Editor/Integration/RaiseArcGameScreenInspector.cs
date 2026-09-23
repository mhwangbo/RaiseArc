using PrincessStudio.Editor;
using RaiseArc.Unity;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    [CustomEditor(typeof(RaiseArcGameScreenSettings))]
    public sealed class GameScreenInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var screen = (RaiseArcGameScreenSettings)target;
            EditorGUILayout.ObjectField("Game Project", screen.Project, typeof(PrincessStudio.Unity.GameProjectAsset), false);
            EditorGUILayout.ObjectField("Image bindings", screen.Skin, typeof(PrincessStudio.Unity.PresentationSkin), false);
            EditorGUILayout.LabelField("Screen revision", screen.Revision.ToString());
            EditorGUILayout.HelpBox(StudioText.Language == "ko" ? "화면 설정은 게임 규칙·세이브와 별도로 저장됩니다." : "Screen settings are stored separately from game rules and player saves.", MessageType.Info);
            if (GUILayout.Button(StudioText.Language == "ko" ? "화면 설정 편집" : "Edit screen settings")) StudioIntegrations.CreateBasicGameScreen?.Invoke(screen.Project);
        }
    }
}
