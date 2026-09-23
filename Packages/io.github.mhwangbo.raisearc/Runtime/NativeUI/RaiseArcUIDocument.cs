using System.Collections;
using System.Collections.Generic;
using PrincessStudio.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.UI
{
    [DisallowMultipleComponent, RequireComponent(typeof(UIDocument))]
    public sealed class RaiseArcUIDocument : MonoBehaviour
    {
        [SerializeField] private RaiseArcGame game;
        [SerializeField] private RaiseArcUIBindings bindings;
        [SerializeField, Tooltip("Use the game locale font. Leave off to keep your own UI font. / 게임 언어별 글꼴 사용. 사용자 글꼴은 끄세요.")] private bool useProjectFont;
        private RaiseArcUIConnection connection;
        private LocalizedFontBinding font;
        private UIDocument document;
        private VisualElement connectedRoot;
        public RaiseArcGame Game => game;
        public RaiseArcUIBindings Bindings => bindings;
        public IReadOnlyList<string> Errors => connection?.Errors ?? new[] { "The view is not connected." };
        public void Configure(RaiseArcGame owner, RaiseArcUIBindings map, bool projectFont = false) { game = owner; bindings = map; useProjectFont = projectFont; }
        private void OnEnable() { document = GetComponent<UIDocument>(); if (Application.isPlaying) StartCoroutine(ConnectAfterDocument()); }
        private void LateUpdate()
        {
            // UIDocument rebuilds its root when re-enabled or its UXML is replaced.
            if (Application.isPlaying && document.rootVisualElement != connectedRoot) Reconnect();
        }
        private IEnumerator ConnectAfterDocument()
        {
            yield return null;
            Reconnect();
        }
        public void Reconnect()
        {
            connection?.Dispose(); font?.Dispose(); connection = null; font = null;
            if (!Application.isPlaying) return;
            document = GetComponent<UIDocument>();
            var root = document.rootVisualElement; connectedRoot = root;
            if (root == null) return;
            root.Q("raisearc-configuration-error")?.RemoveFromHierarchy();
            if (game == null || bindings == null)
            {
                root.Add(new Label("RaiseArc: select the explicit Game and UI bindings in the Inspector.") { name = "raisearc-configuration-error" });
                return;
            }
            if (document.visualTreeAsset != bindings.document)
            {
                root.Add(new Label("RaiseArc: UIDocument and bindings must use the same UXML.") { name = "raisearc-configuration-error" });
                return;
            }
            connection = new RaiseArcUIConnection(game, root, bindings);
            if (connection.Errors.Count != 0)
                root.Add(new Label(string.Join("\n", connection.Errors)) { name = "raisearc-configuration-error" });
            else if (useProjectFont && game.Project.Read().localizedAssets.Exists(a => a.key == "ui.font"))
                font = new LocalizedFontBinding(root, "Princess." + game.Project.Read().id + ".Assets", "ui.font");
        }
        private void OnDisable()
        {
            StopAllCoroutines(); connectedRoot = null;
            connection?.Dispose(); connection = null;
            font?.Dispose(); font = null;
        }
    }
}
