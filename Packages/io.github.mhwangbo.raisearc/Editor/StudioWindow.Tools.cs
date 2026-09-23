using System;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor
{
    public sealed partial class StudioWindow
    {
        private void DrawLocalization(ProjectDefinition p)
        {
            var scroll = new ScrollView();
            content.Add(scroll);
            scroll.style.flexGrow = 1;
            StringList(scroll, "Enabled locales", p.locales);
            Text(scroll, "Default locale", p.defaultLocale, x => p.defaultLocale = x);
            Text(scroll, "Fallback locale", p.fallbackLocale, x => p.fallbackLocale = x);
            var actions = Row();
            actions.Add(Button("Save language settings", () => Mutate(() => service.Replace(p, service.Revision)), true));
            actions.Add(Button("Generate missing keys", () => Mutate(() => service.Edit(next =>
            {
                foreach (var d in next.AllDefinitions())
                    foreach (var key in new[] { d.nameKey, d.descriptionKey }.Where(x => !string.IsNullOrEmpty(x)))
                        foreach (var locale in next.locales)
                            if (!next.translations.Exists(t => t.key == key && t.locale == locale))
                                next.translations.Add(new TranslationEntry { key = key, locale = locale, text = "", draft = true });
            }, service.Revision))));
            actions.Add(Button("Export CSV", () => { var path = EditorUtility.SaveFilePanel("Export translations", "", "translations.csv", "csv"); if (path.Length > 0) Safe(() => File.WriteAllText(path, TranslationCsv.Export(service.Snapshot()))); }));
            actions.Add(Button("Import CSV", () => { var path = EditorUtility.OpenFilePanel("Import translations", "", "csv"); if (path.Length > 0) Mutate(() => TranslationCsv.Import(service, File.ReadAllText(path), service.Revision)); }));
            actions.Add(Button("Sync Unity tables", () => Safe(() => { if (StudioIntegrations.SyncLocalization == null) throw new InvalidOperationException("Install Unity Localization 1.5 or newer using Package Manager."); StudioIntegrations.SyncLocalization(asset); Notify("Unity String and Asset tables synchronized."); })));
            scroll.Add(actions);
            var language = new PopupField<string>(StudioText.T("Language"), p.locales, Math.Max(0, p.locales.IndexOf(p.defaultLocale)));
            scroll.Add(language);
            var search = new TextField(StudioText.T("Filter keys"));
            scroll.Add(search);
            var entries = new VisualElement();
            scroll.Add(entries);
            void Rebuild()
            {
                entries.Clear();
                var filtered = p.translations.Where(t => t.locale == language.value && t.key.IndexOf(search.value ?? "", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                var list = new ListView { itemsSource = filtered, fixedItemHeight = 116, selectionType = SelectionType.None };
                list.style.height = 464;
                list.makeItem = () => { var row = new VisualElement(); row.AddToClassList("card"); row.Add(new Label { name = "key" }); row.Add(new TextField { name = "translation", multiline = true }); row.Add(new Toggle(StudioText.T("Draft / review required")) { name = "draft" }); return row; };
                list.bindItem = (row, index) =>
                {
                    var entry = filtered[index];
                    row.Q<Label>("key").text = entry.key;
                    var field = row.Q<TextField>("translation");
                    field.userData = entry;
                    field.SetValueWithoutNotify(entry.text);
                    var draft = row.Q<Toggle>("draft");
                    draft.userData = entry;
                    draft.SetValueWithoutNotify(entry.draft);
                };
                list.RegisterCallback<ChangeEvent<string>>(e => { if (e.target is TextField f && f.userData is TranslationEntry t) t.text = e.newValue; });
                list.RegisterCallback<ChangeEvent<bool>>(e => { if (e.target is Toggle f && f.userData is TranslationEntry t) t.draft = e.newValue; });
                entries.Add(list);
            }
            language.RegisterValueChangedCallback(_ => Rebuild());
            search.RegisterValueChangedCallback(_ => Rebuild());
            Rebuild();
            scroll.Add(Button("Apply translations", () => Mutate(() => service.Replace(p, service.Revision)), true));
            var assets = new Foldout { text = StudioText.T("Locale assets • fonts / portraits / audio"), value = false };
            scroll.Add(assets);
            foreach (var entry in p.localizedAssets)
            {
                var row = new VisualElement();
                row.AddToClassList("card");
                Text(row, "Key", entry.key, x => entry.key = x);
                Text(row, "Locale", entry.locale, x => entry.locale = x);
                var field = new UnityEditor.UIElements.ObjectField(StudioText.T("Asset")) { objectType = typeof(UnityEngine.Object), allowSceneObjects = false, value = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(entry.assetGuid)) };
                field.RegisterValueChangedCallback(e => entry.assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.newValue)));
                row.Add(field);
                assets.Add(row);
            }
            assets.Add(Button("+ Localized asset", () => { p.localizedAssets.Add(new LocalizedAssetEntry { key = "appearance.new", locale = language.value }); content.Clear(); Heading(page, p.id); DrawLocalization(p); }));
            assets.Add(Button("Apply asset mappings", () => Mutate(() => service.Replace(p, service.Revision))));
            var glossary = new Foldout { text = StudioText.T("Terminology / glossary"), value = false };
            scroll.Add(glossary);
            foreach (var term in p.glossary)
            {
                Text(glossary, "Source term", term.source, x => term.source = x);
                Text(glossary, "Target locale", term.locale, x => term.locale = x);
                Text(glossary, "Required translation", term.translation, x => term.translation = x);
            }
            glossary.Add(Button("+ Term", () => { p.glossary.Add(new GlossaryEntry { source = "New term", locale = language.value, translation = "Translation" }); content.Clear(); Heading(page, p.id); DrawLocalization(p); }));
            glossary.Add(Button("Apply glossary", () => Mutate(() => service.Replace(p, service.Revision))));
        }
        private void DrawValidation(ProjectDefinition p)
        {
            var actions = Row();
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            var report = StyledLabel(StudioText.Report(service.ValidateProject()), "report");
            scroll.Add(report);
            actions.Add(Button("Validate project", () => report.text = StudioText.Report(service.ValidateProject()), true));
            actions.Add(Button("Simulate 32 lives", () => Safe(() =>
            {
                var result = service.SimulatePlaythrough(42, 32, 10000);
                report.text = JsonUtility.ToJson(result, true);
                Notify("Simulation finished: " + result.completed + " / " + result.runs + " endings, " + result.elapsedMilliseconds + " ms");
            })));
            actions.Add(Button("Check localized assets", () =>
            {
                var result = new ValidationReport();
                foreach (var entry in p.localizedAssets)
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(entry.assetGuid)))
                        result.Add(IssueSeverity.Error, "asset.missing", entry.key, "GUID does not resolve: " + entry.locale);
                foreach (var m in p.modules)
                    if (!string.IsNullOrEmpty(m.scenePath) && !EditorBuildSettings.scenes.Any(s => s.enabled && s.path == m.scenePath))
                        result.Add(IssueSeverity.Warning, "module.build-scene", m.id, "Scene is not enabled in Build Settings.");
                report.text = StudioText.Report(result);
            }));
            actions.Add(Button("Export report", () => { var path = EditorUtility.SaveFilePanel("Export validation", "", "validation.txt", "txt"); if (path.Length > 0) File.WriteAllText(path, report.text); }));
            content.Add(actions);
            content.Add(scroll);
            var probe = new Label(StudioText.T("Layout probe"));
            probe.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(probe);
            var locale = new PopupField<string>(StudioText.T("Audit locale"), p.locales, 0);
            content.Add(locale);
            var font = new UnityEditor.UIElements.ObjectField(StudioText.T("Target font")) { objectType = typeof(Font), allowSceneObjects = false };
            content.Add(font);
            var width = new FloatField(StudioText.T("Text box width")) { value = 320 };
            var height = new FloatField(StudioText.T("Text box height")) { value = 80 };
            content.Add(width);
            content.Add(height);
            content.Add(Button("Measure text and glyphs", () => { report.text = LocalizationLayoutAudit.Inspect(p, locale.value, font.value as Font, probe, width.value, height.value, 16).Summary; }));
            content.Add(StyledLabel("Reachability: contradictory bounds are errors. Random simulation is evidence, not an exhaustive proof. UI clipping and glyphs must be checked at target resolution.", "notice"));
        }
        private void DrawCommands()
        {
            var connection = Row();
            connection.Add(Button(AuthoringPipeServer.IsRunning ? "Stop MCP connection" : "Enable local MCP", () => Safe(() => { if (AuthoringPipeServer.IsRunning) AuthoringPipeServer.Stop(); else AuthoringPipeServer.Start(asset); Render(); })));
            connection.Add(Button("Refresh project", Reload));
            content.Add(connection);
            if (AuthoringPipeServer.IsRunning)
                content.Add(StyledLabel("Current-user pipe: " + AuthoringPipeServer.PipeName + " · Project: " + AssetDatabase.GetAssetPath(asset), "notice"));
            content.Add(StyledLabel("Paste a structured command from your LLM client. Changes use the same authoring service and Unity Undo as this editor.", "subtitle"));
            var input = new TextField { multiline = true, value = "{\n  \"operation\": \"ValidateProject\"\n}" };
            input.AddToClassList("code-input");
            content.Add(input);
            var output = new TextField { multiline = true, isReadOnly = true };
            output.style.flexGrow = 1;
            content.Add(output);
            content.Add(Button("Execute command", () =>
            {
                Safe(() =>
                {
                    if (asset.Read().revision != service.Revision)
                        throw new InvalidOperationException("Project changed externally. Refresh before sending a command.");
                    var response = new AuthoringCommandGateway(service).ExecuteJson(input.value);
                    output.value = response;
                    var parsed = JsonUtility.FromJson<CommandResponse>(response);
                    if (parsed.success)
                    {
                        Undo.RecordObject(asset, "RaiseArc authoring command");
                        asset.Write(service.Snapshot());
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                        preview = null;
                        Notify("Command completed • revision " + service.Revision);
                    }
                    else
                        Notify(parsed.error);
                });
            }, true));
        }
        private void DrawPreview(ProjectDefinition p)
        {
            if (preview == null)
            {
                try
                {
                    preview = GameFactory.Create(p, asset.CreateExtensions(p));
                    previewDuration = p.durationDays; previewRevision = p.revision;
                }
                catch (Exception e) { content.Add(new HelpBox(StudioText.T(e.Message), HelpBoxMessageType.Error)); return; }
            }
            var actions = Row();
            actions.Add(Button("Restart", () => { preview = null; Render(); }));
            actions.Add(Button("Save preview", () => Safe(() => { new SaveStore(Path.Combine(Application.persistentDataPath, "PrincessStudioPreview", p.id), p).Save("preview", preview); Notify("Preview saved."); })));
            actions.Add(Button("Load preview", () => Safe(() => { var recovered = new SaveStore(Path.Combine(Application.persistentDataPath, "PrincessStudioPreview", p.id), p).Load("preview", preview); Notify(recovered ? "Recovered backup save." : "Preview loaded."); Render(); })));
            content.Add(actions);
            content.Add(new HelpBox(StudioText.T("Test play checks saved rules inside Studio. To play the illustrated game, use Create basic game screen, then Unity Play."), HelpBoxMessageType.Info));
            content.Add(Button("Create basic game screen", () => StudioIntegrations.CreateBasicGameScreen?.Invoke(asset)));
            if (asset.Revision != previewRevision)
            {
                content.Add(new HelpBox(StudioText.T("The saved project changed. Reload saved settings and start a new test session."), HelpBoxMessageType.Warning));
                content.Add(Button("Reload saved settings", Reload, true));
            }
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            content.Add(scroll);
            var view = new VisualElement();
            view.AddToClassList("preview");
            scroll.Add(view);
            var state = preview.State;
            view.Add(StyledLabel(StudioText.T("Elapsed days / game duration") + $": {state.Day} / {previewDuration} · " + StudioText.T("Saved revision") + ": " + previewRevision, "card-title"));
            view.Add(StyledLabel(state.EndingId.Length > 0 ? StudioText.T("Game ended") : state.PendingEventId.Length > 0 ? StudioText.T("Waiting for event dialogue or a choice; activities are paused.") : StudioText.T("Choose an activity to advance time. Endings are checked at the game duration, after pending events finish."), "subtitle"));
            if (screenSkin != null)
            {
                var stage = new PrincessStudio.Unity.PresentationStage(p, screenSkin);
                view.Add(stage.Element); stage.Render(state, p.defaultLocale);
                view.RegisterCallback<DetachFromPanelEvent>(_ => stage.Dispose());
            }
            view.Add(StyledLabel(PreviewText(p, p.character.nameKey), "preview-heading"));
            view.Add(StyledLabel("Age " + state.Age + "  /  Year " + (state.Day / (p.daysPerMonth * p.monthsPerYear) + 1) + " · Month " + (state.Day / p.daysPerMonth % p.monthsPerYear + 1) + " · Day " + (state.Day % p.daysPerMonth + 1) + "  /  " + state.Money + " gold", "subtitle"));
            var growth = p.growth.Find(g => g.id == preview.CurrentGrowthId());
            if (growth != null)
            {
                var entry = p.localizedAssets.Find(a => a.key == growth.appearanceKey && a.locale == p.defaultLocale) ?? p.localizedAssets.Find(a => a.key == growth.appearanceKey && a.locale == p.fallbackLocale);
                if (entry != null)
                {
                    var picture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(entry.assetGuid));
                    if (picture != null)
                    {
                        var image = new Image { image = picture, scaleMode = ScaleMode.ScaleToFit };
                        image.style.height = 180;
                        view.Add(image);
                    }
                }
            }
            foreach (var stat in p.stats)
            {
                var bar = new ProgressBar { title = PreviewText(p, stat.nameKey) + "  " + state.Stats[stat.id], lowValue = stat.minimum, highValue = stat.maximum, value = state.Stats[stat.id] };
                bar.style.marginBottom = 8;
                view.Add(bar);
            }
            DrawRuleState(view, p, state);
            if (state.EndingId.Length > 0)
            {
                var ending = p.endings.Find(e => e.id == state.EndingId);
                view.Add(StyledLabel(PreviewText(p, ending.nameKey), "preview-heading"));
                view.Add(StyledLabel(PreviewText(p, ending.descriptionKey), "preview-text"));
                return;
            }
            if (state.PendingEventId.Length > 0)
            {
                var authoredEvent = p.events.Find(e => e.id == state.PendingEventId);
                if (authoredEvent == null)
                {
                    view.Add(StyledLabel("Pending event is missing from the current content.", "notice"));
                    return;
                }
                var ev = RaiseArcFlowReuse.Expand(p, authoredEvent);
                var step = ev.presentation.Find(x => x.id == state.PresentationStepId);
                if (step != null)
                {
                    if (step.kind != PresentationStepKind.Image)
                    {
                        var speaker = p.actors.Find(x => x.id == step.speakerActorId);
                        if (speaker != null) view.Add(StyledLabel(PreviewText(p, speaker.nameKey), "card-title"));
                        view.Add(StyledLabel(PreviewText(p, step.nameKey), "preview-text"));
                    }
                    if (step.kind != PresentationStepKind.Choice)
                        view.Add(Button("Continue", () => Safe(() => { preview.AdvancePresentation(step.id); Render(); })));
                }
                else
                {
                    view.Add(StyledLabel(PreviewText(p, ev.nameKey), "card-title"));
                    view.Add(StyledLabel(PreviewText(p, ev.descriptionKey), "preview-text"));
                }
                foreach (var id in preview.AvailableChoices())
                {
                    var choice = (step == null ? ev.choices : step.choices).Find(c => c.id == id);
                    if (choice == null)
                    {
                        view.Add(StyledLabel("Pending choice is missing from the current content: " + id, "notice"));
                        continue;
                    }
                    view.Add(Button(PreviewText(p, choice.nameKey), () => Safe(() => { preview.Choose(id); Render(); })));
                }
                return;
            }
            view.Add(StyledLabel("Plan the next chapter", "card-title"));
            foreach (var activity in p.activities)
            {
                var button = Button(PreviewText(p, activity.nameKey) + "  ·  " + activity.days + " days  ·  " + activity.cost + " gold", () => Safe(() => { preview.PerformActivity(activity.id); Render(); }));
                button.SetEnabled(preview.CanPerform(activity.id) && string.IsNullOrEmpty(activity.moduleId));
                view.Add(button);
            }
            if (p.activities.Any(a => !string.IsNullOrEmpty(a.moduleId)))
                view.Add(StyledLabel("Scene activities run in Play Mode through ModuleRunner. This preview exercises core activities and choices.", "notice"));
        }
    }
}
