using System.Collections.Generic;
using PrincessStudio.Core;
using PrincessStudio.Localization;
using PrincessStudio.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Samples
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class ForestSampleEndpoint : SceneModuleEndpoint
    {
        [SerializeField] private GameProjectAsset projectAsset;
        private readonly List<LocalizedLabelBinding> bindings = new List<LocalizedLabelBinding>();
        private ModuleEncounter encounter;
        private LocalizedFontBinding fontBinding;
        public void Configure(GameProjectAsset asset)
        {
            projectAsset = asset;
        }
        protected override void OnBegin(ModuleContext context)
        {
            var p = projectAsset.Read();
            var module = p.modules.Find(m => m.id == context.ModuleId);
            encounter = module.encounters[0];
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.style.backgroundColor = new Color(0.06f, 0.14f, 0.12f);
            root.style.flexGrow = 1;
            root.style.paddingTop = 90;
            root.style.paddingLeft = 70;
            root.style.paddingRight = 70;
            root.style.color = Color.white;
            if (p.localizedAssets.Exists(a => a.key == "ui.font"))
                fontBinding = new LocalizedFontBinding(root, "Princess." + p.id + ".Assets", "ui.font");
            var title = new Label();
            title.style.fontSize = 32;
            root.Add(title);
            bindings.Add(new LocalizedLabelBinding(title, "Princess." + p.id, encounter.nameKey));
            var body = new Label();
            body.style.whiteSpace = WhiteSpace.Normal;
            root.Add(body);
            bindings.Add(new LocalizedLabelBinding(body, "Princess." + p.id, encounter.descriptionKey));
            var button = new Button(GatherAndReturn);
            var text = new Label();
            button.Add(text);
            bindings.Add(new LocalizedLabelBinding(text, "Princess." + p.id, "ui.forest.return"));
            button.style.height = 56;
            root.Add(button);
        }
        public void GatherAndReturn()
        {
            if (encounter == null)
                throw new System.InvalidOperationException("Forest module is not initialized.");
            Complete(new ModuleResult { elapsedDays = 3, messageKey = encounter.nameKey, effects = encounter.rewards });
        }
        protected override void OnDestroy()
        {
            fontBinding?.Dispose();
            foreach (var binding in bindings)
                binding.Dispose();
            base.OnDestroy();
        }
    }
}
