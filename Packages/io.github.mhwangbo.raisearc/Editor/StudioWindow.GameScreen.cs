using System;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public sealed partial class StudioWindow
    {
        private void DrawGameScreen(ProjectDefinition p)
        {
            var scroll = new ScrollView(); content.Add(scroll); scroll.style.flexGrow = 1;
            scroll.Add(Button("Create basic game screen", () => StudioIntegrations.CreateBasicGameScreen?.Invoke(asset), true));
            scroll.Add(new HelpBox(StudioText.T("Start with one background and one finished character image. Advanced appearance layers below are optional."), HelpBoxMessageType.Info));
            var skinField = new ObjectField(StudioText.T("Screen skin")) { objectType = typeof(PresentationSkin), allowSceneObjects = false, value = screenSkin };
            skinField.RegisterValueChangedCallback(e => { screenSkin = e.newValue as PresentationSkin; Render(); }); scroll.Add(skinField);
            scroll.Add(Button("Create skin", () =>
            {
                var path = EditorUtility.SaveFilePanelInProject("Create presentation skin", "PresentationSkin", "asset", "Choose a location.");
                if (string.IsNullOrEmpty(path)) return;
                screenSkin = CreateInstance<PresentationSkin>(); AssetDatabase.CreateAsset(screenSkin, path); Render();
            }));
            if (screenSkin == null) { scroll.Add(new Label(StudioText.T("Select a skin to map appearance keys to Sprite assets and preview the runtime stage."))); return; }
            var skin = screenSkin;
            void EditSkin(Action edit) { Undo.RecordObject(skin, "Edit presentation skin"); edit(); EditorUtility.SetDirty(skin); }
            void ColorField(string label, Color value, Action<Color> set)
            {
                var field = new UnityEditor.UIElements.ColorField(StudioText.T(label)) { value = value }; field.RegisterValueChangedCallback(e => EditSkin(() => set(e.newValue))); scroll.Add(field);
            }
            ColorField("Background", skin.background, x => skin.background = x);
            ColorField("Text", skin.foreground, x => skin.foreground = x);
            ColorField("Accent", skin.accent, x => skin.accent = x);
            Number(scroll, "Stage height (180–720)", skin.stageHeight, x => EditSkin(() => skin.stageHeight = Mathf.Clamp(x, 180, 720)));
            var mapping = new Foldout { text = StudioText.T("Sprite resources · common canvas/pivot required"), value = false }; scroll.Add(mapping);
            foreach (var binding in skin.sprites.ToArray())
            {
                var row = new Foldout { text = StudioText.T(binding.key )}; mapping.Add(row);
                Text(row, "Resource key", binding.key, x => EditSkin(() => binding.key = x));
                var locales = new System.Collections.Generic.List<string> { "" }; locales.AddRange(p.locales);
                var locale = StudioText.Dropdown("Locale (empty = shared)", locales, Math.Max(0, locales.IndexOf(binding.locale)));
                locale.RegisterValueChangedCallback(e => EditSkin(() => binding.locale = e.newValue)); row.Add(locale);
                var sprite = new ObjectField(StudioText.T("Sprite")) { objectType = typeof(Sprite), allowSceneObjects = false, value = binding.sprite };
                sprite.RegisterValueChangedCallback(e => EditSkin(() => binding.sprite = e.newValue as Sprite)); row.Add(sprite);
                row.Add(Button("Remove binding", () => { EditSkin(() => skin.sprites.Remove(binding)); Render(); }));
            }
            mapping.Add(Button("+ Sprite resource", () => { EditSkin(() => skin.sprites.Add(new SpriteBinding { key = "appearance.new-" + Guid.NewGuid().ToString("N").Substring(0, 6) })); Render(); }));
            scroll.Add(Button("Save skin / refresh preview", () => { AssetDatabase.SaveAssetIfDirty(skin); Render(); }));
            scroll.Add(Button("Export game screen prefab", () => Safe(() =>
            {
                var path = EditorUtility.SaveFilePanelInProject("Create game screen", "GameScreen", "prefab", "The current project, skin and localization will be connected automatically.");
                if (string.IsNullOrEmpty(path)) return;
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PrincessStudio.Editor.Localization.GameScreenPrefabBuilder")).FirstOrDefault(t => t != null);
                if (type == null) throw new InvalidOperationException("Create a basic game screen for this project first.");
                type.GetMethod("Export").Invoke(null, new object[] { asset, skin, path });
                Notify("Game screen prefab created. Drag it into your scene and enter Play Mode.");
            }), true));
            foreach (var issue in skin.ValidateBindings(p).Distinct()) scroll.Add(new HelpBox(StudioText.T(issue), HelpBoxMessageType.Warning));
            var session = new GameSession(p);
            var state = session.Capture();
            var previewStage = new PresentationStage(p, skin);
            scroll.Add(previewStage.Element);
            scroll.RegisterCallback<DetachFromPanelEvent>(_ => previewStage.Dispose());
            PresentationStep selectedStep = null;
            var diagnostics = new Label(); diagnostics.style.whiteSpace = WhiteSpace.Normal; scroll.Add(diagnostics);
            void Refresh()
            {
                Safe(() =>
                {
                    session.Restore(state); previewStage.Render(session.State, p.defaultLocale);
                    var resolver = new AppearanceResolver(p);
                    var stageState = session.State.CaptureStage();
                    diagnostics.text = string.Join("\n", p.actors.Select(actor => { var r = resolver.Resolve(session.State, actor.id, stageState.actors.Find(x => x.actorId == actor.id)); return actor.id + " · age " + session.State.ActorAges[actor.id] + " · " + r.slots.body + " / " + r.slots.hair + " / " + r.slots.outfit + " / " + r.slots.expression + " · rules: " + string.Join(", ", r.ruleIds); }));
                });
            }
            scroll.Add(new Label(StudioText.T("Preview state is temporary and never written to the project or a save slot.")));
            Number(scroll, "Preview day", state.day, x => { state.day = Mathf.Clamp(x, 0, p.durationDays); Refresh(); });
            foreach (var value in state.stats) Number(scroll, "Protagonist / " + value.id, value.value, x => { value.value = x; Refresh(); });
            foreach (var value in state.actorValues) Number(scroll, "Actor / " + value.id, value.value, x => { value.value = x; Refresh(); });
            foreach (var value in state.flags) Toggle(scroll, "Flag / " + value.id, value.value == 1, x => { value.value = x ? 1 : 0; Refresh(); });
            var steps = p.events.SelectMany(x => x.presentation).ToList();
            scroll.Add(new Label(StudioText.T("Sequence preview follows the first structural path to the step; it does not apply choice effects. Use Test play to exercise a specific branch.")));
            SelectReference(scroll, "Sequence preview", steps, "", x =>
            {
                selectedStep = steps.Find(step => step.id == x);
                var e = p.events.Find(entry => entry.presentation.Contains(selectedStep));
                state.pendingEventId = e?.id ?? "";
                state.presentationStepId = selectedStep?.id ?? "";
                state.presentationPath = e == null ? new System.Collections.Generic.List<string>() : EventSequence.PreviewPath(e, selectedStep.id);
                Refresh();
            }, true);
            Refresh();
        }
    }
}
