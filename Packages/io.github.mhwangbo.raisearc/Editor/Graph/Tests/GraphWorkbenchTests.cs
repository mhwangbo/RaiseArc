using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PrincessStudio.Editor.Graph.Tests
{
    public sealed class GraphWorkbenchTests
    {
        private const string AssetPath = "Assets/GraphWorkbenchTestProject.asset";
        private GameProjectAsset asset;
        private GraphWorkbenchWindow window;
        private string layoutPath;
        private readonly UnityProjectCodec codec = new UnityProjectCodec();
        [SetUp] public void Setup()
        {
            asset = ScriptableObject.CreateInstance<GameProjectAsset>();
            asset.Write(codec.FromJson(File.ReadAllText("Assets/PrincessStudio/Samples/Data/PresentationDemo.json")));
            AssetDatabase.CreateAsset(asset, AssetPath);
            layoutPath = "Assets/PrincessStudioWorkbench/Layouts/" + AssetDatabase.AssetPathToGUID(AssetPath) + ".asset";
            GraphWorkbenchWindow.Open(asset); window = EditorWindow.GetWindow<GraphWorkbenchWindow>(); window.CreateGUI();
        }
        [TearDown] public void Cleanup()
        {
            if (window != null) window.Close();
            AssetDatabase.DeleteAsset(AssetPath); if (!string.IsNullOrEmpty(layoutPath)) AssetDatabase.DeleteAsset(layoutPath);
        }
        private object Field(string name) => typeof(GraphWorkbenchWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
        private void Call(string name, params object[] args) => typeof(GraphWorkbenchWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args);
        [UnityTest] public IEnumerator TerminalChoiceWireCanDisconnectUndoInsertSaveAndResume()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "invitation", true);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            ((FlowCanvas)Field("canvas")).FitAll();
            for (var i = 0; i < 5; i++) yield return null;
            var pin = window.rootVisualElement.Q("output-invitation:terminal-accept");
            Assert.That(pin.enabledSelf, Is.True);
            var revision = asset.Revision;
            Drag(pin, EmptyCanvas()); DismissNodeSearch();
            var draft = ((ProjectDefinition)Field("project")).events.Find(e => e.id == "invitation");
            Assert.That(draft.choices, Is.Empty);
            Assert.That(draft.presentation.Single(s => s.choices.Any(c => c.id == "accept")).choices.Single(c => c.id == "accept").nextStepId, Is.EqualTo("$disconnected"));
            Call("ApplyChanges"); Assert.That(asset.Revision, Is.EqualTo(revision));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            yield return WaitForUI(() => ((ProjectDefinition)Field("project")).events.Find(e => e.id == "invitation").choices.Count == 2);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            ((FlowCanvas)Field("canvas")).FitAll();
            for (var i = 0; i < 5; i++) yield return null;
            var endInput = window.rootVisualElement.Q("input-$end");
            Assert.That(endInput.enabledSelf, Is.True);
            Drag(endInput, EmptyCanvas()); DismissNodeSearch();
            draft = ((ProjectDefinition)Field("project")).events.Find(e => e.id == "invitation");
            Assert.That(draft.presentation.SelectMany(s => s.choices).Count(c => c.nextStepId == "$disconnected"), Is.EqualTo(1));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            yield return WaitForUI(() => ((ProjectDefinition)Field("project")).events.Find(e => e.id == "invitation").choices.Count == 2);
            Call("Stage", new GraphEdit { operation = "InsertNodeBetween", eventId = "invitation", targetId = "invitation:terminal", portId = "accept", nextId = EventSequence.End,
                node = new PresentationStep { id = "after.accept", nameKey = "dialogue.hello", kind = PresentationStepKind.Dialogue } });
            Call("ApplyChanges"); Call("Reload");
            var saved = asset.Read(); var flow = saved.events.Find(e => e.id == "invitation");
            var choiceStep = flow.presentation.Single(s => s.choices.Any(c => c.id == "accept"));
            Assert.That(flow.choices, Is.Empty);
            Assert.That(choiceStep.choices.Single(c => c.id == "decline").nextStepId, Is.EqualTo(EventSequence.End));
            Assert.That(choiceStep.choices.Single(c => c.id == "accept").nextStepId, Is.EqualTo("after.accept"));
            var session = new GameSession(saved); session.PerformActivity("study");
            session.AdvancePresentation("invitation.line1"); session.AdvancePresentation("invitation.line2");
            session.Choose("accept");
            Assert.That(session.State.PresentationStepId, Is.EqualTo("after.accept"));
            var restored = new GameSession(saved); restored.Restore(session.Capture());
            Assert.That(restored.State.PresentationStepId, Is.EqualTo("after.accept"));
            restored.AdvancePresentation("after.accept");
            Assert.That(restored.State.PendingEventId, Is.Empty);
        }
        [UnityTest] public IEnumerator NodeDetailsKeepChoiceOwnershipAndPersistIndependentFoldsWithoutContentEdits()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "invitation", true);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            var before = codec.ToJson((ProjectDefinition)Field("project")); var revision = asset.Revision;
            var accept = window.rootVisualElement.Q("choice-detail-accept");
            var decline = window.rootVisualElement.Q("choice-detail-decline");
            string Text(VisualElement parent) => string.Join(" ", parent.Query<Label>().ToList().Select(l => l.text));
            Assert.That(Text(accept), Does.Contain("+5"));
            Assert.That(Text(decline), Does.Not.Contain("+5"));
            Assert.That(Text(decline), Does.Contain(StudioText.T("No direct effects")));
            var detail = accept.Q<Foldout>("detail-accept/details"); detail.value = true;
            var conditions = accept.Q<Foldout>("detail-accept/conditions"); var effects = accept.Q<Foldout>("detail-accept/effects");
            conditions.value = true; effects.value = false;
            var effect = effects.Query<Foldout>().ToList().First(f => f != effects); effect.value = true;
            effects.value = true; Assert.That(effect.value, Is.True); effects.value = false;
            Assert.That(effects.text, Does.Contain("+5"));
            Assert.That(conditions.value, Is.True);
            var canvas = (FlowCanvas)Field("canvas"); var zoom = canvas.Zoom; var offset = canvas.ScrollOffset;
            var collapse = window.rootVisualElement.Q<Button>("collapse-invitation:terminal");
            using (var click = NavigationSubmitEvent.GetPooled()) { click.target = collapse; collapse.SendEvent(click); }
            yield return null;
            Assert.That(((GraphLayoutAsset)Field("layout")).Get("invitation", "invitation:terminal", 0).collapsed, Is.True);
            Assert.That(accept.Q("output-invitation:terminal-accept").resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None));
            Assert.That(canvas.Zoom, Is.EqualTo(zoom)); Assert.That(canvas.ScrollOffset, Is.EqualTo(offset));
            Assert.That(codec.ToJson((ProjectDefinition)Field("project")), Is.EqualTo(before)); Assert.That(asset.Revision, Is.EqualTo(revision));
            window.Close(); GraphWorkbenchWindow.Open(asset); window = EditorWindow.GetWindow<GraphWorkbenchWindow>(); window.CreateGUI();
            Call("Navigate", "invitation", true);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            Assert.That(window.rootVisualElement.Q<Foldout>("detail-accept/conditions").value, Is.True);
            Assert.That(window.rootVisualElement.Q<Foldout>("detail-accept/effects").value, Is.False);
            Assert.That(window.rootVisualElement.Q<Foldout>(effect.name).value, Is.True);
            Assert.That(asset.Revision, Is.EqualTo(revision));
        }
        [UnityTest] public IEnumerator DialogueDetailsPreserveLineOwnershipAndAutoLayoutUsesCardHeight()
        {
            var data = asset.Read(); var longText = new string('A', 600);
            data.translations.Find(t => t.key == "dialogue.hello" && t.locale == "en").text = longText; asset.Write(data); Call("Reload");
            Call("Navigate", "invitation.line1", true);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            var first = window.rootVisualElement.Q<Foldout>("detail-invitation.line1/line/invitation.line1");
            var second = window.rootVisualElement.Q<Foldout>("detail-invitation.line2/line/invitation.line2");
            Assert.That(window.rootVisualElement.Q("node-invitation.line1").Q<Label>(className: "node-title").text.Length, Is.LessThanOrEqualTo(101));
            Assert.That(first.Query<Label>().ToList().Any(l => l.text.Contains(longText)), Is.True);
            Assert.That(first.text, Does.Contain("Mira")); Assert.That(second.text, Does.Contain("Luna"));
            first.value = true; var actors = first.Q<Foldout>("detail-invitation.line1/actors"); actors.value = true;
            var actor = actors.Query<Foldout>().ToList().First(f => f != actors); actor.value = true;
            Assert.That(second.value, Is.False);
            first.Q<Foldout>("detail-invitation.line1/images").value = true;
            Assert.That(second.Q<Foldout>("detail-invitation.line2/images").value, Is.False);
            for (var i = 0; i < 5; i++) yield return null;
            Call("AutoLayout");
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            for (var i = 0; i < 5; i++) yield return null;
            var nodes = window.rootVisualElement.Query<VisualElement>(className: "flow-node").ToList();
            for (var i = 0; i < nodes.Count; i++) for (var j = i + 1; j < nodes.Count; j++)
                Assert.That(nodes[i].layout.Overlaps(nodes[j].layout), Is.False, nodes[i].name + " / " + nodes[j].name);
        }
        [Test] public void NodeDetailsEditExactChoiceAndTraceHighlightsOnlyTheChosenBranch()
        {
            Call("Navigate", "invitation", true);
            Call("EditNodeDetail", "accept", "Effects");
            Assert.That((string)Field("selectedId"), Is.EqualTo("accept"));
            Assert.That(window.rootVisualElement.Q<Foldout>("inspector-section-Effects").value, Is.True);
            var data = asset.Read(); var api = new AuthoringService(data, codec);
            var trace = api.SimulateFromNode("invitation", "invitation.line1", choices: new System.Collections.Generic.List<string> { "accept" });
            Assert.That(trace.error, Is.Empty);
            var frame = trace.frames.Single(f => f.choiceId == "accept");
            Assert.That(frame.before.pendingEventId, Is.EqualTo("invitation"));
            var playbackType = typeof(GraphWorkbenchWindow).Assembly.GetType("RaiseArc.Editor.RaiseArcTracePlayback");
            var playback = System.Activator.CreateInstance(playbackType, data, "invitation", "en", trace, (FlowCanvas)Field("canvas"), (System.Action<string>)(_ => { }), trace.frames.IndexOf(frame));
            Assert.That(playbackType.GetMethod("CurrentSource", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(playback, null), Is.EqualTo("invitation:terminal"));
            Assert.That(window.rootVisualElement.Q("choice-detail-accept").ClassListContains("choice-executing"), Is.True);
            Assert.That(window.rootVisualElement.Q("choice-detail-decline").ClassListContains("choice-executing"), Is.False);
            playbackType.GetMethod("Advance").Invoke(playback, null);
            Assert.That(window.rootVisualElement.Q("choice-detail-accept").ClassListContains("choice-executing"), Is.False);
        }
        [Test] public void DialogueCardsExposeActualLinesAndLocalizationReviewIsExplicit()
        {
            var project = asset.Read(); var graph = GraphProjection.Build(project, "invitation", "en");
            var dialogue = graph.nodes.First(n => n.dialogue.Count > 0);
            var index = new ContentIndex(project, "en");
            foreach (var id in dialogue.stepIds)
                Assert.That(dialogue.dialogue, Does.Contain(index.Text(((PresentationStep)index.Find(id)).nameKey, "en")));
            var key = ((PresentationStep)index.Find(dialogue.stepIds[0])).nameKey;
            var translation = project.translations.First(t => t.key == key && t.locale == "en");
            translation.draft = true;
            var api = new AuthoringService(project, codec);
            Assert.That(api.GetLocalizationStatus().Any(s => s.key == key && s.locale == "en" && s.draft && s.needsReview), Is.True);
            translation.draft = false; translation.reviewed = true;
            var restored = codec.FromJson(codec.ToJson(project));
            Assert.That(restored.translations.First(t => t.key == key && t.locale == "en").reviewed, Is.True);
            Assert.That(new AuthoringService(restored, codec).GetLocalizationStatus().Any(s => s.key == key && s.locale == "en" && !s.needsReview && !s.missing), Is.True);
        }
        [Test] public void GraphPlaybackStepsWithoutChangingContentAndShowsReadableChoices()
        {
            var before = codec.ToJson(asset.Read());
            Call("Navigate", "invitation.line1", true); Call("PlaySelection");
            var trace = (ExecutionTrace)Field("trace"); Assert.That(trace.error, Is.Empty);
            Assert.That(trace.frames.Count, Is.GreaterThan(0));
            var start = trace.frames[0].nodeId;
            Call("Navigate", "invitation.line2", true); Call("RunTrace");
            trace = (ExecutionTrace)Field("trace");
            Assert.That(trace.frames[0].nodeId, Is.EqualTo(start), "Inspecting another card must not change the replay's starting point.");
            var player = Field("tracePlayback");
            Assert.That(window.rootVisualElement.Q("trace-playback"), Is.Not.Null);
            Assert.That(window.rootVisualElement.Q("trace-speed"), Is.Not.Null);
            for (var i = 0; i < trace.frames.Count; i++) player.GetType().GetMethod("Advance").Invoke(player, null);
            var choices = window.rootVisualElement.Q("trace-playback").Query<Button>().ToList().Where(b => b.name != null && b.name.StartsWith("trace-choice-")).ToList();
            Assert.That(choices.Count, Is.GreaterThan(0));
            Assert.That(choices.Any(b => b.text.Contains("Accept") || b.text.Contains("Stay")), Is.True);
            Assert.That(codec.ToJson(asset.Read()), Is.EqualTo(before));
        }
        [Test] public void RelationshipViewUsesExistingReferencesWithoutEditableWires()
        {
            Call("Navigate", "vitality", true);
            typeof(GraphWorkbenchWindow).GetField("mode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, "Relationships");
            Call("RefreshGraph");
            var pins = window.rootVisualElement.Query<Button>(className: "connection-pin").ToList();
            Assert.That(pins.Count, Is.GreaterThan(0));
            Assert.That(pins.All(pin => !pin.enabledSelf), Is.True);
            Assert.That(window.rootVisualElement.Q("node-vitality"), Is.Not.Null);
            Assert.That(window.rootVisualElement.Query<Label>(className: "node-title").ToList().All(label => !string.IsNullOrWhiteSpace(label.text)), Is.True);
        }
        [Test] public void SharedFlowReplayHighlightsItsCallCard()
        {
            Call("Navigate", "invitation", true);
            Call("AddSharedFlow", "gallery-event", (object)null);
            var project = (ProjectDefinition)Field("project");
            var call = project.events.Find(e => e.id == "invitation").presentation.Last();
            Call("StageMany", (object)new[] {
                new GraphEdit { operation = "ConnectNodes", eventId = "invitation", targetId = "invitation", portId = "next", nextId = call.id },
                new GraphEdit { operation = "ConnectNodes", eventId = "invitation", targetId = call.id, portId = "next", nextId = EventSequence.End }
            });
            var preset = new GameSession((ProjectDefinition)Field("project")).Capture();
            preset.day = 2;
            typeof(GraphWorkbenchWindow).GetField("preset", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, preset);
            Call("Navigate", call.id, true); Call("PlaySelection");
            var trace = (ExecutionTrace)Field("trace");
            Assert.That(trace.error, Is.Empty);
            var player = Field("tracePlayback");
            for (var i = 0; i < trace.frames.Count; i++)
            {
                if (trace.frames[i].nodeId.StartsWith(call.id + ".call."))
                    Assert.That(window.rootVisualElement.Q("node-" + call.id).ClassListContains("node-executing"), Is.True);
                player.GetType().GetMethod("Advance").Invoke(player, null);
            }
            Assert.That(trace.frames.Any(f => f.nodeId.StartsWith(call.id + ".call.")), Is.True);
        }
        [Test] public void SearchCopiesContentWithIndependentTextAndSharesOriginalOnRequest()
        {
            Call("Navigate", "invitation", true);
            var original = ((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event").presentation.Find(s => s.id == "gallery.garden");
            var key = original.nameKey;
            var text = ((ProjectDefinition)Field("project")).translations.First(t => t.key == key && t.locale == "en").text;
            Call("ShowNodeSearch", (object)null);
            Assert.That(window.rootVisualElement.Q("node-search"), Is.Not.Null);
            var query = window.rootVisualElement.Q<TextField>("node-search-query"); query.value = "gallery.garden";
            Assert.That(window.rootVisualElement.Q("node-search-results").Query<Button>().ToList().Count, Is.EqualTo(2));
            Call("CopyExistingNode", "gallery-event", "gallery.garden", new Vector2(450, 350));
            var project = (ProjectDefinition)Field("project");
            var copied = project.events.Find(e => e.id == "invitation").presentation.Last();
            Assert.That(copied.nameKey, Is.Not.EqualTo(key));
            Assert.That(project.translations.Single(t => t.key == copied.nameKey && t.locale == "en").text, Is.EqualTo(text));
            Assert.That(copied.sharedStepId, Is.Empty);
            Call("AddSharedContent", "gallery-event", "gallery.garden", (object)null);
            project = (ProjectDefinition)Field("project");
            var shared = project.events.Find(e => e.id == "invitation").presentation.Last();
            Assert.That(shared.sharedStepId, Is.EqualTo("gallery.garden"));
            Assert.That(new ContentIndex(project).References("gallery.garden").Any(r => r.sourceId == shared.id), Is.True);
            Assert.That(copied.nextStepId, Is.EqualTo("$disconnected"));
            Assert.That(shared.nextStepId, Is.EqualTo("$disconnected"));
            Call("StageMany", (object)new[] {
                new GraphEdit { operation = "ConnectNodes", eventId = "invitation", targetId = copied.id, portId = "next", nextId = EventSequence.End },
                new GraphEdit { operation = "ConnectNodes", eventId = "invitation", targetId = shared.id, portId = "next", nextId = EventSequence.End }
            });
            Call("ApplyChanges"); Call("Reload");
            Assert.That(asset.Read().events.Find(e => e.id == "invitation").presentation.Exists(s => s.id == shared.id && s.sharedStepId == "gallery.garden"), Is.True);
        }
        [UnityTest] public IEnumerator BlankCanvasRightClickOpensSearchAndEscapeCancels()
        {
            window.position = new Rect(0, 0, 1280, 800);
            for (var i = 0; i < 8; i++) yield return null;
            var canvas = window.rootVisualElement.Q<VisualElement>("flow-canvas");
            var point = canvas.worldBound.position + new Vector2(5, 5);
            using (var click = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 1, mousePosition = point }))
            { click.target = canvas; canvas.SendEvent(click); }
            var search = window.rootVisualElement.Q<TextField>("node-search-query");
            Assert.That(search, Is.Not.Null);
            var before = asset.Revision;
            using (var escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            { escape.target = search; search.SendEvent(escape); }
            Assert.That(window.rootVisualElement.Q("node-search"), Is.Null);
            Assert.That(asset.Revision, Is.EqualTo(before));
            Assert.That(((AuthoringChangeSet)Field("changes")).edits, Is.Empty);
        }
        [Test] public void SaveIncludesCurrentFieldsAndKeepsEditsAcrossSelection()
        {
            var before = asset.Revision;
            Call("Navigate", "gallery.a1", true);
            var draft = (ProjectDefinition)Field("inspectorProject");
            draft.events.Find(e => e.id == "gallery-event").presentation.Find(s => s.id == "gallery.a1").nameKey = "qa.first";
            Call("MarkInspectorDirty");
            Assert.That(window.rootVisualElement.Q<Button>("save-content").enabledSelf, Is.True);
            Call("Navigate", "gallery.garden", true);
            Assert.That(asset.Revision, Is.EqualTo(before), "Selection must not save content.");
            draft = (ProjectDefinition)Field("inspectorProject");
            draft.events.Find(e => e.id == "gallery-event").presentation.Find(s => s.id == "gallery.garden").nameKey = "qa.second";
            Call("MarkInspectorDirty");
            Call("ApplyChanges");
            Assert.That(asset.Revision, Is.EqualTo(before + 1), "Save commits the draft and current fields together.");
            Call("Reload");
            var saved = asset.Read().events.Find(e => e.id == "gallery-event");
            Assert.That(saved.presentation.Find(s => s.id == "gallery.a1").nameKey, Is.EqualTo("qa.first"));
            Assert.That(saved.presentation.Find(s => s.id == "gallery.garden").nameKey, Is.EqualTo("qa.second"));
            Assert.That(window.rootVisualElement.Q<Button>("save-content").enabledSelf, Is.False);
        }
        [Test] public void SaveWithValidationErrorsRetainsEditsWithoutWritingTheAsset()
        {
            var before = codec.ToJson(asset.Read());
            Call("Navigate", "gallery.garden", true);
            var draft = (ProjectDefinition)Field("inspectorProject");
            draft.events.Find(e => e.id == "gallery-event").presentation.Find(s => s.id == "gallery.garden").nextStepId = "missing-qa-node";
            Call("MarkInspectorDirty"); Call("ApplyChanges");
            Assert.That(codec.ToJson(asset.Read()), Is.EqualTo(before));
            Assert.That(Field("panel"), Is.EqualTo("Problems"));
            Assert.That(((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event").presentation.Find(s => s.id == "gallery.garden").nextStepId, Is.EqualTo("missing-qa-node"));
            Assert.That(window.rootVisualElement.Q<Button>("save-content").enabledSelf, Is.True);
        }
        [Test] public void ShellProjectsContentAndLayoutDoesNotChangeGameRevision()
        {
            var before = codec.ToJson(asset.Read());
            Assert.That(window.rootVisualElement.Q<ListView>("content-explorer"), Is.Not.Null);
            Assert.That(window.rootVisualElement.Q("flow-canvas"), Is.Not.Null);
            Call("AutoLayout");
            Assert.That(codec.ToJson(asset.Read()), Is.EqualTo(before));
            Assert.That(((GraphLayoutAsset)Field("layout")).nodes.Count, Is.GreaterThan(0));
            LogAssert.NoUnexpectedReceived();
        }
        [Test] public void ClipboardDuplicatesDialogueBlockAsOneProposalWithFreshIds()
        {
            var previous = EditorGUIUtility.systemCopyBuffer;
            try
            {
                Call("Navigate", "gallery.a1", true);
                var original = ((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event");
                var count = original.presentation.Count; var ids = original.presentation.Select(s => s.id).ToArray();
                Call("CopyNodes"); Call("PasteNodes");
                var changes = (AuthoringChangeSet)Field("changes"); Assert.That(changes.edits.Count(edit => edit.operation == "CreateNode"), Is.EqualTo(3));
                var copy = ((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event").presentation.Skip(count).ToList();
                Assert.That(copy.Count, Is.EqualTo(3)); Assert.That(copy.All(s => !ids.Contains(s.id)), Is.True);
                Assert.That(copy[0].nextStepId, Is.EqualTo(copy[1].id)); Assert.That(copy[1].nextStepId, Is.EqualTo(copy[2].id)); Assert.That(copy[2].nextStepId, Is.EqualTo("$disconnected"));
                Call("ApplyChanges"); Assert.That(asset.Read().events.Find(e => e.id == "gallery-event").presentation.Count, Is.EqualTo(count));
                Call("Stage", new GraphEdit { operation = "ConnectNodes", eventId = "gallery-event", targetId = copy[0].id, portId = "next", nextId = EventSequence.End });
                Call("ApplyChanges"); Assert.That(asset.Read().events.Find(e => e.id == "gallery-event").presentation.Count, Is.EqualTo(count + 3));
            }
            finally { EditorGUIUtility.systemCopyBuffer = previous; }
        }
        [Test] public void ChangingEditorLanguagePreservesPendingGraphProposal()
        {
            var previous = StudioText.Language;
            try
            {
                Call("Stage", new GraphEdit { operation = "CreateNode", eventId = "invitation", node = new PresentationStep { id = "language-draft", nameKey = "sequence.event", kind = PresentationStepKind.Image, nextStepId = EventSequence.End } });
                StudioText.Language = previous == "ko" ? "en" : "ko"; window.CreateGUI();
                Assert.That(((AuthoringChangeSet)Field("changes")).edits.Count, Is.EqualTo(1));
                Assert.That(((ProjectDefinition)Field("project")).events.Find(e => e.id == "invitation").presentation.Exists(s => s.id == "language-draft"), Is.True);
                Assert.That(asset.Read().events.Find(e => e.id == "invitation").presentation.Exists(s => s.id == "language-draft"), Is.False);
            }
            finally { StudioText.Language = previous; }
        }
        [Test] public void GraphDraftAndAssetUndoShareAuthoringApi()
        {
            var before = asset.Revision;
            Call("AddNode", PresentationStepKind.Image);
            Assert.That(asset.Revision, Is.EqualTo(before));
            Assert.That(((AuthoringChangeSet)Field("changes")).edits.Count, Is.EqualTo(1));
            Call("Stage", new GraphEdit { operation = "ConnectNodes", eventId = (string)Field("eventId"), targetId = (string)Field("selectedId"), portId = "next", nextId = EventSequence.End });
            Call("ApplyChanges"); Undo.FlushUndoRecordObjects();
            Assert.That(asset.Revision, Is.EqualTo(before + 1));
            var api = new AuthoringService(asset.Read(), codec);
            Assert.That(api.GetGraphProjection("invitation").nodes.Any(n => n.kind == "Presentation"), Is.True);
            Undo.PerformUndo(); Assert.That(asset.Revision, Is.EqualTo(before));
            Undo.PerformRedo(); Assert.That(asset.Revision, Is.EqualTo(before + 1));
        }

        [Test] public void ExplorerSelectionSeparatesPropertiesFromEventFlows()
        {
            var explorer = window.rootVisualElement.Q<ListView>("content-explorer");
            int Row(string id) => explorer.itemsSource.Cast<ContentEntry>().ToList().FindIndex(x => x.id == id);
            var stat = asset.Read().stats[0].id;
            explorer.SetSelection(Row(stat));
            Assert.That(Field("selectedId"), Is.EqualTo(stat));
            Assert.That(Field("eventId"), Is.EqualTo(""));
            Assert.That(window.rootVisualElement.Q("flow-canvas"), Is.Null);
            Assert.That(window.rootVisualElement.Q("properties-navigation"), Is.Not.Null);
            Call("Reload");
            Assert.That(Field("selectedId"), Is.EqualTo(stat), "Reload must not replace a property selection with the first event.");
            Assert.That(window.rootVisualElement.Q("flow-canvas"), Is.Null);
            explorer.SetSelection(Row("gallery.a1"));
            Assert.That(Field("eventId"), Is.EqualTo("gallery-event"));
            Assert.That(window.rootVisualElement.Q("flow-canvas"), Is.Not.Null);
            Assert.That(window.rootVisualElement.Q("properties-navigation"), Is.Null);
            Assert.That(window.rootVisualElement.Q<Label>("graph-title").text, Does.Contain(new ContentIndex(asset.Read(), (string)Field("locale")).Text("sequence.event", (string)Field("locale"))));
        }

        [Test] public void EditingPreservesTheSelectedDiagnosticTab()
        {
            var references = window.rootVisualElement.Q<ToolbarToggle>("tab-References");
            references.value = true;
            Assert.That(Field("panel"), Is.EqualTo("References"));
            Assert.That(references.ClassListContains("tab-selected"), Is.True);
            Assert.That(window.rootVisualElement.Q<ToolbarToggle>("tab-Problems").value, Is.False);
            Call("AddNode", PresentationStepKind.Image);
            Assert.That(window.rootVisualElement.Q<ToolbarToggle>("tab-Changes").value, Is.False);
            Assert.That(references.value, Is.True);
            Assert.That(window.rootVisualElement.Query<ToolbarToggle>().ToList().Count(t => t.ClassListContains("tab-selected")), Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator SaveShortcutIncludesTextWhileTheFieldIsFocused()
        {
            Call("Navigate", "gallery.a1", true);
            yield return null;
            foreach (var modifier in new[] { EventModifiers.Control, EventModifiers.Command })
            {
                var field = window.rootVisualElement.Query<TextField>().ToList().First(f => f.label == StudioText.T("en Text"));
                var text = field.value + " [Save QA " + modifier + "]";
                var before = asset.Revision;
                field.Focus(); field.value = text;
                Assert.That(asset.Revision, Is.EqualTo(before));
                using (var save = KeyDownEvent.GetPooled('s', KeyCode.S, modifier)) { save.target = field; field.SendEvent(save); }
                Assert.That(asset.Revision, Is.EqualTo(before + 1));
                Assert.That(asset.Read().translations.Any(t => t.locale == "en" && t.text == text), Is.True);
                Assert.That(window.rootVisualElement.Q<Button>("save-content").enabledSelf, Is.False);
                yield return null;
            }
        }

        [UnityTest] public IEnumerator ZoomAndFitPreserveContentAndLayout()
        {
            window.position = new Rect(0, 0, 1280, 800);
            for (var i = 0; i < 5; i++) yield return null;
            var view = (FlowCanvas)Field("canvas");
            var layout = (GraphLayoutAsset)Field("layout");
            var contentBefore = codec.ToJson(asset.Read()); var layoutBefore = JsonUtility.ToJson(layout);
            var node = view.Element.Q("node-invitation");
            view.SetZoom(1);
            for (var i = 0; i < 3; i++) yield return null;
            var width = node.worldBound.width;
            Assert.That(width, Is.GreaterThan(0), "The UI must have a real layout for this test.");
            view.SetZoom(.5f);
            yield return WaitForUI(() => Mathf.Abs(node.worldBound.width - width / 2) < 1);
            Assert.That(node.worldBound.width, Is.EqualTo(width / 2).Within(1));
            var viewport = view.Element;
            view.ScrollOffset = new Vector2(250, 180);
            var anchor = node.parent.WorldToLocal(viewport.worldBound.center);
            using (var wheel = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, delta = new Vector2(0, -1), mousePosition = viewport.worldBound.center }))
            {
                wheel.target = viewport; viewport.SendEvent(wheel);
            }
            yield return WaitForUI(() => view.Zoom > .5f && Mathf.Abs(node.worldBound.width - width * view.Zoom) < 1 && Vector2.Distance(node.parent.WorldToLocal(viewport.worldBound.center), anchor) < 2);
            Assert.That(view.Zoom, Is.GreaterThan(.5f), "The wheel must zoom rather than only scrolling.");
            Assert.That(Vector2.Distance(node.parent.WorldToLocal(viewport.worldBound.center), anchor), Is.LessThan(2));
            view.SetZoom(1);
            yield return WaitForUI(() => Vector2.Distance(node.parent.WorldToLocal(viewport.worldBound.center), anchor) < 2 && Mathf.Abs(node.worldBound.width - width) < 1);
            Assert.That(Vector2.Distance(node.parent.WorldToLocal(viewport.worldBound.center), anchor), Is.LessThan(2), "Zoom should retain the point at the viewport center.");
            view.FitAll();
            yield return WaitForUI(() => view.Element.Query(className: "flow-node").ToList().All(card => viewport.worldBound.Contains(card.worldBound.min) && viewport.worldBound.Contains(card.worldBound.max - Vector2.one)));
            foreach (var card in view.Element.Query(className: "flow-node").ToList())
            {
                Assert.That(viewport.worldBound.Contains(card.worldBound.min), Is.True, card.name);
                Assert.That(viewport.worldBound.Contains(card.worldBound.max - Vector2.one), Is.True, card.name);
            }
            Assert.That(codec.ToJson(asset.Read()), Is.EqualTo(contentBefore));
            Assert.That(JsonUtility.ToJson(layout), Is.EqualTo(layoutBefore));
            Call("Navigate", "gallery.a2", true);
            for (var i = 0; i < 5; i++) yield return null;
            var block = window.rootVisualElement.Q("node-gallery.a1");
            Assert.That(block, Is.Not.Null);
            Assert.That(block.ClassListContains("node-highlight"), Is.True, "Searching a grouped dialogue line should focus its visible block.");
            var selectedViewport = ((FlowCanvas)Field("canvas")).Element;
            Assert.That(selectedViewport.worldBound.Contains(block.worldBound.center), Is.True);
        }

        private static IEnumerator WaitForUI(System.Func<bool> ready)
        {
            // UI Toolkit schedules work by elapsed time, not by a fixed number of test frames.
            var deadline = EditorApplication.timeSinceStartup + 2;
            while (!ready() && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, "The rendered UI did not reach the expected state.");
        }

        [Test] public void ValidationMessagesAppearOnTheVisibleDialogueBlockAndClearAfterRepair()
        {
            Call("Navigate", "gallery.a2", true);
            var data = asset.Read(); var step = data.events.Find(e => e.id == "gallery-event").presentation.Find(s => s.id == "gallery.a2");
            step.nextStepId = "missing-step";
            var view = (FlowCanvas)Field("canvas");
            view.ShowIssues(ProjectValidator.Validate(data).issues, new ContentIndex(data));
            var card = view.Element.Q("node-gallery.a1");
            Assert.That(card.ClassListContains("node-error"), Is.True);
            Assert.That(card.Query<Label>(className: "node-error-message").ToList().Any(l => !string.IsNullOrWhiteSpace(l.text)), Is.True);
            step.nextStepId = "gallery.choice";
            view.ShowIssues(ProjectValidator.Validate(data).issues, new ContentIndex(data));
            Assert.That(card.Q("node-issues"), Is.Null);
            Assert.That(card.ClassListContains("node-error"), Is.False);
        }

        private static void Pointer(VisualElement port, EventType type, Vector2 position, EventModifiers modifiers = EventModifiers.None, Vector2 delta = default)
        {
            var input = new Event { type = type, button = 0, mousePosition = position, modifiers = modifiers, delta = delta };
            if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = port; port.SendEvent(e); }
            else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = port; port.SendEvent(e); }
            else { using var e = PointerUpEvent.GetPooled(input); e.target = port; port.SendEvent(e); }
        }
        private static void Drag(VisualElement output, Vector2 destination)
        {
            Pointer(output, EventType.MouseDown, output.worldBound.center);
            Pointer(output, EventType.MouseDrag, destination);
            Pointer(output, EventType.MouseUp, destination);
        }
        private Button GardenOutput() => window.rootVisualElement.Q<Button>("output-gallery.choice-gallery.choose-garden");
        [UnityTest] public IEnumerator StartWireCanDisconnectReconnectPersistAndControlRuntimeEntry()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery-event", true);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            ((FlowCanvas)Field("canvas")).FitAll();
            for (var i = 0; i < 5; i++) yield return null;
            var output = window.rootVisualElement.Q("output-gallery-event-next");
            Assert.That(output.enabledSelf, Is.True);
            Assert.That(window.rootVisualElement.Q("input-gallery.a1").enabledSelf, Is.True);
            var order = asset.Read().events.Find(e => e.id == "gallery-event").presentation.Select(s => s.id).ToArray();
            var revision = asset.Revision;
            Drag(output, EmptyCanvas()); DismissNodeSearch();
            Assert.That(((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event").entryStepId, Is.EqualTo("$disconnected"));
            Call("ApplyChanges"); Assert.That(asset.Revision, Is.EqualTo(revision));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            yield return WaitForUI(() => ((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event").entryStepId != "$disconnected");
            Call("Stage", new GraphEdit { operation = "ConnectNodes", eventId = "gallery-event", targetId = "gallery-event", portId = "next", nextId = "gallery.images" });
            Call("ApplyChanges"); Call("Reload");
            var saved = asset.Read(); var flow = saved.events.Find(e => e.id == "gallery-event");
            Assert.That(flow.presentation.Select(s => s.id), Is.EqualTo(order));
            Assert.That(flow.entryStepId, Is.EqualTo("gallery.images"));
            GameSession Run(ProjectDefinition data)
            {
                foreach (var item in data.events) item.callOnly = item.id != "gallery-event";
                data.events.Find(e => e.id == "gallery-event").conditions.Clear();
                var runtime = new GameSession(data);
                runtime.PerformActivity(data.activities.First(a => runtime.CanPerform(a.id)).id);
                return runtime;
            }
            var session = Run(saved);
            Assert.That(session.State.PresentationStepId, Is.EqualTo("gallery.images"));
            var restored = new GameSession(saved); restored.Restore(session.Capture());
            Assert.That(restored.State.PresentationStepId, Is.EqualTo("gallery.images"));
            Call("Stage", new GraphEdit { operation = "ConnectNodes", eventId = "gallery-event", targetId = "gallery-event", portId = "next", nextId = EventSequence.End });
            Call("ApplyChanges"); session = Run(asset.Read());
            Assert.That(session.State.PendingEventId, Is.Empty);
        }
        [UnityTest] public IEnumerator CanvasSupportsNegativeCoordinatesAndMinimapNavigationWithoutScrollbars()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.choice", true);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            var canvas = (FlowCanvas)Field("canvas"); var scroll = canvas.Element;
            Assert.That(scroll.Query<Scroller>().ToList(), Is.Empty);

            canvas.SetZoom(.6f); canvas.ScrollOffset = new Vector2(-2400, -1700);
            for (var i = 0; i < 5; i++) yield return null;
            var world = window.rootVisualElement.Q("flow-world");
            var center = world.WorldToLocal(scroll.worldBound.center);
            Assert.That(Vector2.Distance(center, (canvas.ScrollOffset + scroll.layout.size / 2) / canvas.Zoom), Is.LessThan(2));
            var before = codec.ToJson((ProjectDefinition)Field("project"));
            var map = window.rootVisualElement.Q("graph-minimap");
            Pointer(map, EventType.MouseDown, map.worldBound.center);
            Pointer(map, EventType.MouseUp, map.worldBound.center);
            Assert.That(Vector2.Distance(canvas.ScrollOffset, new Vector2(-2400, -1700)), Is.GreaterThan(10));
            Assert.That(codec.ToJson((ProjectDefinition)Field("project")), Is.EqualTo(before));
            var position = new Vector2(-750, -520);
            Call("ShowNodeSearch", position);
            var search = window.rootVisualElement.Q<TextField>("node-search-query");
            search.value = StudioText.T("New") + " · " + StudioText.T("Effect");
            using (var enter = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None)) { enter.target = search; search.SendEvent(enter); }
            var id = (string)Field("selectedId");
            var layout = (GraphLayoutAsset)Field("layout");
            Assert.That(layout.Get("gallery-event", id, 0).x, Is.EqualTo(position.x));
            Assert.That(layout.Get("gallery-event", id, 0).y, Is.EqualTo(position.y));
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            canvas = (FlowCanvas)Field("canvas"); canvas.FocusNode(id);
            for (var i = 0; i < 5; i++) yield return null;
            var title = window.rootVisualElement.Q("node-" + id).Q(className: "node-title");
            Pointer(title, EventType.MouseDown, title.worldBound.center);
            Pointer(title, EventType.MouseDrag, title.worldBound.center - Vector2.one * 20, delta: -Vector2.one * 20);
            Pointer(title, EventType.MouseUp, title.worldBound.center);
            Assert.That(layout.Get("gallery-event", id, 0).x, Is.LessThan(position.x));
            Assert.That(layout.Get("gallery-event", id, 0).y, Is.LessThan(position.y));
        }
        [UnityTest] public IEnumerator MultipleSelectionSupportsModifiersMarqueeAndGroupMovement()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.choice", true);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            var canvas = (FlowCanvas)Field("canvas"); canvas.FitAll();
            for (var i = 0; i < 8; i++) yield return null;
            var a = window.rootVisualElement.Q("node-gallery.garden");
            var b = window.rootVisualElement.Q("node-gallery.outfit");
            var before = codec.ToJson((ProjectDefinition)Field("project"));
            Pointer(a, EventType.MouseDown, a.worldBound.center);
            Pointer(b, EventType.MouseDown, b.worldBound.center, EventModifiers.Control);
            Assert.That(canvas.SelectedNodes, Is.EquivalentTo(new[] { "gallery.garden", "gallery.outfit" }));
            Assert.That(a.ClassListContains("node-selected") && b.ClassListContains("node-selected"), Is.True);
            Pointer(b, EventType.MouseDown, b.worldBound.center, EventModifiers.Shift);
            Assert.That(canvas.SelectedNodes, Is.EquivalentTo(new[] { "gallery.garden" }));
            Pointer(b, EventType.MouseDown, b.worldBound.center, EventModifiers.Shift);
            var layout = (GraphLayoutAsset)Field("layout");
            var pa = layout.Get("gallery-event", "gallery.garden", 0);
            var pb = layout.Get("gallery-event", "gallery.outfit", 0);
            var oldA = new Vector2(pa.x, pa.y); var oldB = new Vector2(pb.x, pb.y);
            var title = a.Q(className: "node-title");
            Pointer(title, EventType.MouseDown, title.worldBound.center);
            Pointer(title, EventType.MouseDrag, title.worldBound.center + new Vector2(30, 20), delta: new Vector2(30, 20));
            Pointer(title, EventType.MouseUp, title.worldBound.center);
            Assert.That(pa.x, Is.GreaterThan(oldA.x));
            Assert.That(Vector2.Distance(new Vector2(pa.x, pa.y) - oldA, new Vector2(pb.x, pb.y) - oldB), Is.LessThan(.01f));
            Assert.That(codec.ToJson((ProjectDefinition)Field("project")), Is.EqualTo(before));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            canvas = (FlowCanvas)Field("canvas"); canvas.FitAll();
            Assert.That(layout.Get("gallery-event", "gallery.garden", 0).x, Is.EqualTo(oldA.x).Within(.01));
            Assert.That(layout.Get("gallery-event", "gallery.outfit", 0).x, Is.EqualTo(oldB.x).Within(.01));
            for (var i = 0; i < 8; i++) yield return null;
            var world = window.rootVisualElement.Q("flow-world");
            var viewport = canvas.Element.worldBound;
            Pointer(world, EventType.MouseDown, viewport.min + Vector2.one * 5);
            Pointer(world, EventType.MouseDrag, viewport.max - Vector2.one * 5);
            Pointer(world, EventType.MouseUp, viewport.max - Vector2.one * 5);
            Assert.That(canvas.SelectedNodes.Count, Is.GreaterThan(1));
            Assert.That(window.rootVisualElement.Q("selection-marquee").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }
        private void DismissNodeSearch()
        {
            var search = window.rootVisualElement.Q<TextField>("node-search-query");
            Assert.That(search, Is.Not.Null);
            using var escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None);
            escape.target = search; search.SendEvent(escape);
            Assert.That(window.rootVisualElement.Q("node-search"), Is.Null);
        }
        [Test] public void StandaloneNodesDoNotConnectTheirEmptyOutputsOrChangeExistingWires()
        {
            Call("Navigate", "gallery.choice", true);
            var original = GraphProjection.Build((ProjectDefinition)Field("project"), "gallery-event").edges;
            var ids = new System.Collections.Generic.List<string>();
            foreach (var kind in new[] { PresentationStepKind.Condition, PresentationStepKind.Choice, PresentationStepKind.Effect })
            {
                Call("AddNode", kind); ids.Add((string)Field("selectedId"));
            }
            Call("AddSharedContent", "gallery-event", "gallery.choice", (object)null); ids.Add((string)Field("selectedId"));
            Call("AddSharedFlow", "invitation", (object)null); ids.Add((string)Field("selectedId"));
            var graph = GraphProjection.Build((ProjectDefinition)Field("project"), "gallery-event");
            foreach (var id in ids)
            {
                Assert.That(graph.edges.Where(e => e.sourceId == id).Select(e => e.targetId), Is.All.EqualTo("$disconnected"));
                Assert.That(graph.edges.Any(e => e.targetId == id), Is.False);
            }
            foreach (var edge in original)
                Assert.That(graph.edges.Any(e => e.sourceId == edge.sourceId && e.portId == edge.portId && e.targetId == edge.targetId), Is.True);
        }
        [UnityTest] public IEnumerator EmptyOutputSearchConnectsOnlyTheRequestedSide()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.choice", true);
            Call("AddNode", PresentationStepKind.Effect);
            var sourceId = (string)Field("selectedId");
            Call("Navigate", sourceId, true);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            yield return WaitForUI(() => window.rootVisualElement.Q("output-" + sourceId + "-next").worldBound.width > 0);
            Drag(window.rootVisualElement.Q("output-" + sourceId + "-next"), EmptyCanvas());
            var search = window.rootVisualElement.Q<TextField>("node-search-query");
            Assert.That(search, Is.Not.Null); search.value = StudioText.T("New") + " · " + StudioText.T("Condition");
            using (var enter = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None)) { enter.target = search; search.SendEvent(enter); }
            var flow = ((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event");
            var created = flow.presentation.Find(s => s.id == (string)Field("selectedId"));
            Assert.That(flow.presentation.Find(s => s.id == sourceId).nextStepId, Is.EqualTo(created.id));
            Assert.That(created.nextStepId, Is.EqualTo("$disconnected"));
            Assert.That(created.falseStepId, Is.EqualTo("$disconnected"));
        }
        [UnityTest] public IEnumerator BlankWireDropAddsAndConnectsNodeAndUndoRestoresOriginal()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.images", true);
            yield return WaitForUI(() => window.rootVisualElement.Q("input-gallery.images").worldBound.width > 0);
            var before = codec.ToJson((ProjectDefinition)Field("project"));
            var persisted = codec.ToJson(asset.Read());
            Drag(window.rootVisualElement.Q("input-gallery.images"), EmptyCanvas());
            Assert.That(codec.ToJson((ProjectDefinition)Field("project")), Is.EqualTo(before));
            var search = window.rootVisualElement.Q<TextField>("node-search-query");
            Assert.That(search, Is.Not.Null); search.value = StudioText.T("New") + " · " + StudioText.T("Effect");
            using (var enter = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None)) { enter.target = search; search.SendEvent(enter); }
            var flow = ((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event");
            var id = (string)Field("selectedId");
            Assert.That(flow.presentation.Find(s => s.id == id).kind, Is.EqualTo(PresentationStepKind.Effect));
            Assert.That(flow.presentation.Find(s => s.id == id).nextStepId, Is.EqualTo("gallery.images"));
            Assert.That(flow.presentation.Find(s => s.id == "gallery.garden").nextStepId, Is.EqualTo(id));
            Assert.That(flow.presentation.Find(s => s.id == "gallery.outfit").nextStepId, Is.EqualTo("gallery.images"));
            Assert.That(codec.ToJson(asset.Read()), Is.EqualTo(persisted));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            yield return null;
            Assert.That(((AuthoringChangeSet)Field("changes")).edits, Is.Empty, "One undo must remove both creation and connection.");
            var restored = (ProjectDefinition)Field("project");
            var expected = codec.FromJson(before);
            // Preview evaluation assigns a prospective revision even for an empty draft.
            expected.revision = restored.revision;
            Assert.That(codec.ToJson(restored), Is.EqualTo(codec.ToJson(expected)));
        }
        [UnityTest] public IEnumerator BlankOutputDropInsertsNodeBetweenOriginalEndpoints()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.choice", true);
            yield return WaitForUI(() => GardenOutput().worldBound.width > 0);
            Drag(GardenOutput(), EmptyCanvas());
            var search = window.rootVisualElement.Q<TextField>("node-search-query");
            Assert.That(search, Is.Not.Null); search.value = StudioText.T("New") + " · " + StudioText.T("Effect");
            using (var enter = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None)) { enter.target = search; search.SendEvent(enter); }
            var data = (ProjectDefinition)Field("project");
            Assert.That(GardenDestination(data), Is.EqualTo((string)Field("selectedId")));
            Assert.That(data.events.Find(e => e.id == "gallery-event").presentation.Find(s => s.id == (string)Field("selectedId")).nextStepId, Is.EqualTo("gallery.garden"));
        }
        [UnityTest] public IEnumerator OutsideSearchClickDismissesAndDisconnectsOnlyPendingWire()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.choice", true);
            yield return WaitForUI(() => GardenOutput().worldBound.width > 0);
            Drag(GardenOutput(), EmptyCanvas());
            yield return WaitForUI(() => window.rootVisualElement.Q("node-search").worldBound.width > 0);
            Pointer(window.rootVisualElement, EventType.MouseDown, window.rootVisualElement.worldBound.max - Vector2.one * 3);
            Assert.That(window.rootVisualElement.Q("node-search"), Is.Null);
            Assert.That(GardenDestination((ProjectDefinition)Field("project")), Is.EqualTo("$disconnected"));
        }
        private Vector2 EmptyCanvas()
        {
            var viewport = ((FlowCanvas)Field("canvas")).Element.worldBound;
            var occupied = window.rootVisualElement.Query<VisualElement>(className: "flow-node").ToList()
                .Concat(window.rootVisualElement.Query<VisualElement>(className: "connection-pin").ToList())
                .Append(window.rootVisualElement.Q("graph-minimap")).Where(e => e != null).Select(e => e.worldBound).ToArray();
            for (var y = viewport.yMin + 8; y < viewport.yMax - 8; y += 25)
                for (var x = viewport.xMin + 8; x < viewport.xMax - 8; x += 25)
                {
                    var point = new Vector2(x, y); if (!occupied.Any(rect => rect.Contains(point))) return point;
                }
            Assert.Fail("No empty canvas point is visible."); return Vector2.zero;
        }
        private string GardenDestination(ProjectDefinition data) => data.events.Find(e => e.id == "gallery-event").presentation.Find(s => s.id == "gallery.choice").choices.Find(c => c.id == "gallery.choose-garden").nextStepId;

        [UnityTest] public IEnumerator DragConnectionsUseReviewedDraftsSupportUndoAndPersistOnlyOnApply()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.choice", true);
            yield return WaitForUI(() => GardenOutput().worldBound.width > 0);
            ((FlowCanvas)Field("canvas")).FitAll();
            yield return WaitForUI(() => ((FlowCanvas)Field("canvas")).Element.worldBound.Contains(window.rootVisualElement.Q("input-gallery.outfit").worldBound.center));
            var original = codec.ToJson(asset.Read());
            Drag(GardenOutput(), window.rootVisualElement.Q("input-gallery.outfit").worldBound.center);
            Assert.That(GardenDestination((ProjectDefinition)Field("project")), Is.EqualTo("gallery.outfit"));
            Assert.That(codec.ToJson(asset.Read()), Is.EqualTo(original));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            yield return WaitForUI(() => GardenDestination((ProjectDefinition)Field("project")) == "gallery.garden");
            Undo.PerformRedo();
            yield return WaitForUI(() => GardenDestination((ProjectDefinition)Field("project")) == "gallery.outfit");
            yield return WaitForUI(() => GardenOutput().worldBound.width > 0);
            Drag(GardenOutput(), EmptyCanvas());
            DismissNodeSearch();
            Assert.That(((AuthoringChangeSet)Field("changes")).edits.Last().operation, Is.EqualTo("DisconnectNodes"));
            Assert.That(window.rootVisualElement.Q("node-gallery.choice").ClassListContains("node-error"), Is.True);
            Assert.That(codec.ToJson(asset.Read()), Is.EqualTo(original));
            yield return WaitForUI(() => GardenOutput().worldBound.width > 0);
            ((FlowCanvas)Field("canvas")).FitAll();
            yield return WaitForUI(() => ((FlowCanvas)Field("canvas")).Element.worldBound.Contains(window.rootVisualElement.Q("input-gallery.outfit").worldBound.center));
            Drag(GardenOutput(), window.rootVisualElement.Q("input-gallery.outfit").worldBound.center);
            Assert.That(window.rootVisualElement.Q("node-gallery.choice").ClassListContains("node-error"), Is.False);
            Call("ApplyChanges"); Call("Reload");
            Assert.That(GardenDestination(asset.Read()), Is.EqualTo("gallery.outfit"));
        }

        [UnityTest] public IEnumerator InvalidDropAndEscapeKeepTheOriginalConnection()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.choice", true);
            yield return WaitForUI(() => GardenOutput().worldBound.width > 0);
            var original = codec.ToJson((ProjectDefinition)Field("project"));
            var output = GardenOutput();
            ((FlowCanvas)Field("canvas")).FitAll();
            yield return WaitForUI(() => ((FlowCanvas)Field("canvas")).Element.worldBound.Contains(window.rootVisualElement.Q("input-gallery.outfit").worldBound.center));
            Drag(output, window.rootVisualElement.Q("output-gallery.choice-gallery.choose-outfit").worldBound.center);
            Assert.That(((AuthoringChangeSet)Field("changes")).edits, Is.Empty, "Outputs connect to inputs, never to outputs.");
            Drag(output, new Vector2(-100, -100));
            Assert.That(((AuthoringChangeSet)Field("changes")).edits, Is.Empty);
            Pointer(output, EventType.MouseDown, output.worldBound.center);
            Pointer(output, EventType.MouseDrag, output.worldBound.center + Vector2.one * 30);
            using (var escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None)) { escape.target = output; output.SendEvent(escape); }
            Pointer(output, EventType.MouseUp, EmptyCanvas());
            Assert.That(codec.ToJson((ProjectDefinition)Field("project")), Is.EqualTo(original));
            Assert.That(((AuthoringChangeSet)Field("changes")).edits, Is.Empty);
            Assert.That(output.ClassListContains("port-armed"), Is.False);
        }

        [UnityTest] public IEnumerator InputHandleKeepsItsDestinationAndReplacesOnlyItsOwnSource()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.images", true);
            yield return WaitForUI(() => window.rootVisualElement.Q("input-gallery.images").worldBound.width > 0);
            var before = codec.ToJson(asset.Read());
            string Next(string id) => ((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event").presentation.Find(s => s.id == id).nextStepId;
            var first = window.rootVisualElement.Q<Button>("input-gallery.images");
            Assert.That(first.ClassListContains("connection-pin"), Is.True);
            Assert.That(window.rootVisualElement.Q("input-gallery.images-from-gallery.outfit-next"), Is.Not.Null);
            Drag(first, EmptyCanvas());
            DismissNodeSearch();
            Assert.That(Next("gallery.garden"), Is.EqualTo("$disconnected"));
            Assert.That(Next("gallery.outfit"), Is.EqualTo("gallery.images"));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            yield return WaitForUI(() => Next("gallery.garden") == "gallery.images");
            yield return WaitForUI(() => window.rootVisualElement.Q("input-gallery.images").worldBound.width > 0);
            ((FlowCanvas)Field("canvas")).FitAll();
            yield return WaitForUI(() => ((FlowCanvas)Field("canvas")).Element.worldBound.Contains(window.rootVisualElement.Q("input-gallery.outfit").worldBound.center));
            Drag(window.rootVisualElement.Q("input-gallery.images"), GardenOutput().worldBound.center);
            Assert.That(Next("gallery.garden"), Is.EqualTo("$disconnected"));
            Assert.That(GardenDestination((ProjectDefinition)Field("project")), Is.EqualTo("gallery.images"));
            Assert.That(Next("gallery.outfit"), Is.EqualTo("gallery.images"));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            yield return WaitForUI(() => Next("gallery.garden") == "gallery.images" && GardenDestination((ProjectDefinition)Field("project")) == "gallery.garden");
            Assert.That(codec.ToJson(asset.Read()), Is.EqualTo(before));
        }

        [Test] public void MovingOutputOntoAnExistingWireDoesNotRetargetARegroupedDialogueTail()
        {
            var data = asset.Read(); var flow = data.events.Find(e => e.id == "gallery-event");
            flow.presentation.Clear();
            foreach (var node in new[]
            {
                new PresentationStep { id = "endpoint.first", nameKey = "sequence.event", kind = PresentationStepKind.Dialogue, nextStepId = "endpoint.second" },
                new PresentationStep { id = "endpoint.second", nameKey = "sequence.event", kind = PresentationStepKind.Dialogue, nextStepId = "endpoint.third" },
                new PresentationStep { id = "endpoint.third", nameKey = "sequence.event", kind = PresentationStepKind.Dialogue, nextStepId = EventSequence.End },
                new PresentationStep { id = "endpoint.aux", nameKey = "sequence.event", kind = PresentationStepKind.Effect, nextStepId = "endpoint.second" }
            }) flow.presentation.Add(node);
            asset.Write(data); Call("Reload"); Call("Navigate", "endpoint.aux", true);
            Call("ChangeConnection", new FlowEdge { sourceId = "endpoint.aux", portId = "next", targetId = "endpoint.second" },
                new FlowEdge { sourceId = "endpoint.first", portId = "next", targetId = "endpoint.second" });
            var changed = ((ProjectDefinition)Field("project")).events.Find(e => e.id == "gallery-event");
            Assert.That(changed.presentation.Find(s => s.id == "endpoint.aux").nextStepId, Is.EqualTo("$disconnected"));
            Assert.That(changed.presentation.Find(s => s.id == "endpoint.third").nextStepId, Is.EqualTo(EventSequence.End));
            Assert.That(((AuthoringChangeSet)Field("changes")).edits.Count, Is.EqualTo(1));
            Assert.That(ProjectValidator.Validate((ProjectDefinition)Field("project")).issues.Any(i => i.message.Contains("cycle")), Is.False);
        }

        [UnityTest] public IEnumerator NodeSearchPreservesViewportAcrossCreationAndConnection()
        {
            window.position = new Rect(0, 0, 1600, 1000); Call("Navigate", "gallery.choice", true);
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            var canvas = (FlowCanvas)Field("canvas");
            canvas.SetZoom(.55f);
            for (var i = 0; i < 5; i++) yield return null;
            canvas.ScrollOffset = new Vector2(140, 110);
            yield return null;
            var zoom = canvas.Zoom; var scroll = canvas.ScrollOffset;
            Call("ShowNodeSearch", new Vector2(1800, 1800));
            var query = window.rootVisualElement.Q<TextField>("node-search-query");
            query.value = StudioText.T("New") + " · " + StudioText.T("Effect");
            using (var enter = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None)) { enter.target = query; query.SendEvent(enter); }
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            for (var i = 0; i < 5; i++) yield return null;
            Assert.That(((FlowCanvas)Field("canvas")).Zoom, Is.EqualTo(zoom).Within(.001));
            Assert.That(Vector2.Distance(((FlowCanvas)Field("canvas")).ScrollOffset, scroll), Is.LessThan(1));
            var edge = GraphProjection.Build((ProjectDefinition)Field("project"), "gallery-event").edges.First(e => e.sourceId == "gallery.choice");
            Call("ShowConnectionNodeSearch", new Vector2(1900, 1800), edge, edge, false);
            query = window.rootVisualElement.Q<TextField>("node-search-query");
            query.value = StudioText.T("New") + " · " + StudioText.T("Effect");
            using (var enter = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None)) { enter.target = query; query.SendEvent(enter); }
            yield return WaitForUI(() => (bool)Field("canvasViewReady"));
            for (var i = 0; i < 5; i++) yield return null;
            Assert.That(((FlowCanvas)Field("canvas")).Zoom, Is.EqualTo(zoom).Within(.001));
            Assert.That(Vector2.Distance(((FlowCanvas)Field("canvas")).ScrollOffset, scroll), Is.LessThan(1));
        }
        [UnityTest] public IEnumerator SmallStudioCanScrollToLastNavigationPage()
        {
            var studio = StudioWindow.OpenProject(asset);
            try
            {
                studio.position = new Rect(0, 0, 980, 640); studio.CreateGUI();
                for (var i = 0; i < 5; i++) yield return null;
                var navigation = studio.rootVisualElement.Q<ScrollView>("navigation");
                Assert.That(navigation, Is.Not.Null);
                var last = navigation.Q<Button>("nav-LLM commands");
                Assert.That(navigation.verticalScroller.highValue, Is.GreaterThan(0));
                navigation.ScrollTo(last);
                yield return null;
                Assert.That(navigation.contentViewport.worldBound.Contains(last.worldBound.center), Is.True, "Last page must be reachable at the minimum window size.");
            }
            finally { studio.Close(); }
        }

    }
}
