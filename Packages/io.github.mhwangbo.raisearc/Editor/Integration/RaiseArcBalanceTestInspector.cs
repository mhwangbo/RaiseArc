using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    [CustomEditor(typeof(RaiseArcBalanceTestAsset))] public sealed class BalanceTestInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var test = (RaiseArcBalanceTestAsset)target;
            EditorGUILayout.LabelField(test.Read().name, EditorStyles.boldLabel); EditorGUILayout.LabelField(test.Read().id);
            EditorGUILayout.LabelField("Test revision", test.Revision.ToString());
            if (GUILayout.Button("Open in Simulation Explorer / 탐색기에서 열기")) BalanceTests.Open(test);
        }
        [UnityEditor.Callbacks.OnOpenAsset] private static bool OpenAsset(EntityId id, int line)
        {
            if (!(EditorUtility.EntityIdToObject(id) is RaiseArcBalanceTestAsset test)) return false;
            BalanceTests.Open(test); return true;
        }
    }
}
