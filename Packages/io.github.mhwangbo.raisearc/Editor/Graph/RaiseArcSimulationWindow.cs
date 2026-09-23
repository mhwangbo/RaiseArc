using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    public sealed partial class SimulationWindow : EditorWindow
    {
        [SerializeField] private GameProjectAsset asset;
        [SerializeField] private string jobId = "", tab = "Overview", query = "";
        private ExplorationSettings settings = new ExplorationSettings();
        private ScrollView body;
        private Label message, progress;
        private int pathRecord = -1, replayStep;
        private double nextReplay;
        private bool playing;
        private float replaySeconds = 1;
        private string initialJson = "", weightsText = "";
        private string compareId = "";
        private string coverageFilter = "All";
        private IVisualElementScheduledItem refreshTimer;
        private int renderedTransitions = -1;
        private ExplorationStatus renderedStatus;
        private bool renderedStale;
        private readonly Dictionary<string, ToolbarButton> tabButtons = new Dictionary<string, ToolbarButton>();
        private static string T(string en, string ko) => StudioText.Language == "ko" ? ko : en;
        [MenuItem("Window/RaiseArc/Simulation Explorer")]
        public static void Open() { var w = GetWindow<SimulationWindow>(); w.titleContent = new GUIContent("RaiseArc · Simulation"); w.minSize = new Vector2(780, 580); w.Show(); }

        [InitializeOnLoadMethod] private static void RegisterPlanEntry() => SimulationJobs.PlanRequested += OpenPlan;
        public static void OpenPlan(GameProjectAsset project, RaiseArc.Core.GamePlan plan, StateData initial)
        {
            var data = project.Read();
            plan.Validate(data, true);
            if (data.time.periodNameKeys.Count == 0 || plan.capacity == 0 || plan.startTick != initial.day * data.time.PeriodsPerDay + initial.period || !string.IsNullOrEmpty(initial.pendingEventId))
                throw new InvalidOperationException(T("Analyze a complete plan before its first activity.", "첫 활동 실행 전, 모든 칸을 채운 계획을 분석하세요."));
            var w = GetWindow<SimulationWindow>();
            w.asset = project; w.endingDraft = null; w.balanceMode = false; w.savedTest = null;
            w.settings = new ExplorationSettings { mode = ExplorationMode.Exhaustive, useCommittedPlan = true,
                committedActivities = plan.entries.OrderBy(e => e.slot).Select(e => e.activityId).ToList(),
                horizonDays = data.durationDays, maxSteps = Math.Max(100, plan.entries.Count * 8), maxStates = 5000 };
            w.initialJson = JsonUtility.ToJson(initial); w.jobId = ""; w.tab = "Overview";
            w.titleContent = new GUIContent("RaiseArc · Simulation"); w.minSize = new Vector2(780, 580);
            w.CreateGUI(); w.Show(); w.Focus();
        }

        public void CreateGUI()
        {
            refreshTimer?.Pause();
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("workbench"); rootVisualElement.AddToClassList("simulation");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.mhwangbo.raisearc/Editor/Graph/Workbench.uss");
            if (sheet != null && !rootVisualElement.styleSheets.Contains(sheet)) rootVisualElement.styleSheets.Add(sheet);
            rootVisualElement.style.paddingLeft = rootVisualElement.style.paddingRight = 12;
            var title = new Label(T("Simulation Explorer · experimental", "시뮬레이션 탐색기 · 실험 기능")); title.style.fontSize = 21; title.AddToClassList("workbench-brand"); rootVisualElement.Add(title);
            var projectField = new ObjectField(T("Game project", "게임 프로젝트")) { objectType = typeof(GameProjectAsset), value = asset ?? Selection.activeObject as GameProjectAsset };
            asset = projectField.value as GameProjectAsset;
            projectField.RegisterValueChangedCallback(e => { asset = e.newValue as GameProjectAsset; savedTest = null; endingDraft = null; savedBalance = null; balanceDraft = null; balanceCandidate = null; balanceJobId = ""; ClearBalanceSelection(); balanceWeightEdits.Clear(); ClearAnalysisSelection(); settings = new ExplorationSettings(); CreateGUI(); }); rootVisualElement.Add(projectField);
            if (settings.useCommittedPlan) rootVisualElement.Add(new HelpBox(T("Committed plan: activities are fixed before execution. Only event inputs branch; this is not adaptive planning.", "확정 계획: 활동 순서는 실행 전에 고정됩니다. 사건 입력만 분기하며 결과를 보고 재편성하지 않습니다.") + "\n" + string.Join(" → ", settings.committedActivities.Select(id => asset.Read().activities.Find(a => a.id == id)?.nameKey ?? id)), HelpBoxMessageType.Info));
            DrawEndingTest();
            if (balanceMode) { DrawBalanceWorkspace(); StudioText.ApplyFont(rootVisualElement); return; }
            var setup = new Foldout { text = T("Advanced analysis settings", "고급 분석 설정"), value = endingDraft == null };
            setup.style.maxHeight = 245; var setupScroll = new ScrollView(); setup.Add(setupScroll); rootVisualElement.Add(setup);
            var modes = new List<string> { T("Quick check", "빠른 점검"), T("Target search", "목표 경로 찾기"), T("Exhaustive reachability", "도달성 전수 탐색"), T("Balance / Monte Carlo", "밸런스 / 반복 플레이") };
            var mode = new DropdownField(T("Mode", "모드"), modes, (int)settings.mode); mode.RegisterValueChangedCallback(e => settings.mode = (ExplorationMode)modes.IndexOf(e.newValue)); setupScroll.Add(mode);
            Text(setupScroll, T("Target content ID", "목표 콘텐츠 ID"), settings.targetId, x => settings.targetId = x);
            var targets = new List<string> { T("Content ID only", "콘텐츠 ID만"), T("Normal ending", "정상 엔딩"), T("Death", "사망"), T("Other failure", "기타 실패"), T("Deadlock", "진행 불가"), T("Runtime error", "런타임 오류"), T("Unsupported boundary", "미지원 경계") };
            var target = new DropdownField(T("Or target final result", "또는 목표 종료 결과"), targets, (int)settings.targetResult); target.RegisterValueChangedCallback(e => settings.targetResult = (PlayResult)targets.IndexOf(e.newValue)); setupScroll.Add(target);
            Int(setupScroll, T("Independent plays", "독립 플레이 횟수"), settings.runs, x => settings.runs = x);
            Int(setupScroll, T("Seed", "시드"), settings.seed, x => settings.seed = x);
            Int(setupScroll, T("Analysis day limit (does not change game duration)", "분석 최대 일차 (게임 기간을 변경하지 않음)"), settings.horizonDays, x => settings.horizonDays = x);
            Int(setupScroll, T("Steps per path", "경로별 최대 입력 수"), settings.maxSteps, x => settings.maxSteps = x);
            Int(setupScroll, T("Retained state records", "보관할 상태 기록 수"), settings.maxStates, x => settings.maxStates = x);
            Int(setupScroll, T("Transitions", "최대 전이 수"), settings.maxTransitions, x => settings.maxTransitions = x);
            Int(setupScroll, T("Work time (seconds)", "작업 시간 (초)"), settings.maxSeconds, x => settings.maxSeconds = x);
            Int(setupScroll, T("Retained memory budget (MiB)", "보관 메모리 예산 (MiB)"), settings.maxMemoryMiB, x => settings.maxMemoryMiB = x);
            Int(setupScroll, T("Checkpoint budget (MiB)", "체크포인트 예산 (MiB)"), settings.maxCheckpointMiB, x => settings.maxCheckpointMiB = x);
            var rare = new DoubleField(T("Rare run-rate threshold (0–1)", "희귀 발생 비율 기준 (0–1)")) { value = settings.rareRunRate }; rare.RegisterValueChangedCallback(e => settings.rareRunRate = e.newValue); setupScroll.Add(rare);
            var frequent = new DoubleField(T("Frequent run-rate threshold (0–1)", "빈번한 발생 비율 기준 (0–1)")) { value = settings.frequentRunRate }; frequent.RegisterValueChangedCallback(e => settings.frequentRunRate = e.newValue); setupScroll.Add(frequent);
            var weighted = new Toggle(T("Use authored weights (missing ID = 1)", "지정 가중치 사용 (빠진 ID는 1)")) { value = settings.policy == "weighted-v1" }; weighted.RegisterValueChangedCallback(e => settings.policy = e.newValue ? "weighted-v1" : "uniform-v1"); setupScroll.Add(weighted);
            Text(setupScroll, T("Weights: one ID=weight per line", "가중치: 줄마다 ID=숫자"), weightsText, x => weightsText = x, true);
            Text(setupScroll, T("Death ending IDs (comma separated)", "사망으로 분류할 엔딩 ID (쉼표 구분)"), string.Join(",", settings.deathEndingIds), x => settings.deathEndingIds = Split(x));
            Text(setupScroll, T("Other failure ending IDs", "기타 실패로 분류할 엔딩 ID"), string.Join(",", settings.failureEndingIds), x => settings.failureEndingIds = Split(x));
            Text(setupScroll, T("Initial StateData JSON (empty = actual new game)", "초기 StateData JSON (비우면 실제 새 게임)"), initialJson, x => initialJson = x, true);
            var external = new Toggle(T("Allow registered external headless code (not sandboxed)", "등록한 외부 헤드리스 코드 실행 허용 (샌드박스 아님)")) { value = settings.allowExternalCode }; external.RegisterValueChangedCallback(e => settings.allowExternalCode = e.newValue); setupScroll.Add(external);
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.flexWrap = Wrap.Wrap; rootVisualElement.Add(actions);
            if (endingDraft == null) Button(actions, T("Start new analysis", "새 분석 시작"), () => {
                settings.weights = ParseWeights(weightsText);
                var job = SimulationJobs.Start(asset, settings, string.IsNullOrWhiteSpace(initialJson) ? null : JsonUtility.FromJson<StateData>(initialJson)); jobId = job.Id; setup.value = false; pathRecord = -1; Refresh();
            });
            Button(actions, T("Pause", "일시 정지"), () => { Current.Explorer.Pause(); Refresh(); });
            Button(actions, T("Resume", "계속"), () => { if (SimulationJobs.Stale(Current)) throw new InvalidOperationException(T("Content or code changed; start a new analysis.", "콘텐츠나 코드가 바뀌었습니다. 새 분석을 시작하세요.")); Current.Explorer.Resume(); Refresh(); });
            Button(actions, T("Cancel", "취소"), () => { Current.Explorer.Cancel(); Refresh(); });
            Button(actions, T("Save checkpoint", "체크포인트 저장"), () => { SimulationJobs.SaveCheckpoint(Current); message.text = T("Saved; analysis paused. Use Resume to continue.", "저장하고 일시 정지했습니다. 계속 버튼으로 재개하세요."); Refresh(); });
            Button(actions, T("Load checkpoint…", "체크포인트 불러오기…"), CheckpointMenu);
            Button(actions, T("Analyses…", "분석 목록…"), () => {
                var menu = new GenericMenu(); foreach (var job in SimulationJobs.All) menu.AddItem(new GUIContent(job.Explorer.Report.manifest.projectId + " · " + job.Explorer.Report.manifest.settings.policy + " · " + job.Id.Substring(0, 8)), job.Id == jobId,
                    () => { jobId = job.Id; asset = job.Asset; projectField.SetValueWithoutNotify(asset); pathRecord = -1; Refresh(); });
                if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent(T("No analyses", "분석 없음"))); menu.ShowAsContext();
            });
            Button(actions, T("Remove from memory", "메모리에서 닫기"), () => { SimulationJobs.Forget(jobId); jobId = ""; Refresh(); });
            Button(actions, T("Export report…", "보고서 내보내기…"), () => {
                var path = EditorUtility.SaveFilePanel(T("Export simulation report", "분석 보고서 저장"), "", "RaiseArc-Simulation.json", "json");
                if (path.Length > 0) File.WriteAllText(path, JsonUtility.ToJson(Current.Explorer.Report, true));
            });
            Button(actions, "English / 한국어", () => { StudioText.Language = StudioText.Language == "ko" ? "en" : "ko"; CreateGUI(); });
            progress = new Label(); progress.style.whiteSpace = WhiteSpace.Normal; rootVisualElement.Add(progress);
            message = new Label(); message.style.whiteSpace = WhiteSpace.Normal; rootVisualElement.Add(message);
            var tabs = new Toolbar(); rootVisualElement.Add(tabs);
            tabButtons.Clear();
            foreach (var name in new[] { "Overview", "Balance", "Coverage", "Replay" })
            {
                var caption = name == "Overview" ? T(name, "개요") : name == "Balance" ? T(name, "밸런스") : name == "Coverage" ? T(name, "커버리지 / 문제") : T(name, "경로 재생");
                var button = new ToolbarButton(() => { tab = name; Refresh(); }) { text = caption }; tabButtons[name] = button; tabs.Add(button);
            }
            tabs.Add(new Label(T("Search", "검색")));
            var search = new ToolbarSearchField { value = query }; search.RegisterValueChangedCallback(e => { query = e.newValue; Refresh(); }); tabs.Add(search);
            body = new ScrollView(); body.style.flexGrow = 1; rootVisualElement.Add(body);
            refreshTimer = rootVisualElement.schedule.Execute(() => { if (jobsAvailable && (Current.Explorer.Report.status != renderedStatus || Current.Explorer.Report.transitions != renderedTransitions || SimulationJobs.Stale(Current) != renderedStale)) Refresh(); Playback(); Safe(RefreshEndingReplay); }).Every(500);
            Refresh(); StudioText.ApplyFont(rootVisualElement);
        }
        private bool jobsAvailable => SimulationJobs.All.Any(j => j.Id == jobId);
        private void OnDisable() { refreshTimer?.Pause(); playing = false; Undo.undoRedoPerformed -= ReloadEndingAfterUndo; }
        private SimulationJob Current => SimulationJobs.Find(jobId);
        private void Refresh()
        {
            if (body == null) return; var offset = body.scrollOffset; body.Clear();
            if (!jobsAvailable) { Line(T("Choose a GameProjectAsset, then Start new analysis. Existing graphs and saves are not edited.", "GameProjectAsset을 선택하고 새 분석을 시작하세요. 원본 그래프와 게임 저장 데이터는 바뀌지 않습니다.")); return; }
            var report = Current.Explorer.Report; var m = report.manifest;
            foreach (var entry in tabButtons) entry.Value.EnableInClassList("simulation-tab-active", entry.Key == tab);
            renderedStatus = report.status; renderedTransitions = report.transitions;
            renderedStale = SimulationJobs.Stale(Current);
            progress.text = $"{Status(report.status)} · {m.settings.mode} · {report.transitions} " + T("transitions", "전이") + $" · {T("work", "작업")} {report.elapsedSeconds:F1}s · " + (renderedStale ? T("STALE: content/code changed", "오래된 결과: 콘텐츠/코드 변경됨") : m.projectId);
            if (tab == "Overview") Overview(report);
            else if (tab == "Balance") Balance(report);
            else if (tab == "Coverage") Coverage(report);
            else Replay();
            body.scrollOffset = offset;
        }
        private void Overview(ExplorationReport report)
        {
            var m = report.manifest;
            if (m.settings.endingGoal) Line(T("Ending goal verdict", "엔딩 목표 판정") + ": " + EndingTests.Verdict(Current), true);
            Line(T("Verification completeness", "검증 완전성") + ": " + (m.completeWithinScope ? T("Supported state graph exhausted within recorded scope and provider contracts", "기록된 범위와 제공자 계약에 따라 지원되는 상태 그래프 탐색 완료") : T("Not established — unseen does not mean unreachable", "확정하지 않음 — 미발견은 도달 불가가 아님")), true);
            Line($"{T("Content revision", "콘텐츠 리비전")}: {m.revision} · {m.contentFingerprint.Substring(0, Math.Min(16, m.contentFingerprint.Length))}");
            Line(T("Start day / money", "시작 일차 / 소지금") + $": {m.initial.day} / {m.initial.money} · {T("Policy", "정책")}: {m.settings.policy} · seed {m.settings.seed}");
            Line(T("Saved game duration / analysis day limit", "저장된 게임 기간 / 분석 최대 일차") + $": {m.gameDurationDays} / {m.settings.horizonDays}");
            if (m.gameDurationDays > m.settings.horizonDays) Line(T("Analysis stops before the game's ending evaluation day. Budget-stopped plays are not failed endings.", "분석이 게임 종료 평가일보다 먼저 멈춥니다. 기간 제한으로 중단된 플레이는 엔딩 실패가 아닙니다."), true);
            Line(T("Initial state source", "초기 상태 출처") + ": " + m.initialOrigin + (string.IsNullOrEmpty(m.initial.pendingEventId) ? "" : " · " + m.initial.pendingEventId + "/" + m.initial.presentationStepId));
            Line(T("Stop reason", "중단/완료 이유") + ": " + m.stopReason);
            if (m.settings.mode == ExplorationMode.MonteCarlo) Line($"{T("Started / finished / running", "시작 / 종료 / 실행 중")}: {report.startedRuns} / {report.plays.Count} / {report.runningRuns}");
            else Line($"{T("Unique states / transitions / frontier", "고유 상태 / 전이 / 남은 탐색 상태")}: {report.states} / {report.transitions} / {report.frontier}");
            var metrics = new VisualElement(); metrics.AddToClassList("simulation-metrics"); body.Add(metrics);
            foreach (var kind in new[] { "EventDefinition", "EndingDefinition", "ChoiceDefinition", "ActivityDefinition" })
            {
                var rows = report.coverage.Where(r => r.kind == kind).ToList();
                var card = new VisualElement(); card.AddToClassList("simulation-metric"); metrics.Add(card);
                card.Add(new Label(Kind(kind)));
                var count = new Label($"{rows.Count(r => r.status == ReachStatus.Reached)} / {rows.Count}"); count.AddToClassList("simulation-metric-count"); card.Add(count);
                card.Add(new Label(T("actual / registered", "실제 도달 / 등록됨")));
                card.Add(new Label($"+ {rows.Count(r => r.status == ReachStatus.ModelOnly)} {T("model only", "모델에서만 도달")}"));
            }
            foreach (var assumption in m.assumptions) Line("[" + T("MODEL", "모델 가정") + "] " + assumption);
            foreach (var limitation in m.limitations) Line("[" + T("LIMIT", "제한") + "] " + limitation);
            foreach (var problem in report.problems) Line("[" + T("OBSERVED PROBLEM", "관찰된 문제") + "] " + problem);
            foreach (var comparison in report.modelComparisons) Line($"{(comparison.matches ? "MATCH" : "MISMATCH")} · {comparison.moduleId} · {comparison.source} · {comparison.observationId} · " + T("caller-reported external observation; not independently verified", "호출자가 제출한 외부 관찰이며 독립적으로 확인한 실행은 아님"));
        }
        private void Balance(ExplorationReport report)
        {
            if (report.manifest.settings.mode != ExplorationMode.MonteCarlo) { Line(T("State exploration is not a play-frequency or probability dataset. Start Monte Carlo to see balance samples.", "상태 탐색 수는 플레이 빈도나 확률이 아닙니다. 반복 플레이 모드를 실행하면 표본 통계를 볼 수 있습니다.")); return; }
            Line(T("Simulated policy samples, not human-player probabilities. Denominator: all started runs, including incomplete runs.", "지정 정책의 모의 플레이 표본이며 실제 플레이어 확률이 아닙니다. 분모는 미완료를 포함한 시작 실행 전체입니다."), true);
            foreach (PlayResult result in Enum.GetValues(typeof(PlayResult)))
            {
                var count = result == PlayResult.Running ? report.runningRuns : report.plays.Count(p => p.result == result);
                Line($"{Outcome(result)}: {count}/{report.startedRuns} ({Rate(count, report.startedRuns)})");
            }
            foreach (var ending in report.plays.Where(p => !string.IsNullOrEmpty(p.endingId)).GroupBy(p => p.endingId))
                Line($"{ending.Key}: {ending.Count()}/{report.startedRuns} · {T("mean terminal day", "평균 종료 일차")} {ending.Average(p => p.day):F1}");
            foreach (var row in report.coverage.Where(r => r.kind == "EventDefinition" || r.kind == "ActivityDefinition" || r.kind == "ChoiceDefinition"))
                Line($"{row.id}: {row.runsReached}/{report.startedRuns} {T("runs", "실행")} · {row.occurrences} {T("occurrences", "발생")} · {row.opportunities} {T("available input opportunities", "선택 가능 기회")} · {T("spent/earned", "지출/수입")} {row.moneySpent}/{row.moneyEarned} · {T("transaction net money", "연결 효과 포함 순소지금 변화")} {row.transactionMoneyDelta}");
            Line(T("State samples: last observed state per run/day, reached population only; missing/early-ended runs are not zero-filled.", "상태 표본: 실행별 각 일차의 마지막 관찰 상태입니다. 해당 일차에 도달한 표본만 사용하며 조기 종료를 0으로 채우지 않습니다."), true);
            foreach (var day in report.samples.GroupBy(s => s.day).OrderBy(g => g.Key).Take(50))
            {
                var money = day.Select(s => s.money).OrderBy(n => n).ToArray();
                Line($"Day {day.Key} · n={money.Length} · {T("money min / median / mean / max", "소지금 최소 / 중앙 / 평균 / 최대")}: {money.First()} / {Median(money):F1} / {money.Average():F1} / {money.Last()}");
                foreach (var stat in day.SelectMany(s => s.stats).GroupBy(s => s.id))
                {
                    var values = stat.Select(s => s.value).OrderBy(v => v).ToArray();
                    Line($"  {stat.Key} · n={values.Length} · min/median/mean/max {values.First()}/{Median(values):F1}/{values.Average():F1}/{values.Last()}");
                }
            }
            foreach (var age in report.samples.GroupBy(s => s.age).OrderBy(g => g.Key))
                Line($"{T("Age reached", "도달 나이")} {age.Key} · n={age.Select(s => s.run).Distinct().Count()}/{report.startedRuns}");
            var others = SimulationJobs.All.Where(j => j.Id != jobId && j.Explorer.Report.manifest.settings.mode == ExplorationMode.MonteCarlo).ToList();
            if (others.Count > 0)
            {
                var select = new DropdownField(T("Compare separate policy dataset", "별도 정책 결과 비교"), others.Select(j => j.Id).ToList(), Math.Max(0, others.FindIndex(j => j.Id == compareId)));
                select.RegisterValueChangedCallback(e => { compareId = e.newValue; Refresh(); }); body.Add(select);
                var other = others.Find(j => j.Id == select.value).Explorer.Report;
                if (!ComparableScope(report.manifest, other.manifest)) Line(T("Different content, code, providers, initial state or analysis limits — comparison withheld.", "콘텐츠·코드·제공자·초기 상태 또는 분석 제한이 달라 비교하지 않습니다."));
                else { Line($"{other.manifest.settings.policy} · n={other.startedRuns} · {T("kept separate; no pooled rate", "별도 분모 유지, 통합 비율 없음")}"); foreach (var g in other.plays.GroupBy(p => p.result)) Line($"{Outcome(g.Key)}: {g.Count()}/{other.startedRuns}"); }
            }
        }
        private static bool ComparableScope(AnalysisManifest a, AnalysisManifest b)
        {
            string Limits(ExplorationSettings settings)
            {
                var copy = JsonUtility.FromJson<ExplorationSettings>(JsonUtility.ToJson(settings));
                copy.policy = ""; copy.seed = 0; copy.runs = 0; copy.weights.Clear();
                return JsonUtility.ToJson(copy);
            }
            return a.contentFingerprint == b.contentFingerprint && a.runtimeFingerprint == b.runtimeFingerprint &&
                a.adapterFingerprint == b.adapterFingerprint && JsonUtility.ToJson(a.initial) == JsonUtility.ToJson(b.initial) &&
                Limits(a.settings) == Limits(b.settings);
        }
        private void Coverage(ExplorationReport report)
        {
            Line(T("Search by ID, type or status. Every count refers to the fixed registered catalog, including unsupported items.", "ID·유형·상태로 검색합니다. 미지원 항목도 고정된 등록 목록에 남습니다."));
            var filters = new List<string> { "All", "Unseen", "Unverified", "Model", "Rare", "Frequent", "Priority loss", "Branches" };
            var filterLabels = StudioText.Language == "ko" ? new List<string> { "전체", "미발견", "미검증", "모델", "희귀", "빈번함", "우선순위 밀림", "분기 연결" } : filters;
            var filter = new DropdownField(T("Filter (frequency requires Monte Carlo)", "필터 (빈도는 반복 플레이에서만)"), filterLabels, Math.Max(0, filters.IndexOf(coverageFilter)));
            filter.RegisterValueChangedCallback(e => { coverageFilter = filters[filterLabels.IndexOf(e.newValue)]; Refresh(); }); body.Add(filter);
            if (coverageFilter == "Branches")
            {
                foreach (var branch in report.branches.Where(b => string.IsNullOrEmpty(query) || (b.eventId + " " + b.sourceId + " " + b.targetId).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).Take(100))
                {
                    Line($"{Reach(branch.status)} · {branch.eventId}: {branch.sourceId} [{branch.port}] → {branch.targetId} · {branch.occurrences} " + T("traversals", "통과"));
                    if (branch.firstRecord >= 0) Button(body, T("Show branch path", "분기 경로 보기"), () => { pathRecord = branch.firstRecord; replayStep = 0; tab = "Replay"; Refresh(); });
                }
                return;
            }
            bool Include(CoverageRow r)
            {
                var mc = report.manifest.settings.mode == ExplorationMode.MonteCarlo && report.startedRuns > 0;
                var rate = report.startedRuns == 0 ? 0 : (double)r.runsReached / report.startedRuns;
                switch (coverageFilter)
                {
                    case "Unseen": return r.occurrences == 0;
                    case "Unverified": return r.status == ReachStatus.Unverified;
                    case "Model": return r.status == ReachStatus.ModelOnly;
                    case "Rare": return mc && r.kind == "EventDefinition" && rate <= report.manifest.settings.rareRunRate;
                    case "Frequent": return mc && r.kind == "EventDefinition" && rate >= report.manifest.settings.frequentRunRate;
                    case "Priority loss": return r.priorityLosses > 0;
                    default: return true;
                }
            }
            var rows = report.coverage.Where(Include).Where(r => string.IsNullOrWhiteSpace(query) || (r.id + " " + Kind(r.kind) + " " + Reach(r.status)).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).Take(100);
            foreach (var row in rows)
            {
                var box = new VisualElement(); box.style.marginBottom = 8; body.Add(box);
                var label = new Label($"{Reach(row.status)} · {Kind(row.kind)} · {row.id}"); label.style.unityFontStyleAndWeight = FontStyle.Bold; box.Add(label);
                var info = $"{T("Observed", "관찰")}: {row.occurrences}";
                if (row.kind == "ActivityDefinition" || row.kind == "ChoiceDefinition") info += $" · {T("available input opportunities", "선택 가능 기회")}: {row.opportunities}";
                if (row.kind == "Condition") info += $" · {T("true / false / not evaluated", "충족 / 미충족 / 평가 안 됨")}: {row.trueCount}/{row.falseCount}/{row.unevaluatedCount}";
                if (row.kind == "Effect") info += $" · {T("attempted / committed", "시도 / 확정")}: {row.attempted}/{row.committed}";
                if (row.kind == "EndingDefinition" || row.kind == "EventDefinition") info += $" · {T("eligible / selected", "조건 충족 / 선택")}: {row.eligible}/{row.selected}";
                if (row.kind == "EndingDefinition") info += $" · {T("observed priority losses", "관찰된 우선순위 밀림")}: {row.priorityLosses}";
                var details = new Label(info + "\n" + Reason(row.reason)); details.style.whiteSpace = WhiteSpace.Normal; box.Add(details);
                var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; box.Add(actions);
                Button(actions, T("Open source", "원본 보기"), () => SimulationJobs.Navigate(Current, row.id));
                if (row.firstRecord >= 0) Button(actions, T("Show path", "경로 보기"), () => { pathRecord = row.firstRecord; replayStep = 0; tab = "Replay"; Refresh(); });
                Button(actions, T("Search this target", "이 목표 탐색"), () => { var s = JsonUtility.FromJson<ExplorationSettings>(JsonUtility.ToJson(report.manifest.settings)); s.mode = ExplorationMode.TargetSearch; s.targetId = row.id; asset = Current.Asset; jobId = SimulationJobs.Start(asset, s, report.manifest.initial).Id; tab = "Overview"; Refresh(); });
            }
            Line(T("Showing up to 100 matches. Narrow the search for more.", "최대 100개를 표시합니다. 검색어를 좁히면 다른 항목을 볼 수 있습니다."));
        }
        private void Replay()
        {
            var explorer = Current.Explorer;
            if (pathRecord < 0) pathRecord = explorer.Report.targetRecord;
            if (pathRecord < 0) { Line(T("Choose Show path in Coverage. These paths start from the recorded game state, not an injected node.", "커버리지에서 경로 보기를 선택하세요. 경로는 특정 노드에 주입한 상태가 아니라 기록된 게임 초기 상태에서 출발합니다.")); return; }
            var path = explorer.Path(pathRecord); replayStep = Math.Min(replayStep, path.Count - 1);
            var controls = new VisualElement(); controls.style.flexDirection = FlexDirection.Row; body.Add(controls);
            Button(controls, T("Previous", "이전"), () => { replayStep = Math.Max(0, replayStep - 1); Refresh(); });
            Button(controls, T("Next", "다음"), () => { replayStep = Math.Min(path.Count - 1, replayStep + 1); ShowFrame(path[replayStep]); Refresh(); });
            Button(controls, playing ? T("Stop", "정지") : T("Play overlay", "그래프에서 재생"), () => playing = !playing);
            Button(controls, T("Verify replay", "경로 재실행 검증"), () => { if (SimulationJobs.Stale(Current)) throw new InvalidOperationException(T("Stale result cannot be verified.", "오래된 결과는 검증할 수 없습니다.")); var verification = explorer.VerifyPath(pathRecord); message.text = (verification.matches ? "PASS · " : "FAIL · ") + verification.checkedInputs + " · " + verification.detail; });
            var speed = new FloatField(T("Seconds per step", "단계 간격 (초)")) { value = replaySeconds }; speed.RegisterValueChangedCallback(e => replaySeconds = Mathf.Clamp(e.newValue, .1f, 10)); body.Add(speed);
            var frame = path[replayStep];
            Line($"{replayStep}/{path.Count - 1} · {frame.input?.kind ?? "Start"} {frame.input?.id} · {T("day", "일차")} {frame.state.day} · {T("money", "소지금")} {frame.state.money}", true);
            if (!string.IsNullOrEmpty(frame.input?.moduleId)) Line($"{frame.input.moduleId} · {T("Module outcome", "모듈 결과")}: {frame.input.outcomeId} · seed {frame.input.randomSeed}");
            if (replayStep > 0) Line($"{T("Before → after", "변경 전 → 후")}: Day {path[replayStep - 1].state.day} → {frame.state.day}; Money {path[replayStep - 1].state.money} → {frame.state.money}");
            foreach (var value in frame.state.stats) Line($"{value.id}: {value.value}");
            foreach (var value in frame.state.recordAttempts)
                Line(value.id + " · " + T("best / total / attempts / passes", "최고 / 합계 / 시도 / 통과") + ": " +
                    frame.state.recordBest.Find(x => x.id == value.id)?.value + " / " + frame.state.recordTotal.Find(x => x.id == value.id)?.value + " / " +
                    value.value + " / " + frame.state.recordPasses.Find(x => x.id == value.id)?.value);
            foreach (var value in frame.state.flags.Where(x => x.value == 1)) Line(T("Acquired flag", "획득한 플래그") + ": " + value.id);
            foreach (var value in frame.state.modifierExpiry) Line(value.id + " · " + Math.Max(0, value.value - frame.state.day) + T(" game days remaining", "일 남음"));
            foreach (var delta in frame.observations.Where(o => o.hasDelta))
                Line($"{delta.target}: {delta.before} → {delta.after} · {T("requested / applied", "요청 / 적용")} {delta.requestedDelta} / {delta.appliedDelta} · {delta.id}" +
                    (delta.committed ? "" : " · " + T("not committed", "반영되지 않음")));
            foreach (var observation in frame.observations)
            {
                var applied = observation.kind == "Condition" || observation.kind.EndsWith("Eligibility", StringComparison.Ordinal) ? "" : $" · {T("committed", "확정")}: {observation.committed}";
                Line($"{observation.kind} · {observation.id} · {observation.outcome}" + (observation.hasActual ? $" · {observation.target}: {observation.actual} {observation.comparison} {observation.expected}" : "") + applied);
                if (observation.kind == "Branch") Button(body, T("Show this branch", "이 분기 보기"), () => SimulationJobs.Navigate(Current, observation.id, frame, observation));
            }
            foreach (var a in frame.assumptions) Line("[MODEL] " + a);
            if (frame.reason.Length > 0) Line(frame.reason);
            Button(body, T("Locate current graph node", "현재 그래프 노드 보기"), () => ShowFrame(frame));
            Line(T("Recorded deterministic inputs and actual before/after states. Model segments remain labelled; this does not run an external Scene.", "실제 입력과 변경 전후 상태를 기록한 경로입니다. 모델 구간은 가정으로 표시하며 외부 Scene을 실행하지 않습니다."));
        }
        private void Playback()
        {
            if (!playing || !jobsAvailable || pathRecord < 0 || EditorApplication.timeSinceStartup < nextReplay) return;
            var path = Current.Explorer.Path(pathRecord);
            if (++replayStep >= path.Count) { playing = false; replayStep = path.Count - 1; return; }
            Safe(() => { ShowFrame(path[replayStep]); Refresh(); }); nextReplay = EditorApplication.timeSinceStartup + replaySeconds;
        }
        private void ShowFrame(SimulationRecord frame) => SimulationJobs.Navigate(Current, !string.IsNullOrEmpty(frame.nodeId) ? frame.nodeId : !string.IsNullOrEmpty(frame.eventId) ? frame.eventId : frame.input?.id ?? "", frame);
        private void CheckpointMenu()
        {
            var menu = new GenericMenu(); foreach (var id in SimulationJobs.SavedCheckpointIds()) menu.AddItem(new GUIContent(id), false, () => Safe(() => { jobId = SimulationJobs.LoadCheckpoint(asset, id).Id; Refresh(); }));
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent(T("No saved checkpoints", "저장된 체크포인트 없음"))); menu.ShowAsContext();
        }
        private void Line(string text, bool heading = false) { var label = new Label(text); label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 6; if (heading) label.style.unityFontStyleAndWeight = FontStyle.Bold; body.Add(label); }
        private void Button(VisualElement parent, string label, Action action) => parent.Add(new Button(() => Safe(action)) { text = label });
        private void Safe(Action action) { try { action(); } catch (Exception ex) { playing = false; message.text = ex.Message; } }
        private static void Int(VisualElement parent, string label, int value, Action<int> changed) { var f = new IntegerField(label) { value = value }; f.RegisterValueChangedCallback(e => changed(e.newValue)); parent.Add(f); }
        private static void Text(VisualElement parent, string label, string value, Action<string> changed, bool multiline = false) { var f = new TextField(label) { value = value, multiline = multiline }; f.RegisterValueChangedCallback(e => changed(e.newValue)); parent.Add(f); }
        private static List<string> Split(string s) => s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        private static List<InputWeight> ParseWeights(string text) => text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(line => { var p = line.Split('='); if (p.Length != 2) throw new ArgumentException("Weight format: ID=number"); return new InputWeight { id = p[0].Trim(), weight = double.Parse(p[1], CultureInfo.InvariantCulture) }; }).ToList();
        private static double Median(int[] n) => n.Length % 2 == 1 ? n[n.Length / 2] : ((double)n[n.Length / 2 - 1] + n[n.Length / 2]) / 2;
        private static string Rate(int n, int total) => total == 0 ? "—" : (100.0 * n / total).ToString("F1") + "%";
        private static string Status(ExplorationStatus s) => T(s.ToString(), new[] { "실행 중", "일시 정지", "작업 완료", "취소됨", "예산 중단" }[(int)s]);
        private static string Reach(ReachStatus s) => T(s.ToString(), new[] { "미발견", "실제 도달", "모델에서만 도달", "명시된 범위 내 도달 불가", "미검증", "범위 밖" }[(int)s]);
        private static string Outcome(PlayResult s) => T(s.ToString(), new[] { "실행 중", "정상 엔딩", "사망", "기타 실패", "진행 불가", "런타임 오류", "미지원 경계", "예산/기간 중단", "취소" }[(int)s]);
        private static string Kind(string k) => k == "EventDefinition" ? T("Events", "이벤트") : k == "EndingDefinition" ? T("Endings", "엔딩") : k == "ChoiceDefinition" ? T("Choices", "선택지") : k == "ActivityDefinition" ? T("Activities", "활동") : k;
        private static string Reason(string reason)
        {
            if (StudioText.Language != "ko") return reason;
            switch (reason)
            {
                case "Observed from the recorded initial state": return "기록된 초기 상태에서 실제 입력을 거쳐 관찰했습니다.";
                case "Not observed in this analysis": return "이번 분석에서 아직 관찰하지 못했습니다.";
                case "No visit in fully exhausted supported state graph for this initial state": return "이 초기 상태의 지원되는 상태 그래프를 계약 범위 내에서 모두 탐색했지만 도달하지 못했습니다.";
                case "An opaque boundary may hide paths to this content": return "미지원 경계 너머에 이 콘텐츠로 가는 경로가 있을 수 있습니다.";
                case "Presentation resolution is outside this input runner": return "외형·연출 선택은 이 플레이 입력 분석의 범위 밖입니다.";
                default: return reason;
            }
        }
    }
}
