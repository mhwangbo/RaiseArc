using System;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Editor;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public sealed partial class SimulationWindow
    {
        [SerializeField] private RaiseArcEndingTestAsset savedTest;
        [SerializeField] private string resultId = "";
        private EndingTestDefinition endingDraft;
        private int testRevision;
        private string endingReplayJobId;

        [InitializeOnLoadMethod]
        private static void ConnectEndingTests()
        {
            EndingTests.OpenRequested -= OpenEnding;
            EndingTests.OpenRequested += OpenEnding;
            EndingTests.OpenSavedRequested -= OpenSavedEnding;
            EndingTests.OpenSavedRequested += OpenSavedEnding;
        }
        private static void OpenSavedEnding(RaiseArcEndingTestAsset test)
        {
            Open(); var window = GetWindow<SimulationWindow>(); window.savedTest = test;
            window.ClearAnalysisSelection();
            window.asset = test.Project; window.endingDraft = null; window.CreateGUI();
        }
        private void ClearAnalysisSelection()
        {
            jobId = ""; resultId = ""; endingReplayJobId = null;
            pathRecord = -1; playing = false;
        }
        private void OnEnable() { Undo.undoRedoPerformed += ReloadEndingAfterUndo; }
        private void ReloadEndingAfterUndo() { if (savedBalance != null) { balanceDraft = null; balanceWeightEdits.Clear(); CreateGUI(); } else if (savedTest != null) { endingDraft = null; CreateGUI(); } }
        private static void OpenEnding(GameProjectAsset project, string endingId)
        {
            Open(); var window = GetWindow<SimulationWindow>();
            window.ClearAnalysisSelection();
            window.asset = project; window.savedTest = null; window.testRevision = 0;
            window.endingDraft = EndingTests.New(project, endingId); window.settings = window.endingDraft.settings;
            window.CreateGUI();
        }
        private void DrawEndingTest()
        {
            var balanceToggle = new UnityEngine.UIElements.Toggle(T("Saved balance tests · play many times", "저장된 밸런스 테스트 · 여러 번 플레이")) { value = balanceMode };
            balanceToggle.RegisterValueChangedCallback(e => { balanceMode = e.newValue; CreateGUI(); }); rootVisualElement.Add(balanceToggle);
            if (balanceMode) return;
            var panel = new Foldout { text = T("Can I reach this ending? · Saved tests", "이 엔딩에 도달할 수 있을까요? · 저장된 테스트"), value = true };
            panel.style.maxHeight = 290;
            var content = new ScrollView(); panel.Add(content); rootVisualElement.Add(panel);
            var saved = new ObjectField(T("Saved test", "저장된 테스트")) { objectType = typeof(RaiseArcEndingTestAsset), value = savedTest };
            saved.RegisterValueChangedCallback(e => { savedTest = e.newValue as RaiseArcEndingTestAsset; if (savedTest != null) asset = savedTest.Project; ClearAnalysisSelection(); endingDraft = null; settings = new ExplorationSettings(); CreateGUI(); }); content.Add(saved);
            if (endingDraft == null && savedTest != null)
            { asset = savedTest.Project; endingDraft = savedTest.Read(); testRevision = savedTest.Revision; settings = endingDraft.settings; }
            Button(content, T("New ending test…", "새 엔딩 테스트…"), () => {
                if (asset == null) throw new InvalidOperationException(T("Select a game project first.", "먼저 게임 프로젝트를 선택하세요."));
                var p = asset.Read(); var menu = new GenericMenu();
                foreach (var ending in p.endings)
                {
                    var id = ending.id;
                    var text = p.translations.Find(t => t.key == ending.nameKey && t.locale == StudioText.Language)?.text;
                    menu.AddItem(new GUIContent((string.IsNullOrEmpty(text) ? ending.id : text) + " · " + id), false, () => OpenEnding(asset, id));
                }
                if (p.endings.Count == 0) menu.AddDisabledItem(new GUIContent(T("Create and save an ending first.", "먼저 엔딩을 작성하고 저장하세요.")));
                menu.ShowAsContext();
            });
            if (endingDraft == null) return;
            var draft = endingDraft;
            content.Add(new Label(T("Uses saved game content. Unsaved editor changes are excluded.", "저장된 게임 콘텐츠를 사용합니다. 편집 중인 미저장 변경은 포함하지 않습니다.")));
            Text(content, T("Test name", "테스트 이름"), draft.name, x => draft.name = x);
            content.Add(new Label(T("Ending selected at game end", "게임 종료 시 선택될 엔딩") + ": " + draft.endingId));
            var start = new Foldout { text = T("Starting conditions · new game", "시작 조건 · 새 게임"), value = false }; content.Add(start);
            var overrideMoney = new Toggle(T("Override starting money for this test only", "이번 테스트에서만 시작 돈 변경")) { value = draft.overrideMoney };
            overrideMoney.RegisterValueChangedCallback(e => draft.overrideMoney = e.newValue); start.Add(overrideMoney);
            Int(start, T("Starting money", "시작 돈"), draft.money, x => draft.money = x);
            if (asset != null) foreach (var stat in asset.Read().stats)
            {
                var id = stat.id; var existing = draft.stats.Find(x => x.id == id);
                var enabled = new Toggle(id) { value = existing != null };
                var number = new IntegerField(T("Initial value", "시작값")) { value = existing?.value ?? stat.initial }; number.SetEnabled(existing != null);
                enabled.RegisterValueChangedCallback(e => { draft.stats.RemoveAll(x => x.id == id); if (e.newValue) draft.stats.Add(new IntEntry { id = id, value = number.value }); number.SetEnabled(e.newValue); });
                number.RegisterValueChangedCallback(e => { var entry = draft.stats.Find(x => x.id == id); if (entry != null) entry.value = e.newValue; }); start.Add(enabled); start.Add(number);
            }
            var actions = new Foldout { text = T("Action restrictions", "행동 제한"), value = false }; content.Add(actions);
            actions.Add(new Label(T("Restrictions never bypass costs or requirements. Empty allowed list means all activities.", "비용·요구 조건을 우회하지 않습니다. 허용 목록이 비어 있으면 모든 활동을 허용합니다.")));
            if (asset != null) foreach (var activity in asset.Read().activities)
            {
                var id = activity.id;
                var allow = new Toggle(T("Allow only", "허용 목록") + " · " + id) { value = settings.allowedActivities.Contains(id) };
                allow.RegisterValueChangedCallback(e => { settings.allowedActivities.Remove(id); if (e.newValue) settings.allowedActivities.Add(id); }); actions.Add(allow);
                var ban = new Toggle(T("Forbid", "금지") + " · " + id) { value = settings.forbiddenActivities.Contains(id) };
                ban.RegisterValueChangedCallback(e => { settings.forbiddenActivities.Remove(id); if (e.newValue) settings.forbiddenActivities.Add(id); }); actions.Add(ban);
            }
            Button(content, T("Find path / rerun test", "경로 찾기 / 테스트 다시 실행"), () => {
                draft.settings = settings;
                var job = EndingTests.Start(asset, draft, testRevision); jobId = job.Id; pathRecord = -1; Refresh();
            });
            Button(content, T("Save test definition", "테스트 정의 저장"), () => {
                draft.settings = settings;
                savedTest = EndingTests.Save(asset, draft, savedTest, testRevision); testRevision = savedTest.Revision;
                saved.SetValueWithoutNotify(savedTest); message.text = T("Saved test revision ", "테스트 저장 리비전 ") + testRevision;
            });
            Button(content, T("Reload saved test (also after Undo/Redo)", "저장된 테스트 다시 읽기 (실행 취소/다시 실행 후)"), () => { endingDraft = null; CreateGUI(); });
            Button(content, T("Show discovered path", "발견한 경로 보기"), () => {
                pathRecord = EndingTests.TargetRecord(Current);
                if (pathRecord < 0) throw new InvalidOperationException(T("No target path found. Check the scope and stop reason.", "발견한 목표 경로가 없습니다. 탐색 범위와 중단 이유를 확인하세요."));
                tab = "Replay"; Refresh();
            });
            Button(content, T("Preserve result and original content", "결과와 당시 콘텐츠 보존"), () => { resultId = EndingTests.Preserve(Current); content.Q<TextField>("ending-result-id").SetValueWithoutNotify(resultId); message.text = T("Preserved result: ", "보존한 결과: ") + resultId; });
            var resultField = new TextField(T("Preserved result ID", "보존한 결과 ID")) { name = "ending-result-id", value = resultId };
            resultField.RegisterValueChangedCallback(e => resultId = e.newValue); content.Add(resultField);
            Button(content, T("Replay original candidate", "원래 후보에서 재현"), () => ShowEndingReplay(false));
            Button(content, T("Recheck inputs on current candidate", "현재 후보에서 같은 입력 재검사"), () => ShowEndingReplay(true));
            Button(content, T("Cancel input replay", "입력 재현 취소"), () => { if (!string.IsNullOrEmpty(endingReplayJobId)) EndingReplayJobs.Cancel(EndingReplayJobs.Find(asset, endingReplayJobId)); });
        }
        private void ShowEndingReplay(bool currentCandidate)
        {
            endingReplayJobId = EndingReplayJobs.Start(asset, resultId, currentCandidate).id;
            message.text = T("Replaying inputs…", "입력을 재현하고 있습니다…");
        }
        private void RefreshEndingReplay()
        {
            if (string.IsNullOrEmpty(endingReplayJobId)) return;
            var job = EndingReplayJobs.Find(asset, endingReplayJobId);
            if (job.status == "Running") return;
            endingReplayJobId = null;
            var replay = job.replay;
            body.Clear();
            Line(T("Actual runtime input replay", "실제 런타임 입력 재현"), true);
            Line(job.status + " · " + job.error);
            Line(string.IsNullOrEmpty(job.artifactId)
                ? T("Replay result was not preserved.", "재현 결과가 보존되지 않았습니다.")
                : T("Preserved replay result", "보존된 재현 결과") + ": " + job.artifactId);
            Line(T("Goal selected", "목표 엔딩 선택") + ": " + replay.goalReached + " · " + replay.endingId);
            Line(T("Same recorded states", "기록된 상태와 일치") + ": " + replay.sameStates + " · " + replay.reason);
            foreach (var step in replay.path) Line($"{step.depth}. {step.input?.kind} {step.input?.id} · day {step.state.day} · money {step.state.money} · {step.reason}");
            Line(T("A changed path does not prove the ending impossible. Use Find path to search again; the preserved result remains unchanged.", "이전 경로의 변화는 엔딩 도달 불가를 뜻하지 않습니다. 경로 찾기로 다시 탐색하세요. 보존한 결과는 변경되지 않습니다."));
        }
    }
}
