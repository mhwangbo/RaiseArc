using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    [CustomEditor(typeof(RaiseArcUIBindings))]
    public sealed class RaiseArcUIBindingsInspector : UnityEditor.Editor
    {
        private VisualTreeAsset scanned;
        private VisualElement tree;
        private string[] names = Array.Empty<string>();
        private string[] labels = Array.Empty<string>();
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Build layout and styles in UI Builder. Here, connect named standard elements to data and actions. / 배치·스타일은 UI Builder에서, 기능 연결은 여기서 편집합니다.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("project"), new GUIContent("Game project / 게임"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("document"), new GUIContent("UXML document / 화면"));
            var asset = (RaiseArcUIBindings)target;
            var document = (VisualTreeAsset)serializedObject.FindProperty("document").objectReferenceValue;
            if (document != scanned || tree == null) Scan(document);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open UI Builder / 화면 편집") && document != null) AssetDatabase.OpenAsset(document);
                if (GUILayout.Button("Refresh targets / 대상 새로고침")) Scan(document);
            }
            var project = serializedObject.FindProperty("project").objectReferenceValue as GameProjectAsset;
            var data = project != null ? project.Read() : null;
            var connections = serializedObject.FindProperty("connections");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("ELEMENT CONNECTIONS / 요소 연결", EditorStyles.boldLabel);
            for (var i = 0; i < connections.arraySize; i++)
            {
                var c = connections.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    c.isExpanded = EditorGUILayout.Foldout(c.isExpanded, c.FindPropertyRelative("target").stringValue + "  →  " +
                        (c.FindPropertyRelative("data").enumValueIndex != 0 ? c.FindPropertyRelative("data").enumDisplayNames[c.FindPropertyRelative("data").enumValueIndex] : c.FindPropertyRelative("command").enumDisplayNames[c.FindPropertyRelative("command").enumValueIndex]), true);
                    if (!c.isExpanded) continue;
                    Target(c.FindPropertyRelative("target"), "Element / 요소");
                    var field = c.FindPropertyRelative("data");
                    field.enumValueIndex = (int)(UIData)EditorGUILayout.EnumPopup("Display / 표시 데이터", (UIData)field.enumValueIndex);
                    if ((UIData)field.enumValueIndex == UIData.Stat)
                        Content(c.FindPropertyRelative("contentId"), data?.stats.Cast<Definition>(), data, "Stat / 능력치");
                    if ((UIData)field.enumValueIndex == UIData.LocalizedText)
                    {
                        var keys = data?.translations.Select(t => t.key).Distinct().OrderBy(k => k).ToArray() ?? Array.Empty<string>();
                        Choose(c.FindPropertyRelative("contentId"), "Translation key / 번역 키", keys, keys);
                    }
                    var command = c.FindPropertyRelative("command");
                    command.enumValueIndex = (int)(UICommand)EditorGUILayout.EnumPopup("Action / 버튼 행동", (UICommand)command.enumValueIndex);
                    if ((UICommand)command.enumValueIndex == UICommand.OpenPanel || (UICommand)command.enumValueIndex == UICommand.ClosePanel)
                        Target(c.FindPropertyRelative("panel"), "Panel / 표시 패널");
                    if (GUILayout.Button("Remove connection / 연결 삭제")) { connections.DeleteArrayElementAtIndex(i); break; }
                }
            }
            if (GUILayout.Button("Add connection / 연결 추가"))
            {
                var i = connections.arraySize++; var c = connections.GetArrayElementAtIndex(i); c.isExpanded = true;
                c.FindPropertyRelative("target").stringValue = ""; c.FindPropertyRelative("data").enumValueIndex = 0;
                c.FindPropertyRelative("command").enumValueIndex = 0; c.FindPropertyRelative("contentId").stringValue = ""; c.FindPropertyRelative("panel").stringValue = "";
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("REPEATED TEMPLATES / 반복 템플릿", EditorStyles.boldLabel);
            var repeats = serializedObject.FindProperty("repeats");
            for (var i = 0; i < repeats.arraySize; i++)
            {
                var row = repeats.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    Target(row.FindPropertyRelative("target"), "Container / 목록 영역");
                    EditorGUILayout.PropertyField(row.FindPropertyRelative("items"), new GUIContent("Items / 항목"));
                    EditorGUILayout.PropertyField(row.FindPropertyRelative("template"), new GUIContent("Template bindings / 템플릿"));
                    if (row.FindPropertyRelative("items").enumValueIndex == (int)UIItems.Activities)
                    {
                        var category = row.FindPropertyRelative("category");
                        var categories = new[] { "" }.Concat(data?.activities.Select(a => string.IsNullOrEmpty(a.categoryId) ? a.category.ToString() : a.categoryId).Distinct() ?? Enumerable.Empty<string>()).ToList();
                        Choose(category, "Category / 분류", categories.ToArray(), categories.Select(x => x == "" ? "All / 전체" : x).ToArray());
                        EditorGUILayout.PropertyField(row.FindPropertyRelative("order"), new GUIContent("Order / 정렬"));
                    }
                    if (GUILayout.Button("Edit template / 템플릿 선택")) Selection.activeObject = row.FindPropertyRelative("template").objectReferenceValue;
                    if (GUILayout.Button("Remove repeat / 반복 삭제")) { repeats.DeleteArrayElementAtIndex(i); break; }
                }
            }
            if (GUILayout.Button("Add repeat / 반복 추가"))
            {
                var i = repeats.arraySize++; var p = repeats.GetArrayElementAtIndex(i);
                p.FindPropertyRelative("target").stringValue = ""; p.FindPropertyRelative("items").enumValueIndex = 0;
                p.FindPropertyRelative("template").objectReferenceValue = null; p.FindPropertyRelative("category").stringValue = ""; p.FindPropertyRelative("order").enumValueIndex = 0;
            }
            serializedObject.ApplyModifiedProperties();
            if (tree != null)
                foreach (var error in asset.Validate(tree, asset.repeats.Count == 0))
                    EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button("Save bindings / 연결 저장")) AssetDatabase.SaveAssetIfDirty(asset);
        }
        private void Scan(VisualTreeAsset document)
        {
            scanned = document; tree = document == null ? null : document.CloneTree();
            var all = tree == null ? new List<VisualElement>() : tree.Query<VisualElement>().ToList().Where(e => !string.IsNullOrEmpty(e.name)).ToList();
            names = new[] { "" }.Concat(all.Select(e => e.name).Distinct().OrderBy(x => x)).ToArray();
            labels = names.Select(n => n == "" ? "Select… / 선택…" : n + " (" + all.First(e => e.name == n).GetType().Name + ")").ToArray();
        }
        private void Target(SerializedProperty p, string label) => Choose(p, label, names, labels);
        private static void Choose(SerializedProperty p, string label, string[] values, string[] display)
        {
            var index = Array.IndexOf(values, p.stringValue);
            if (index < 0) { values = values.Concat(new[] { p.stringValue }).ToArray(); display = display.Concat(new[] { p.stringValue + " (missing / 누락)" }).ToArray(); index = values.Length - 1; }
            if (values.Length == 0) return;
            p.stringValue = values[EditorGUILayout.Popup(label, index, display)];
        }
        private static void Content(SerializedProperty p, IEnumerable<Definition> entries, ProjectDefinition data, string label)
        {
            var list = (entries ?? Enumerable.Empty<Definition>()).ToList();
            var values = new[] { "" }.Concat(list.Select(x => x.id)).ToArray();
            var labels = new[] { "Select… / 선택…" }.Concat(list.Select(x => data.translations.FirstOrDefault(t => t.key == x.nameKey && t.locale == "en")?.text ?? x.id)).ToArray();
            Choose(p, label, values, labels);
        }
    }

    [CustomEditor(typeof(RaiseArcUIDocument))]
    public sealed class RaiseArcUIDocumentInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var view = (RaiseArcUIDocument)target;
            var document = view.GetComponent<UIDocument>();
            if (view.Game == null) EditorGUILayout.HelpBox("Select the game's owner object; do not place the owner inside a removable panel.", MessageType.Error);
            if (view.Bindings == null) EditorGUILayout.HelpBox("Select or create a UI bindings asset.", MessageType.Error);
            else
            {
                if (document.visualTreeAsset != view.Bindings.document) EditorGUILayout.HelpBox("UIDocument Source Asset differs from the bindings UXML.", MessageType.Error);
                if (GUILayout.Button("Edit bindings / 연결 편집")) Selection.activeObject = view.Bindings;
            }
            if (Application.isPlaying && GUILayout.Button("Reconnect view / 화면 재연결")) view.Reconnect();
        }
    }
}
