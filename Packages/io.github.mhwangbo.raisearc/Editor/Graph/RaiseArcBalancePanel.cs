using System;
using System.Collections.Generic;
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
    public sealed partial class SimulationWindow
    {
        [SerializeField] private bool balanceMode;
        [SerializeField] private RaiseArcBalanceTestAsset savedBalance;
        [SerializeField] private string balanceJobId = "", balanceResultId = "", balancePreviousId = "";
        private BalanceTestDefinition balanceDraft;
        private GameProjectAsset balanceCandidate;
        private int balanceRevision, balancePolicy, balanceRun, balancePage;
        [NonSerialized] private List<SimulationRecord> balancePath;
        [NonSerialized] private BalanceArtifact balancePathResult;
        private string balanceReplayText = "";
        private int balanceReplaySelection;
        private Button balanceExportSingle;
        private RaiseArcTestSuiteAsset balanceSuite;
        private string suiteJobId = "";
        [NonSerialized] private BalanceResultComparison balanceComparison;
        private ScrollView balanceReport;
        private Label balanceMessage;
        private TextField balanceResultField;
        private int balanceRenderedTransitions = -1;
        private ExplorationStatus balanceRenderedStatus;
        private readonly Dictionary<string, string> balanceWeightEdits = new Dictionary<string, string>();

        [InitializeOnLoadMethod] private static void ConnectBalanceTests()
        {
            BalanceTests.OpenRequested += test => {
                Open(); var w = GetWindow<SimulationWindow>(); w.balanceMode = true; w.savedBalance = test; w.asset = test.Project;
                w.balanceDraft = null; w.balanceJobId = ""; w.ClearBalanceSelection(); w.balanceWeightEdits.Clear(); w.CreateGUI();
            };
        }
        private void BalanceAction(Action action)
        {
            try { action(); } catch (Exception e) { if (balanceMessage != null) balanceMessage.text = e.Message; }
        }
        private void BalanceButton(VisualElement parent, string en, string ko, Action action) => parent.Add(new Button(() => BalanceAction(action)) { text = T(en, ko) });
        private BalanceJob BalanceCurrent => BalanceTests.Find(balanceJobId);
        private void ClearBalanceSelection()
        {
            balancePath = null; balancePathResult = null; ClearBalanceReplay(); balanceComparison = null; balancePage = 0;
            balanceResultId = ""; balanceResultField?.SetValueWithoutNotify("");
            if (balanceMessage != null) balanceMessage.text = "";
        }
        private void ClearBalanceReplay() { balanceReplaySelection++; balanceReplayText = ""; }
        private void CompleteBalanceReplay(int request, string feedback)
        {
            if (request != balanceReplaySelection) return;
            balanceReplayText = feedback; RenderBalance();
        }
        private void SelectBalanceRun(BalanceJob job, int policy, int run)
        {
            if (job.Id != balanceJobId) return;
            var id = string.IsNullOrEmpty(balanceResultId) ? BalanceTests.Preserve(job) : balanceResultId;
            var result = BalanceTests.ReadResult(id); var path = BalanceTests.RunPath(result, policy, run);
            balanceResultId = id; balanceResultField?.SetValueWithoutNotify(id);
            balancePolicy = policy; balanceRun = run; balancePathResult = result; balancePath = path;
            ClearBalanceReplay(); RenderBalance();
        }
        private void OpenBalanceComparison(string path)
        {
            var comparison = BalanceComparisonFile.Open(path);
            ClearBalanceSelection(); balanceComparison = comparison; RenderBalance();
        }
        private void DrawBalanceWorkspace()
        {
            var setup = new Foldout { text = T("Play many times under these conditions", "이 조건으로 여러 번 플레이"), value = true };
            setup.style.maxHeight = 370; var form = new ScrollView(); setup.Add(form); rootVisualElement.Add(setup);
            var saved = new ObjectField(T("Saved balance test", "저장된 밸런스 테스트")) { objectType = typeof(RaiseArcBalanceTestAsset), value = savedBalance };
            saved.RegisterValueChangedCallback(e => { savedBalance = e.newValue as RaiseArcBalanceTestAsset; balanceDraft = null; balanceJobId = ""; ClearBalanceSelection(); balanceWeightEdits.Clear(); if (savedBalance != null) asset = savedBalance.Project; CreateGUI(); }); form.Add(saved);
            BalanceButton(form, "New balance test", "새 밸런스 테스트", () => {
                if (asset == null) throw new ArgumentException(T("Select a game project.", "게임 프로젝트를 선택하세요."));
                savedBalance = null; balanceDraft = BalanceTests.New(asset); balanceRevision = 0; balanceJobId = ""; ClearBalanceSelection(); balanceWeightEdits.Clear(); CreateGUI();
            });
            if (balanceDraft == null && savedBalance != null) { balanceDraft = savedBalance.Read(); balanceRevision = savedBalance.Revision; asset = savedBalance.Project; }
            balanceMessage = new Label(); balanceMessage.style.whiteSpace = WhiteSpace.Normal; rootVisualElement.Add(balanceMessage);
            BalanceButton(rootVisualElement, "Open comparison file…", "비교 파일 열기…", () => {
                var path = EditorUtility.OpenFilePanel(T("Open comparison file", "비교 파일 열기"), "", "json");
                if (path.Length > 0) OpenBalanceComparison(path);
            });
            if (balanceDraft == null) { balanceReport = new ScrollView(); balanceReport.style.flexGrow = 1; rootVisualElement.Add(balanceReport); RenderBalance(); return; }
            var d = balanceDraft;
            form.Add(new Label($"{d.id} · test revision {balanceRevision} · {AssetDatabase.GetAssetPath(asset)}"));
            Text(form, T("Test name", "테스트 이름"), d.name, x => d.name = x);
            var start = new Foldout { text = T("Starting conditions and action restrictions", "시작 조건과 행동 제한") }; form.Add(start);
            var money = new Toggle(T("Override starting money (analysis only)", "분석에서만 시작 돈 재정의")) { value = d.overrideMoney }; money.RegisterValueChangedCallback(e => d.overrideMoney = e.newValue); start.Add(money);
            Int(start, T("Starting money", "시작 돈"), d.money, x => d.money = x);
            foreach (var stat in asset.Read().stats)
            {
                var entry = d.stats.Find(x => x.id == stat.id); var id = stat.id;
                var enabled = new Toggle(T("Override stat", "능력치 재정의") + " · " + id) { value = entry != null };
                var field = new IntegerField(id) { value = entry?.value ?? stat.initial }; field.SetEnabled(entry != null);
                enabled.RegisterValueChangedCallback(e => { d.stats.RemoveAll(x => x.id == id); if (e.newValue) d.stats.Add(new IntEntry { id = id, value = field.value }); field.SetEnabled(e.newValue); });
                field.RegisterValueChangedCallback(e => { var v = d.stats.Find(x => x.id == id); if (v != null) v.value = e.newValue; }); start.Add(enabled); start.Add(field);
            }
            Text(start, T("Allowed activity IDs (empty = all)", "허용 활동 ID (비우면 전체)"), string.Join(",", d.settings.allowedActivities), x => d.settings.allowedActivities = Split(x));
            Text(start, T("Forbidden activity IDs", "금지 활동 ID"), string.Join(",", d.settings.forbiddenActivities), x => d.settings.forbiddenActivities = Split(x));
            foreach (var policy in d.policies.ToArray())
            {
                var p = new Foldout { text = policy.name + " · " + policy.id, value = true }; form.Add(p);
                Text(p, T("Policy name", "정책 이름"), policy.name, x => policy.name = x);
                var kinds = new List<string> { "uniform-v1", "weighted-v1", "income-first-v1" };
                var kind = new DropdownField(T("Play policy", "플레이 정책"), kinds, kinds.IndexOf(policy.kind)); kind.RegisterValueChangedCallback(e => { policy.kind = e.newValue; }); p.Add(kind);
                Int(p, T("Run count (fixed sample)", "실행 수 (고정 표본)"), policy.runs, x => policy.runs = x);
                var weightText = balanceWeightEdits.TryGetValue(policy.id, out var pendingWeights) ? pendingWeights : string.Join("\n", policy.weights.Select(w => w.id + "=" + w.weight.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                Text(p, T("Weights · ID=number, one per line", "가중치 · 줄마다 ID=숫자"), weightText, x => balanceWeightEdits[policy.id] = x, true);
                Text(p, T("Income-first activity ID", "수입 우선 활동 ID"), policy.incomeActivityId, x => policy.incomeActivityId = x);
                Int(p, T("Prefer income below this money", "이 돈 미만이면 수입 우선"), policy.incomeThreshold, x => policy.incomeThreshold = x);
                if (d.policies.Count > 1) BalanceButton(p, "Remove policy", "정책 제거", () => { d.policies.Remove(policy); CreateGUI(); });
            }
            BalanceButton(form, "Add policy", "정책 추가", () => { d.policies.Add(new BalancePolicy { id = "policy-" + Guid.NewGuid().ToString("N").Substring(0, 8), name = "Income first", kind = "income-first-v1" }); CreateGUI(); });
            var metrics = new Foldout { text = T("Report sections (display only)", "보고서 표시 지표 (표시만 변경)") }; form.Add(metrics);
            metrics.Add(new Label(T("All runs and creator criteria remain included; hiding a section does not remove observations.", "모든 실행과 제작자 기준은 유지됩니다. 표시를 꺼도 관측값은 제거되지 않습니다.")));
            foreach (var metricName in new[] { "economy", "growth", "content", "rules", "dates" })
            {
                var metricId = metricName;
                var toggle = new Toggle(metricId) { value = d.metrics.Contains(metricId) };
                toggle.RegisterValueChangedCallback(e => { d.metrics.RemoveAll(x => x == metricId); if (e.newValue) d.metrics.Add(metricId); }); metrics.Add(toggle);
            }
            var criteria = new Foldout { text = T("Optional creator criteria (not report filters)", "선택적 제작자 기준 (보고서 필터와 별개)") }; form.Add(criteria);
            foreach (var c in d.criteria.ToArray())
            {
                criteria.Add(new Label(c.id)); Text(criteria, T("Policy ID", "정책 ID"), c.policyId, x => c.policyId = x);
                var metric = new EnumField(T("Metric", "지표"), c.metric); metric.RegisterValueChangedCallback(e => c.metric = (BalanceMetric)e.newValue); criteria.Add(metric);
                Text(criteria, T("Target ending/stat/event ID", "대상 엔딩/능력치/사건 ID"), c.targetId, x => c.targetId = x);
                Int(criteria, T("Day", "일차"), c.day, x => c.day = x); Int(criteria, T("Stat threshold", "능력치 기준"), c.value, x => c.value = x);
                var compare = new EnumField(T("Comparison", "비교"), c.comparison); compare.RegisterValueChangedCallback(e => c.comparison = (BalanceComparison)e.newValue); criteria.Add(compare);
                var rate = new DoubleField(T("Required rate (0–1)", "기준 비율 (0–1)")) { value = c.rate }; rate.RegisterValueChangedCallback(e => c.rate = e.newValue); criteria.Add(rate);
                BalanceButton(criteria, "Remove criterion", "기준 제거", () => { d.criteria.Remove(c); CreateGUI(); });
            }
            BalanceButton(criteria, "Add criterion", "기준 추가", () => { d.criteria.Add(new BalanceCriterion { id = "criterion-" + Guid.NewGuid().ToString("N").Substring(0, 8), policyId = d.policies[0].id }); CreateGUI(); });
            var advanced = new Foldout { text = T("Advanced · seeds, budgets, statistics", "고급 · 시드, 예산, 통계") }; form.Add(advanced);
            Int(advanced, T("Policy RNG seed", "정책 난수 시드"), d.policySeed, x => d.policySeed = x); Int(advanced, T("Game RNG seed", "게임 난수 시드"), d.gameSeed, x => d.gameSeed = x);
            Int(advanced, T("Day horizon", "검사 기간"), d.settings.horizonDays, x => d.settings.horizonDays = x);
            Int(advanced, T("Maximum inputs per run", "run당 최대 입력"), d.settings.maxSteps, x => d.settings.maxSteps = x);
            Int(advanced, T("Total transition budget", "전체 전이 예산"), d.settings.maxTransitions, x => d.settings.maxTransitions = x);
            Int(advanced, T("Work seconds", "작업 시간 초"), d.settings.maxSeconds, x => d.settings.maxSeconds = x);
            Int(advanced, T("Retained memory MiB", "보관 메모리 MiB"), d.settings.maxMemoryMiB, x => d.settings.maxMemoryMiB = x);
            Int(advanced, T("Artifact MiB", "보존 파일 MiB"), d.settings.maxCheckpointMiB, x => d.settings.maxCheckpointMiB = x);
            Text(advanced, T("Explicit death ending IDs", "명시적 사망 엔딩 ID"), string.Join(",", d.settings.deathEndingIds), x => d.settings.deathEndingIds = Split(x));
            advanced.Add(new Label(T("Wilson 95%, fixed independent samples only. Unknown/uncompleted outcomes remain inconclusive. Zero observations do not prove impossibility.", "고정 독립 표본의 Wilson 95%. 미완료·미평가가 있으면 판단 보류. 0회 관측은 불가능의 증명이 아닙니다.")));
            BalanceButton(form, "Save balance test", "밸런스 테스트 저장", () => { foreach (var p in d.policies) if (balanceWeightEdits.TryGetValue(p.id, out var value)) p.weights = ParseWeights(value); savedBalance = BalanceTests.Save(asset, d, savedBalance, balanceRevision); balanceRevision = savedBalance.Revision; balanceWeightEdits.Clear(); balanceMessage.text = "Saved · test revision " + balanceRevision; });
            var candidate = new ObjectField(T("Candidate (empty = test project)", "실행 후보 (비우면 테스트 프로젝트)")) { objectType = typeof(GameProjectAsset), value = balanceCandidate };
            candidate.RegisterValueChangedCallback(e => { balanceCandidate = e.newValue as GameProjectAsset; ClearBalanceReplay(); RenderBalance(); }); form.Add(candidate);
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.flexWrap = Wrap.Wrap; rootVisualElement.Add(actions);
            BalanceButton(actions, "Run saved test", "저장된 테스트 실행", () => { balanceJobId = BalanceTests.Start(savedBalance, balanceCandidate).Id; if (!string.IsNullOrEmpty(balanceResultId)) balancePreviousId = balanceResultId; ClearBalanceSelection(); setup.value = false; RenderBalance(); });
            BalanceButton(actions, "Pause", "일시 정지", () => { BalanceTests.Pause(BalanceCurrent); RenderBalance(); });
            BalanceButton(actions, "Resume", "재개", () => { BalanceTests.Resume(BalanceCurrent); ClearBalanceSelection(); RenderBalance(); });
            BalanceButton(actions, "Cancel", "취소", () => { BalanceTests.Cancel(BalanceCurrent); RenderBalance(); });
            BalanceButton(actions, "Preserve / checkpoint", "결과 보존 / 체크포인트", () => { balanceResultId = BalanceTests.Preserve(BalanceCurrent); balanceResultField?.SetValueWithoutNotify(balanceResultId); balanceMessage.text = balanceResultId + " · " + BalanceTests.ArtifactPath(balanceResultId); RenderBalance(); });
            BalanceButton(actions, "Close report", "보고서 닫기", () => { BalanceTests.Forget(BalanceCurrent); balanceJobId = ""; ClearBalanceSelection(); RenderBalance(); });
            balanceExportSingle = new Button(() => BalanceAction(() => {
                var id = BalanceTests.Preserve(BalanceCurrent); var path = EditorUtility.SaveFilePanel(T("Export single result", "단일 결과 내보내기"), "", "RaiseArc-Balance-" + id, "json");
                if (path.Length > 0) { if (File.Exists(path)) throw new InvalidOperationException("Use a new export filename; existing reports are preserved."); File.Copy(BalanceTests.ArtifactPath(id), path); balanceMessage.text = path; }
            })) { text = T("Export single result…", "단일 결과 내보내기…") }; actions.Add(balanceExportSingle);
            BalanceButton(actions, "English / 한국어", "English / 한국어", () => { StudioText.Language = StudioText.Language == "ko" ? "en" : "ko"; CreateGUI(); });
            var history = new Foldout { text = T("Reopen / compare preserved results", "보존 결과 재오픈 / 비교") }; rootVisualElement.Add(history);
            balanceResultField = new TextField(T("Result ID / checkpoint", "결과 ID / 체크포인트")) { value = balanceResultId };
            history.Add(balanceResultField);
            BalanceButton(history, "Reopen checkpoint", "체크포인트 재오픈", () => { var id = balanceResultField.value; balanceJobId = BalanceTests.Load(balanceCandidate == null ? asset : balanceCandidate, id).Id; ClearBalanceSelection(); balanceResultId = id; balanceResultField.SetValueWithoutNotify(id); RenderBalance(); });
            Text(history, T("Previous result ID", "이전 결과 ID"), balancePreviousId, x => balancePreviousId = x);
            BalanceButton(history, "Compare preserved candidates", "보존한 후보 비교", () => { var comparison = BalanceTests.Compare(balancePreviousId, balanceResultField.value); balancePath = null; balancePathResult = null; ClearBalanceReplay(); balanceMessage.text = ""; balanceComparison = comparison; RenderBalance(); });
            DrawBalanceSuite(history);
            balanceReport = new ScrollView(); balanceReport.style.flexGrow = 1; rootVisualElement.Add(balanceReport);
            refreshTimer = rootVisualElement.schedule.Execute(() => BalanceAction(() => {
                var j = BalanceTests.All.FirstOrDefault(x => x.Id == balanceJobId);
                if (j != null && (j.Explorers.Sum(e => e.Report.transitions) != balanceRenderedTransitions || j.Data.status != balanceRenderedStatus)) RenderBalance();
            })).Every(750);
            RenderBalance();
        }
        private void DrawBalanceSuite(VisualElement parent)
        {
            var suiteField = new ObjectField(T("Saved test suite", "저장된 테스트 묶음")) { objectType = typeof(RaiseArcTestSuiteAsset), value = balanceSuite };
            suiteField.RegisterValueChangedCallback(e => { balanceSuite = e.newValue as RaiseArcTestSuiteAsset; CreateGUI(); }); parent.Add(suiteField);
            if (balanceSuite != null)
            {
                var suiteDraft = balanceSuite.Read(); var revision = balanceSuite.Revision;
                Text(parent, T("Suite name", "묶음 이름"), suiteDraft.name, x => suiteDraft.name = x);
                var entries = BalanceTests.List(asset).Select(t => new TestSuiteEntry { testId = t.Read().id }).Concat(EndingTests.List(asset).Select(t => new TestSuiteEntry { kind = "ending", testId = t.Read().id }));
                foreach (var item in entries)
                {
                    var enabled = new Toggle(item.kind + " · " + item.testId) { value = suiteDraft.entries.Any(e => e.kind == item.kind && e.testId == item.testId) };
                    enabled.RegisterValueChangedCallback(e => { suiteDraft.entries.RemoveAll(x => x.kind == item.kind && x.testId == item.testId); if (e.newValue) suiteDraft.entries.Add(item); }); parent.Add(enabled);
                }
                BalanceButton(parent, "Save suite changes", "묶음 변경 저장", () => { TestSuites.Save(asset, suiteDraft, balanceSuite, revision); balanceMessage.text = "Suite revision " + balanceSuite.Revision; });
            }
            BalanceButton(parent, "Save suite: this test + saved ending tests", "묶음 저장: 이 테스트 + 저장된 엔딩 테스트", () => {
                if (savedBalance == null) throw new InvalidOperationException("Save a balance test first.");
                var suite = new TestSuiteDefinition { id = Guid.NewGuid().ToString("N"), name = balanceDraft.name + " + endings" };
                suite.entries.Add(new TestSuiteEntry { testId = savedBalance.Read().id });
                suite.entries.AddRange(EndingTests.List(asset).Select(t => new TestSuiteEntry { kind = "ending", testId = t.Read().id }));
                balanceSuite = TestSuites.Save(asset, suite, null, 0); CreateGUI(); balanceMessage.text = AssetDatabase.GetAssetPath(balanceSuite);
            });
            BalanceButton(parent, "Run saved suite", "저장된 묶음 실행", () => { suiteJobId = TestSuites.Start(balanceSuite).id; balanceMessage.text = suiteJobId; });
            BalanceButton(parent, "Show suite results", "묶음 결과 보기", () => { var r = TestSuites.Find(suiteJobId, asset); balanceMessage.text = r.execution + "\n" + string.Join("\n", r.entries.Select(e => e.testId + " · " + e.execution + " / " + e.verdict + " " + e.error)); });
            BalanceButton(parent, "Cancel suite", "묶음 취소", () => TestSuites.Cancel(TestSuites.Find(suiteJobId, asset)));
        }
        private void RenderBalance()
        {
            if (balanceReport == null) return; var scroll = balanceReport.scrollOffset; balanceReport.Clear();
            balanceExportSingle?.SetEnabled(balanceComparison == null && BalanceTests.All.Any(x => x.Id == balanceJobId));
            void Row(string text) { var l = new Label(text); l.style.whiteSpace = WhiteSpace.Normal; balanceReport.Add(l); }
            if (balanceComparison != null)
            {
                Row(T("Candidate comparison", "후보 비교") + $" · same definition {balanceComparison.sameDefinition} / runtime {balanceComparison.sameRuntime} / adapters {balanceComparison.sameAdapters}");
                Row($"R0: {balanceComparison.beforeId}\nR1: {balanceComparison.afterId}");
                Row(T("Matched by policy ID · preserved observations; no replay verdict is implied.", "정책 ID로 비교 · 보존된 관측값이며 재현 통과를 뜻하지 않습니다."));
                var comparison = balanceComparison;
                BalanceButton(balanceReport, "Export comparison…", "비교 내보내기…", () => {
                    var path = EditorUtility.SaveFilePanel(T("Export comparison", "비교 내보내기"), "", "RaiseArc-Comparison", "json");
                    if (path.Length > 0) { BalanceComparisonFile.Export(path, comparison); balanceMessage.text = path; }
                });
                Row(T("Same seeds do not guarantee identical chance events after content edits.", "동일 시드는 콘텐츠 변경 후 동일한 우연을 보장하지 않습니다."));
                foreach (var change in balanceComparison.changes) Row(change.path + "\n" + change.before + "\n→ " + change.after);
                for (var i = 0; i < balanceComparison.before.Count; i++)
                {
                    var a = balanceComparison.before[i]; var b = balanceComparison.after.FirstOrDefault(x => x.policyId == a.policyId);
                    Row(a.name + " · " + string.Join(", ", a.outcomes.Select(x => x.endingId + ":" + x.count)) + " → " + (b == null ? "Policy missing" : string.Join(", ", b.outcomes.Select(x => x.endingId + ":" + x.count))));
                    if (b != null) foreach (var value in a.distributions.Where(x => x.day == -1).Take(20))
                    {
                        var next = b.distributions.Find(x => x.day == -1 && x.resource == value.resource && x.population == value.population && x.endingId == value.endingId);
                        Row($"{value.population} {value.endingId} {value.resource}: n={value.distribution.n}, median={value.distribution.median}, mean={value.distribution.mean:F2} → " +
                            (next == null ? "No matching population" : $"n={next.distribution.n}, median={next.distribution.median}, mean={next.distribution.mean:F2}"));
                    }
                }
                foreach (var flow in balanceComparison.beforeLedger.Where(x => x.moneyIn != 0 || x.moneyOut != 0).Take(20))
                {
                    var next = balanceComparison.afterLedger.Where(x => x.policyId == flow.policyId && x.key == flow.key).ToList();
                    Row($"{flow.policyId} {flow.sourceId} {flow.kind} · in/out {flow.moneyIn}/{flow.moneyOut} → {next.Sum(x => x.moneyIn)}/{next.Sum(x => x.moneyOut)}");
                }
                foreach (var before in balanceComparison.before) foreach (var rule in before.rules.Take(12))
                {
                    var after = balanceComparison.after.Find(x => x.policyId == before.policyId)?.rules.Find(x => x.kind == rule.kind && x.id == rule.id && x.population == rule.population);
                    Row($"{before.policyId} {rule.kind} {rule.id}: attempts/passes/total={rule.attempts}/{rule.passes}/{rule.total}, median={rule.values.median} → " +
                        (after == null ? "No matching observations" : $"{after.attempts}/{after.passes}/{after.total}, median={after.values.median}"));
                }
                balanceReport.scrollOffset = scroll; return;
            }
            var job = BalanceTests.All.FirstOrDefault(x => x.Id == balanceJobId); if (job == null) { Row(T("Run or reopen a saved balance report.", "저장된 테스트를 실행하거나 보고서를 다시 여세요.")); return; }
            balanceRenderedTransitions = job.Explorers.Sum(e => e.Report.transitions); balanceRenderedStatus = job.Data.status;
            Row(job.Data.definition.name + " · " + Status(job.Data.status) + " · " + job.Id + (BalanceTests.Stale(job) ? " · STALE" : ""));
            Row(T("Job completion and creator criteria are separate. Policies are authored behavior, not human predictions.", "작업 완료와 제작자 기준 충족은 별개입니다. 정책은 작성한 행동 규칙이며 사람 행동 예측이 아닙니다."));
            var summaries = job.Reports(job.Data.status != ExplorationStatus.Running).ToList();
            bool Show(string metric) => job.Data.definition.metrics.Contains(metric);
            foreach (var summary in summaries)
            {
                var index = summaries.IndexOf(summary);
                Row($"{summary.name} [{summary.policyId}] · {T("requested / not started / started / finished / running", "설정 / 미시작 / 시작 / 종료 / 진행 중")}: {summary.requested}/{summary.notStarted}/{summary.started}/{summary.finished}/{summary.running}");
                if (summary.criteria.Count == 0) Row(T("No creator criteria: no balance-good/bad verdict.", "제작자 기준 없음: 밸런스 좋음/나쁨 판정 없음."));
                foreach (var c in summary.criteria) Row($"{c.id}: {c.verdict} · {c.estimate.observed}/{c.estimate.n}, unknown {c.estimate.unknown}, Wilson95% [{c.estimate.lower:P1}, {c.estimate.upper:P1}] · {c.reason}");
                foreach (var outcome in summary.outcomes)
                {
                    var run = outcome.representativeRun;
                    BalanceButton(balanceReport, $"{outcome.result} / {outcome.endingId}: {outcome.count}/{summary.started} · inspect run {run}", $"{Outcome(outcome.result)} / {outcome.endingId}: {outcome.count}/{summary.started} · run {run} 보기", () => SelectBalanceRun(job, index, run));
                }
                var detail = new Foldout { text = T("Economy / growth / rules / dates", "경제 / 성장 / 규칙 / 날짜") + " · " + summary.name }; balanceReport.Add(detail);
                foreach (var row in job.Explorers[index].Report.ledger.Where(x =>
                    (x.kind == "Economy" || (x.target ?? "").StartsWith("Money:", StringComparison.Ordinal)) ? Show("economy") :
                    (x.target ?? "").StartsWith("Stat:", StringComparison.Ordinal) ? Show("growth") :
                    (x.kind == "Evaluation" || x.kind == "ActivityModifier") ? Show("rules") : Show("content")).Skip(balancePage * 30).Take(30))
                {
                    var id = string.IsNullOrEmpty(row.ownerId) ? row.sourceId : row.ownerId;
                    BalanceButton(detail, $"{row.nameEn} [{row.sourceId}] {row.kind} {row.target} {row.outcome}: n={row.count} requested={row.requested} applied={row.applied} in={row.moneyIn} out={row.moneyOut} first day={row.firstDay}",
                        $"{row.nameKo} [{row.sourceId}] {row.kind} {row.target} {row.outcome}: 횟수={row.count} 요청={row.requested} 실제={row.applied} 유입={row.moneyIn} 소비={row.moneyOut} 첫 일차={row.firstDay}",
                        () => BalanceTests.Navigate(job, index, id, row.firstRecord));
                }
                foreach (var distribution in summary.distributions.Where(x => (x.day < 0 || Show("dates")) && Show(x.resource == "money" ? "economy" : "growth")).Skip(balancePage * 30).Take(30))
                { var v = distribution.distribution; detail.Add(new Label($"{distribution.population} {distribution.endingId} day={distribution.day} {distribution.resource}: n={v.n}, mean={v.mean:F2}, median={v.median:F2}, min/max={v.minimum}/{v.maximum}, p10/p90={v.p10}/{v.p90}")); }
                if (Show("rules")) foreach (var rule in summary.rules.Skip(balancePage * 30).Take(30)) detail.Add(new Label($"{rule.kind} · {rule.id} · {rule.population}: n={rule.n}, attempts/passes/total={rule.attempts}/{rule.passes}/{rule.total}, observed n={rule.values.n}, median={rule.values.median}, min/max={rule.values.minimum}/{rule.values.maximum}"));
                var coverage = new Foldout { text = T("Content / condition observations", "콘텐츠 / 조건 관측") }; detail.Add(coverage);
                if (Show("content")) foreach (var c in job.Explorers[index].Report.coverage.Skip(balancePage * 30).Take(30)) coverage.Add(new Label($"{c.id}: {c.status}, runs={c.runsReached}, occurrences={c.occurrences}, opportunities={c.opportunities}, true/false/unevaluated={c.trueCount}/{c.falseCount}/{c.unevaluatedCount}"));
            }
            BalanceButton(balanceReport, "Next detail page", "다음 상세 페이지", () => { balancePage++; RenderBalance(); });
            BalanceButton(balanceReport, "First detail page", "첫 상세 페이지", () => { balancePage = 0; RenderBalance(); });
            if (balancePath != null)
            {
                var selected = balancePathResult; var selectedPolicy = balancePolicy; var selectedRun = balanceRun;
                var selection = balanceReplaySelection;
                Row($"result={selected.id} · job={selected.jobId}\ncontent={selected.policies[selectedPolicy].report.manifest.contentFingerprint}\npolicy={selectedPolicy} run={selectedRun} · " + T("Recorded inputs and actual states", "기록된 입력과 실제 상태"));
                Row(string.IsNullOrEmpty(balanceReplayText) ? T("Not replayed for this selection.", "현재 선택 경로는 아직 재현하지 않았습니다.") : balanceReplayText);
                foreach (var frame in balancePath) Row($"{frame.input?.kind} {frame.input?.id} · day={frame.state.day} money={frame.state.money} · " + string.Join(", ", frame.state.stats.Select(s => s.id + "=" + s.value)) +
                    " · flags " + string.Join(",", frame.state.flags.Select(s => s.id + "=" + s.value)) + " · modifiers " + string.Join(",", frame.state.modifierExpiry.Select(s => s.id + "=" + Math.Max(0, s.value - frame.state.day))) +
                    " · attempts/passes " + string.Join(",", frame.state.recordAttempts.Select(s => s.id + "=" + s.value + "/" + frame.state.recordPasses.Find(p => p.id == s.id)?.value)));
                BalanceButton(balanceReport, "Verify replay original candidate", "원래 후보 입력 재현 확인", () => {
                    if (selection != balanceReplaySelection) return;
                    ClearBalanceReplay(); var request = balanceReplaySelection; RenderBalance();
                    var r = BalanceTests.Replay(selected, selectedPolicy, selectedRun);
                    CompleteBalanceReplay(request, T("Original candidate replay: ", "원래 후보 재현: ") + r.sameStates + " · " + r.reason);
                });
                BalanceButton(balanceReport, "Recheck inputs on current candidate", "현재 후보에서 같은 입력 재검사", () => {
                    if (selection != balanceReplaySelection) return;
                    ClearBalanceReplay(); var request = balanceReplaySelection; RenderBalance(); var current = balanceCandidate == null ? asset : balanceCandidate; var r = BalanceTests.Replay(selected, selectedPolicy, selectedRun, current);
                    var blocked = r.path.FindIndex(x => x.result == PlayResult.RuntimeError || x.result == PlayResult.Unsupported || x.result == PlayResult.Deadlock);
                    CompleteBalanceReplay(request, T("Current candidate recheck: ", "현재 후보 재검사: ") + SimulationJobs.Fingerprint(current) + $" · same states={r.sameStates} · first difference={r.firstDifference} · blocked input={blocked} · selected ending={r.endingId} · {r.reason} · {r.path.LastOrDefault()?.reason}"); });
            }
            balanceReport.scrollOffset = scroll;
        }
    }
}
