using PrincessStudio.Editor;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    [CustomEditor(typeof(RaiseArcEndingTestAsset))]
    public sealed class EndingTestInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (RaiseArcEndingTestAsset)target;
            var definition = asset.Read();
            var korean = StudioText.Language == "ko";
            EditorGUILayout.LabelField(definition?.name ?? "Ending test", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(korean ? "게임 프로젝트" : "Game project", asset.Project, typeof(PrincessStudio.Unity.GameProjectAsset), false);
            EditorGUILayout.LabelField(korean ? "목표 엔딩" : "Target ending", definition?.endingId ?? "");
            EditorGUILayout.LabelField(korean ? "테스트 리비전" : "Test revision", asset.Revision.ToString());
            if (GUILayout.Button(korean ? "Simulation Explorer에서 테스트 편집" : "Edit test in Simulation Explorer")) EndingTests.Open(asset);
        }
        [UnityEditor.Callbacks.OnOpenAsset]
        private static bool OpenAsset(EntityId instanceId, int line)
        {
            if (!(EditorUtility.EntityIdToObject(instanceId) is RaiseArcEndingTestAsset test)) return false;
            EndingTests.Open(test); return true;
        }
    }
}
