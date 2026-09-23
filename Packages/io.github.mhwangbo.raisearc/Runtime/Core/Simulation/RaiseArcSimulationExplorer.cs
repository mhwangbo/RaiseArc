using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Analysis
{
    /// <summary>Incremental exploration of detached GameSession transactions. Call Tick with a small work budget.</summary>
    public sealed partial class SimulationExplorer
    {
        private readonly ProjectDefinition project;
        private readonly IProjectCodec codec;
        private readonly ExtensionRegistry extensions;
        private readonly AnalysisAdapters adapters;
        private readonly ContentIndex index;
        private readonly bool hasStochasticRules;
        private readonly Dictionary<string, CoverageRow> coverage;
        private readonly Dictionary<string, BranchCoverage> branches;
        private readonly Dictionary<string, BalanceLedgerRow> ledger;
        private readonly HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, PresentationStepKind> compiledKinds = new Dictionary<string, PresentationStepKind>(StringComparer.Ordinal);
        private readonly SimulationCheckpoint checkpoint;
        private GameSession runtime;
        public ExplorationReport Report => checkpoint.report;
        private ExplorationSettings Settings => Report.manifest.settings;
        public IReadOnlyList<SimulationRecord> Records => checkpoint.records;
        public SimulationCheckpoint Checkpoint => checkpoint;

        public SimulationExplorer(ProjectDefinition source, IProjectCodec codec, AnalysisManifest manifest, AnalysisAdapters adapters = null,
            SimulationCheckpoint resume = null)
        {
            if (source == null || codec == null || manifest?.settings == null) throw new ArgumentNullException();
            Validate(manifest.settings);
            manifest = new AnalysisManifest { settings = manifest.settings.Copy(), initial = manifest.initial == null ? null : GameState.FromData(manifest.initial).ToData(),
                contentFingerprint = manifest.contentFingerprint, runtimeFingerprint = manifest.runtimeFingerprint, format = manifest.format, engine = manifest.engine, scope = manifest.scope };
            this.codec = codec;
            project = codec.Clone(source);
            index = new ContentIndex(project, assignIdentities: true);
            if (manifest.settings.useCommittedPlan)
            {
                if (manifest.settings.committedActivities.Count == 0 || manifest.settings.committedActivities.Count > project.time.Capacity)
                    throw new ArgumentException("A committed plan needs a bounded, nonempty activity sequence.");
                foreach (var id in manifest.settings.committedActivities)
                    if (!project.activities.Any(a => a.id == id)) throw new ArgumentException("Committed activity was deleted: " + id);
                var planPeriods = manifest.settings.committedActivities.Sum(id => project.time.Duration(project.activities.Find(a => a.id == id)));
                var startTick = manifest.initial == null ? 0 : manifest.initial.day * project.time.PeriodsPerDay + manifest.initial.period;
                if (planPeriods > project.time.Capacity || startTick + planPeriods > project.durationDays * project.time.PeriodsPerDay)
                    throw new ArgumentException("Committed activities exceed the planning range.");
                manifest.scope += " Activities are fixed before the first result; event choices remain runtime inputs. No adaptive replanning.";
            }
            foreach (var id in manifest.settings.allowedActivities.Concat(manifest.settings.forbiddenActivities))
                if (!project.activities.Any(a => a.id == id)) throw new ArgumentException("Unknown restricted activity: " + id);
            if (manifest.settings.endingGoal && !project.endings.Any(e => e.id == manifest.settings.targetId))
                throw new ArgumentException("The target ending no longer exists: " + manifest.settings.targetId);
            hasStochasticRules = project.activities.Any(a => a.successChance != null && a.successChance.enabled) ||
                project.events.Any(e => e.triggerChance != null && e.triggerChance.enabled) ||
                index.Data.entries.Any(e => index.Find(e.id) is EffectSpec effect && effect.randomRange && effect.minimumValue != effect.maximumValue);
            this.adapters = adapters ?? new AnalysisAdapters();
            extensions = this.adapters.Extensions(index, manifest.settings.allowExternalCode);
            foreach (var ev in project.events)
                foreach (var step in RaiseArc.Core.RaiseArcFlowReuse.Expand(project, ev, sourceIds).presentation) compiledKinds[step.id] = step.kind;
            if (resume != null)
            {
                var previous = resume.report?.manifest ?? throw new ArgumentException("Missing checkpoint manifest.");
                if (previous.format != manifest.format || previous.engine != manifest.engine || previous.contentFingerprint != manifest.contentFingerprint ||
                    previous.runtimeFingerprint != manifest.runtimeFingerprint || previous.adapterFingerprint != this.adapters.Fingerprint)
                    throw new ArgumentException("Checkpoint content/runtime/adapter version mismatch. Start a separate analysis.");
                checkpoint = resume;
                ValidateCheckpoint();
                if (Report.status == ExplorationStatus.Running || !Enum.IsDefined(typeof(ExplorationStatus), Report.status)) throw new ArgumentException("Checkpoint must be paused or finished.");
                foreach (var record in checkpoint.records) visited.Add(Key(record));
            }
            else
            {
                manifest.adapterFingerprint = this.adapters.Fingerprint;
                manifest.modules = this.adapters.Modules.Select(a => a.Contract.Copy()).ToList();
                manifest.rules = this.adapters.Rules.Select(r => r.Copy()).ToList();
                manifest.models = this.adapters.Modules.OfType<ResultModel>().Select(m => new ModelDefinition { moduleId = m.Contract.moduleId, outcomes = m.Outcomes(null, "", "").ToList() }).ToList();
                manifest.projectId = project.id; manifest.revision = project.revision; manifest.gameDurationDays = project.durationDays;
                manifest.createdUtc = DateTime.UtcNow.ToString("O");
                var start = new GameSession(project, extensions);
                manifest.initialOrigin = manifest.initial == null ? "new-game" : "supplied-state";
                if (manifest.initial != null) start.Restore(manifest.initial);
                manifest.initial = start.Capture();
                if (hasStochasticRules)
                {
                    manifest.limitations.Add("Built-in chance and random effect ranges are sampled, not exhaustively enumerated across random seeds. NotFound does not prove unreachable.");
                    manifest.assumptions.Add("Monte Carlo derives a separate saved gameplay random state from the analysis seed and run number. Replay uses that recorded state.");
                }
                manifest.limitations.Add("Custom condition/effect handlers without an opted-in stateless deterministic contract are opaque; Scene autoplay is unavailable in this headless runner.");
                foreach (var rule in this.adapters.Rules.Where(r => r.statelessDeterministic && manifest.settings.allowExternalCode))
                    manifest.assumptions.Add("Provider declaration: stateless deterministic rules " + rule.id + "@" + rule.version + "; not independently proven");
                manifest.limitations.Add("Priority losers after runtime short-circuit are unevaluated, not proven eligible or permanently suppressed.");
                manifest.limitations.Add("Core has no autonomous wait input or terminal death flag; death/failure ending IDs are explicitly configured in settings.");
                manifest.limitations.Add("Growth/appearance/localization resolution and module internals without a catalog are outside built-in execution coverage.");
                checkpoint = new SimulationCheckpoint { report = new ExplorationReport { manifest = manifest } };
                BuildCatalog();
            }
            coverage = Report.coverage.ToDictionary(r => r.id, StringComparer.Ordinal);
            branches = Report.branches.ToDictionary(r => r.id, StringComparer.Ordinal);
            ledger = Report.ledger.ToDictionary(r => r.key, StringComparer.Ordinal);
            if (Settings.policy == "income-first-v1" && !project.activities.Any(a => a.id == Settings.incomeActivityId))
                throw new ArgumentException("Income-first policy requires an existing income activity ID.");
            if (Settings.mode == ExplorationMode.TargetSearch && (string.IsNullOrEmpty(Settings.targetId) ? Settings.targetResult == PlayResult.Running : !coverage.ContainsKey(Settings.targetId)))
                throw new ArgumentException("Target search needs a registered content ID or a non-running targetResult.");
            if (resume == null && Settings.mode != ExplorationMode.MonteCarlo) AddRoot(0);
            RefreshCounts();
        }

        private static void Validate(ExplorationSettings s)
        {
            if (!Enum.IsDefined(typeof(ExplorationMode), s.mode) || !Enum.IsDefined(typeof(PlayResult), s.targetResult) || s.runs < 1 || s.runs > 100000 || s.maxStates < 1 || s.maxStates > 1000000 ||
                s.maxTransitions < 1 || s.maxSteps < 1 || s.horizonDays < 1 || s.maxSeconds < 1 || s.maxMemoryMiB < 1 || s.maxCheckpointMiB < 1)
                throw new ArgumentException("Invalid simulation budget.");
            if (s.policy != "uniform-v1" && s.policy != "weighted-v1" && s.policy != "income-first-v1") throw new ArgumentException("Unknown policy.");
            if (s.runOffset < 0 || s.runOffset > int.MaxValue - s.runs || s.incomeThreshold < 0) throw new ArgumentException("Invalid policy seed range or income threshold.");
            if (s.rareRunRate < 0 || s.frequentRunRate > 1 || s.rareRunRate > s.frequentRunRate) throw new ArgumentException("Invalid frequency thresholds.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in s.weights)
                if (string.IsNullOrEmpty(w.id) || !keys.Add(w.id) || w.weight < 0 || double.IsNaN(w.weight) || double.IsInfinity(w.weight))
                    throw new ArgumentException("Weights require unique IDs and finite, non-negative values.");
            if (s.deathEndingIds.Intersect(s.failureEndingIds).Any()) throw new ArgumentException("Death and other failure ending IDs must not overlap.");
        }
        private void ValidateCheckpoint()
        {
            if (checkpoint.records == null || checkpoint.frontier == null || checkpoint.front < 0 || checkpoint.front > checkpoint.frontier.Count ||
                checkpoint.activeRunRecord < -1 || checkpoint.activeRunRecord >= checkpoint.records.Count || checkpoint.records.Count > Settings.maxStates)
                throw new ArgumentException("Invalid checkpoint bounds.");
            for (var i = 0; i < checkpoint.records.Count; i++)
            {
                var r = checkpoint.records[i];
                if (r == null || r.state == null || r.recordId != i || r.parent < -1 || r.parent >= i || r.cursor < 0 ||
                    r.planCursor < 0 || r.planCursor > (Settings.useCommittedPlan ? Settings.committedActivities.Count : 0) ||
                    r.inputsReady && (r.inputs == null || r.cursor > r.inputs.Count)) throw new ArgumentException("Invalid checkpoint path or input cursor.");
            }
            if (checkpoint.frontier.Any(i => i < 0 || i >= checkpoint.records.Count) || Report.startedRuns != Report.plays.Count + (checkpoint.activeRunRecord >= 0 ? 1 : 0))
                throw new ArgumentException("Invalid checkpoint frontier or run accounting.");
        }

        private void BuildCatalog()
        {
            foreach (var original in project.events)
            {
                var ev = RaiseArc.Core.RaiseArcFlowReuse.Expand(project, original);
                void Add(string from, string target, string port)
                {
                    Report.branches.Add(new BranchCoverage { id = BranchKey(ev.id, from, port), eventId = ev.id, sourceId = Source(from),
                        targetId = Source(target.Length == 0 ? (ev.choices.Count == 0 ? EventSequence.End : EventSequence.TerminalChoices) : target), port = port,
                        status = original.callOnly ? ReachStatus.OutOfScope : ReachStatus.NotFound });
                }
                Add(ev.id, EventSequence.Entry(ev), "Entry");
                foreach (var choice in ev.choices) Add(choice.id, EventSequence.End, "Choice");
                foreach (var step in ev.presentation)
                    if (step.kind == PresentationStepKind.Choice)
                        foreach (var choice in step.choices) Add(choice.id, EventSequence.Next(ev, step, choice.nextStepId), "Choice");
                    else if (step.kind == PresentationStepKind.Condition)
                    { Add(step.id, EventSequence.Next(ev, step), "True"); Add(step.id, EventSequence.Next(ev, step, step.falseStepId), "False"); }
                    else Add(step.id, EventSequence.Next(ev, step), "Next");
            }
            foreach (var entry in index.Data.entries)
            {
                var obj = index.Find(entry.id);
                var executable = obj is EventDefinition || obj is EndingDefinition || obj is ActivityDefinition || obj is ChoiceDefinition ||
                    obj is PresentationStep || obj is ConditionSpec || obj is EffectSpec || obj is ModuleDefinition || obj is ModuleEnemy || obj is ModuleEncounter || obj is ModuleLocation;
                if (!executable) continue;
                var row = new CoverageRow { id = entry.id, kind = entry.kind, ownerId = entry.ownerId, nameKey = entry.nameKey, sourcePath = entry.path };
                var owner = entry.id;
                while (!string.IsNullOrEmpty(owner))
                {
                    if (index.Find(owner) is GrowthDefinition || index.Find(owner) is AppearanceRule) { row.status = ReachStatus.OutOfScope; row.reason = "Presentation resolution is outside this input runner"; break; }
                    if (owner != entry.id && index.Find(owner) is ModuleDefinition module)
                    {
                        if (adapters.Find(module.id)?.Contract.contentIds.Contains(entry.id) == true) { row.id = module.id + "/" + entry.id; row.ownerId = module.id; row.kind = "ModuleInternal"; }
                        else { row.status = ReachStatus.Unverified; row.reason = "Authored module-internal content has no registered observation mapping"; }
                        break;
                    }
                    owner = index.Owner(owner);
                }
                Report.coverage.Add(row);
            }
            foreach (var module in project.modules)
            {
                var adapter = adapters.Find(module.id);
                if (adapter == null) { Report.manifest.limitations.Add("No internal coverage catalog: " + module.id); continue; }
                var contract = adapter.Contract;
                Report.manifest.limitations.Add(module.id + " catalog: " + contract.catalogCompleteness);
                foreach (var id in contract.contentIds.Distinct(StringComparer.Ordinal))
                    if (!Report.coverage.Any(r => r.id == module.id + "/" + id)) Report.coverage.Add(new CoverageRow { id = module.id + "/" + id, ownerId = module.id, kind = "ModuleInternal", reason = "Not observed; catalog=" + contract.catalogCompleteness });
            }
        }

        public void Tick(int maxTransitions = 16, int maxMilliseconds = 8)
        {
            if (Report.status != ExplorationStatus.Running) return;
            var clock = Stopwatch.StartNew();
            try
            {
                for (var n = 0; n < Math.Max(1, maxTransitions) && Report.status == ExplorationStatus.Running; n++)
                {
                    if (Report.elapsedSeconds + clock.Elapsed.TotalSeconds >= Settings.maxSeconds) { StopBudget("Active work-time budget"); break; }
                    if (Report.transitions >= Settings.maxTransitions || checkpoint.records.Count >= Settings.maxStates || Report.retainedBytes >= Settings.maxMemoryMiB * 1048576L)
                    { StopBudget("Transition/state/retained-memory budget"); break; }
                    if (adapters.Fingerprint != Report.manifest.adapterFingerprint) throw new InvalidOperationException("Adapter fingerprint changed during analysis.");
                    if (Settings.mode == ExplorationMode.MonteCarlo) SampleStep(); else ExploreStep();
                    if (clock.ElapsedMilliseconds >= maxMilliseconds) break;
                }
            }
            finally { Report.elapsedSeconds += clock.Elapsed.TotalSeconds; RefreshCounts(); }
        }
        public void Pause() { if (Report.status == ExplorationStatus.Running) Report.status = ExplorationStatus.Paused; }
        public void Resume() { if (Report.status != ExplorationStatus.Paused) throw new InvalidOperationException("Analysis is not paused."); Report.status = ExplorationStatus.Running; }
        public void Cancel()
        {
            if (Report.status != ExplorationStatus.Running && Report.status != ExplorationStatus.Paused) return;
            if (checkpoint.activeRunRecord >= 0) FinishPlay(checkpoint.activeRunRecord, PlayResult.Cancelled, "Owner cancellation");
            Report.status = ExplorationStatus.Cancelled; Report.manifest.stopReason = "Owner cancellation"; FinalizeCoverage(false);
        }
        private void StopBudget(string reason)
        {
            if (checkpoint.activeRunRecord >= 0) FinishPlay(checkpoint.activeRunRecord, PlayResult.Budget, reason);
            Report.status = ExplorationStatus.BudgetStopped; Report.manifest.stopReason = reason; FinalizeCoverage(false);
        }
        public void StopForBudget(string reason) { if (Report.status == ExplorationStatus.Running || Report.status == ExplorationStatus.Paused) StopBudget(reason); }
        private void Complete()
        {
            Report.status = ExplorationStatus.Completed;
            Report.manifest.stopReason = Settings.mode == ExplorationMode.MonteCarlo ? "Requested independent runs completed" : "Frontier exhausted";
            var exact = (Settings.mode == ExplorationMode.Exhaustive || Settings.mode == ExplorationMode.TargetSearch) && !hasStochasticRules && !Report.manifest.hasUnsupported && !Report.manifest.hasModels &&
                !checkpoint.records.Any(r => r.result == PlayResult.Budget || r.result == PlayResult.RuntimeError);
            FinalizeCoverage(exact);
        }
        private void FinalizeCoverage(bool complete)
        {
            Report.manifest.completeWithinScope = complete;
            Report.manifest.completeness = complete ? "All supported transitions from the specified initial state exhausted under recorded provider contracts; does not prove every play terminates" : "Incomplete verification; NotFound is not Unreachable";
            foreach (var row in Report.coverage.Where(r => r.status == ReachStatus.NotFound))
            {
                if (complete && (row.kind != "ModuleInternal" || adapters.Find(row.ownerId)?.Contract.catalogCompleteness == CatalogCompleteness.Complete)) { row.status = ReachStatus.UnreachableInScope; row.evidence = "bounded proof under recorded contracts"; row.reason = "No visit in fully exhausted supported state graph for this initial state"; }
                else if (complete && row.kind == "ModuleInternal") { row.status = ReachStatus.Unverified; row.reason = "Incomplete internal observation catalog"; }
                else if (Report.manifest.hasUnsupported) { row.status = ReachStatus.Unverified; row.reason = "An opaque boundary may hide paths to this content"; }
            }
            foreach (var branch in Report.branches.Where(r => r.status == ReachStatus.NotFound))
                branch.status = complete ? ReachStatus.UnreachableInScope : Report.manifest.hasUnsupported ? ReachStatus.Unverified : ReachStatus.NotFound;
        }
        private void AddRoot(int run)
        {
            var session = new GameSession(project, extensions); session.Restore(Report.manifest.initial);
            var root = new SimulationRecord { state = session.Capture(), run = run, policyRandom = unchecked((uint)Settings.seed + (uint)run * 747796405u), moduleRandom = unchecked((uint)Settings.seed ^ (uint)run * 2891336453u ^ 0xa341316cu) };
            if (Settings.mode == ExplorationMode.MonteCarlo && (hasStochasticRules || Settings.separateGameSeed))
                root.state.randomState = unchecked((uint)(Settings.separateGameSeed ? Settings.gameSeed : Settings.seed) + (uint)run * 0x9e3779b9U);
            var id = Append(root);
            if (Settings.mode == ExplorationMode.MonteCarlo) { checkpoint.activeRunRecord = id; checkpoint.currentRunStart = id; checkpoint.currentRunSeen.Clear(); checkpoint.firstMoneyBlockedDay = -1; checkpoint.qualificationDays.Clear(); checkpoint.modifierActiveDays.Clear(); Report.startedRuns++; SampleState(root); }
            else { visited.Add(Key(root)); checkpoint.frontier.Add(id); }
        }
        private void SampleStep()
        {
            if (checkpoint.activeRunRecord < 0)
            {
                if (Report.startedRuns >= Settings.runs) { Complete(); return; }
                AddRoot(Report.startedRuns + Settings.runOffset); return;
            }
            var id = checkpoint.activeRunRecord;
            var record = checkpoint.records[id];
            if (Classify(record)) { FinishPlay(id, record.result, record.reason); return; }
            try
            {
                EnsureInputs(id);
                if (record.inputs.Count == 0) { FinishPlay(id, PlayResult.Deadlock, "No valid GameSession input; core has no autonomous wait transition"); return; }
                var weights = record.inputs.Select(a => Settings.policy == "uniform-v1" ? 1.0 : Settings.weights.Find(w => w.id == a.id)?.weight ?? 1.0).ToList();
                var preferred = Settings.policy == "income-first-v1" && record.state.money < Settings.incomeThreshold
                    ? record.inputs.Find(a => a.kind == "Activity" && a.id == Settings.incomeActivityId) : null;
                var input = preferred ?? record.inputs[SimulationIdentity.Select(weights, ref record.policyRandom)];
                var next = Transition(record, input);
                var child = Append(next); Aggregate(child); SampleState(next); Report.transitions++;
                Classify(next);
                checkpoint.activeRunRecord = child;
                if (next.result != PlayResult.Running) FinishPlay(child, next.result, next.reason);
            }
            catch (Exception ex) { FinishPlay(id, ResultFor(ex), ex.Message); }
        }
        private void ExploreStep()
        {
            if (checkpoint.front >= checkpoint.frontier.Count) { Complete(); return; }
            var id = checkpoint.frontier[checkpoint.front]; var record = checkpoint.records[id];
            Classify(record);
            if (ReachedTarget(record)) { FoundTarget(id); return; }
            if (record.result != PlayResult.Running) { checkpoint.front++; return; }
            try
            {
                EnsureInputs(id);
                if (record.inputs.Count == 0) { record.result = PlayResult.Deadlock; record.reason = "No valid GameSession input"; if (ReachedTarget(record)) FoundTarget(id); checkpoint.front++; return; }
                if (record.cursor >= record.inputs.Count) { checkpoint.front++; return; }
                var next = Transition(record, record.inputs[record.cursor++]);
                var child = Append(next); Report.transitions++; Aggregate(child);
                Classify(next);
                var newState = visited.Add(Key(next));
                if (ReachedTarget(next)) { FoundTarget(child); return; }
                if (next.result == PlayResult.Running && newState) checkpoint.frontier.Add(child);
                else if (next.result == PlayResult.Running) next.reason = "Previously reached canonical state; this is not proof of a forced loop";
            }
            catch (Exception ex) { record.result = ResultFor(ex); record.reason = ex.Message; Boundary(record); if (ReachedTarget(record)) FoundTarget(id); checkpoint.front++; }
        }
        private void FoundTarget(int id)
        {
            Report.targetRecord = id; Report.status = ExplorationStatus.Completed; Report.manifest.stopReason = "Representative target path discovered (not an optimality claim)"; FinalizeCoverage(false);
        }
        private bool ReachedTarget(SimulationRecord record) => Settings.mode == ExplorationMode.TargetSearch &&
            (Settings.endingGoal ? record.state.endingId == Settings.targetId :
             Settings.targetResult != PlayResult.Running && record.result == Settings.targetResult || !string.IsNullOrEmpty(Settings.targetId) &&
             (record.state.endingId == Settings.targetId || record.state.pendingEventId == Settings.targetId || record.state.presentationStepId == Settings.targetId ||
              record.input?.id == Settings.targetId && record.result != PlayResult.Unsupported && record.result != PlayResult.RuntimeError ||
              record.observations.Any(o => (o.committed || o.kind == "Condition" && (o.outcome == "True" || o.outcome == "False")) && Source(o.id) == Settings.targetId)));

        private void EnsureInputs(int id)
        {
            var record = checkpoint.records[id]; if (record.inputsReady) return;
            var session = Session(record); var observations = session.Observations;
            var inputs = new List<SimulationInput>();
            try
            {
                if (!string.IsNullOrEmpty(session.State.PendingEventId))
                {
                    var ev = RaiseArc.Core.RaiseArcFlowReuse.Expand(project, project.events.Find(e => e.id == session.State.PendingEventId));
                    var step = ev.presentation.Find(s => s.id == session.State.PresentationStepId);
                    if (step == null || step.kind == PresentationStepKind.Choice)
                        foreach (var choice in (step == null ? ev.choices : step.choices).OrderBy(c => c.id, StringComparer.Ordinal))
                        {
                            try { if (session.CanChoose(choice.id)) inputs.Add(new SimulationInput { kind = "Choice", id = choice.id }); }
                            catch (AnalysisBoundaryException ex) { if (Settings.mode == ExplorationMode.MonteCarlo) throw; inputs.Add(new SimulationInput { kind = "Boundary", id = choice.id, boundary = ex.Message }); }
                        }
                    if (inputs.Count == 0 && !string.IsNullOrEmpty(session.State.PresentationStepId))
                    {
                        if (step?.kind != PresentationStepKind.Choice)
                            inputs.Add(new SimulationInput { kind = "Advance", id = session.State.PresentationStepId });
                    }
                }
                else foreach (var activity in project.activities.OrderBy(a => a.id, StringComparer.Ordinal))
                {
                    if (Settings.useCommittedPlan && (record.planCursor >= Settings.committedActivities.Count || activity.id != Settings.committedActivities[record.planCursor])) continue;
                    if (Settings.forbiddenActivities.Contains(activity.id) ||
                        Settings.allowedActivities.Count > 0 && !Settings.allowedActivities.Contains(activity.id)) continue;
                    try
                    {
                        var availability = session.DiagnoseActivity(activity.id);
                        if (availability.Available) inputs.Add(new SimulationInput { kind = "Activity", id = activity.id, moduleId = activity.moduleId });
                        else observations.Entries.Add(new RuntimeObservation { kind = "ActivityBlocked", id = activity.id, day = session.State.Day,
                            outcome = availability.Reason.ToString(), hasActual = true, actual = availability.Actual, expected = availability.Required });
                    }
                    catch (AnalysisBoundaryException ex) { if (Settings.mode == ExplorationMode.MonteCarlo) throw; inputs.Add(new SimulationInput { kind = "Boundary", id = activity.id, boundary = ex.Message }); }
                }
                if (Settings.mode != ExplorationMode.MonteCarlo)
                {
                    var expanded = new List<SimulationInput>();
                    foreach (var input in inputs)
                    {
                        if (string.IsNullOrEmpty(input.moduleId)) { expanded.Add(input); continue; }
                        try
                        {
                            if (adapters.Find(input.moduleId)?.Contract.exhaustiveOutcomes != true) throw new AnalysisBoundaryException("Module does not declare complete outcome enumeration: " + input.moduleId);
                            var outcomes = Outcomes(record, input, session);
                            foreach (var outcome in outcomes) expanded.Add(new SimulationInput { kind = input.kind, id = input.id, moduleId = input.moduleId, outcomeId = outcome.id });
                        }
                        catch (AnalysisBoundaryException) { expanded.Add(input); }
                    }
                    inputs = expanded;
                }
                record.inputs = inputs;
                record.inputsReady = true;
                Report.retainedBytes += inputs.Count * 96L;
                foreach (var input in inputs.Where(i => i.kind != "Boundary").GroupBy(i => i.id).Select(g => g.First()))
                    if (coverage.TryGetValue(Source(input.id), out var row)) row.opportunities++;
            }
            finally
            {
                record.observations.AddRange(observations.Entries);
                Report.retainedBytes += observations.Entries.Count * 192L;
                AggregateObservations(id, observations.Entries);
            }
        }
        private GameSession Session(SimulationRecord record)
        {
            var session = runtime;
            if (session == null || session.HasPendingModule) runtime = session = new GameSession(project, extensions);
            session.Restore(record.state);
            session.Observations = new RuntimeObservations(); return session;
        }
        private IReadOnlyList<AnalysisOutcome> Outcomes(SimulationRecord record, SimulationInput input, GameSession session)
        {
            var adapter = adapters.Find(input.moduleId) ?? throw new AnalysisBoundaryException("No analysis adapter for module: " + input.moduleId);
            if (adapter.Contract.kind == ModuleAnalysisKind.SceneDriver) throw new AnalysisBoundaryException("Scene driver requires an actual Unity Play Mode host; headless substitution is forbidden: " + input.moduleId);
            if (!(adapter is ResultModel) && !Settings.allowExternalCode) throw new AnalysisBoundaryException("External user code requires explicit opt-in: " + input.moduleId);
            var outcomes = adapter.Outcomes(session.State, input.id, record.modules.Find(m => m.id == input.moduleId)?.payload ?? "");
            if (outcomes == null || outcomes.Count == 0 || outcomes.Count > 1024 || outcomes.Any(o => o == null || string.IsNullOrEmpty(o.id)) || outcomes.Select(o => o.id).Distinct().Count() != outcomes.Count)
                throw new ArgumentException("Module outcomes must have 1–1024 unique stable IDs.");
            return outcomes;
        }
        private SimulationRecord Transition(SimulationRecord parent, SimulationInput input)
        {
            var session = Session(parent);
            var next = new SimulationRecord { parent = parent.recordId, depth = parent.depth + 1, run = parent.run, input = input,
                planCursor = parent.planCursor,
                policyRandom = parent.policyRandom, moduleRandom = parent.moduleRandom, state = parent.state, eventId = parent.state.pendingEventId,
                nodeId = parent.state.presentationStepId, modules = parent.modules.Select(m => new ModuleState { id = m.id, payload = m.payload }).ToList(), assumptions = new List<string>(parent.assumptions) };
            try
            {
                if (input.kind == "Boundary") throw new AnalysisBoundaryException(input.boundary);
                if (Settings.useCommittedPlan && input.kind == "Activity" &&
                    (parent.planCursor >= Settings.committedActivities.Count || input.id != Settings.committedActivities[parent.planCursor]))
                    throw new InvalidOperationException("Input differs from the plan committed before execution.");
                if (input.kind == "Advance") session.AdvancePresentation(input.id);
                else if (input.kind == "Choice") session.Choose(input.id);
                else if (string.IsNullOrEmpty(input.moduleId)) session.PerformActivity(input.id);
                else
                {
                    var adapter = adapters.Find(input.moduleId) ?? throw new AnalysisBoundaryException("No analysis adapter for module: " + input.moduleId);
                    if (adapter.Contract.kind == ModuleAnalysisKind.SceneDriver) throw new AnalysisBoundaryException("Scene execution is unsupported in the headless runner: " + input.moduleId);
                    if (!(adapter is ResultModel) && !Settings.allowExternalCode) throw new AnalysisBoundaryException("External code requires explicit opt-in: " + input.moduleId);
                    AnalysisOutcome outcome;
                    uint sampleSeed = 0;
                    if (Settings.mode == ExplorationMode.MonteCarlo && adapter is IModuleSampler sampler && adapter.Contract.kind == ModuleAnalysisKind.Headless)
                    {
                        SimulationIdentity.Next(ref next.moduleRandom); sampleSeed = next.moduleRandom;
                        outcome = sampler.Sample(session.State, input.id, parent.modules.Find(m => m.id == input.moduleId)?.payload ?? "", sampleSeed);
                        if (outcome == null || string.IsNullOrEmpty(outcome.id)) throw new ArgumentException("Sampled module result needs a stable outcome ID.");
                    }
                    else if (Settings.mode == ExplorationMode.MonteCarlo)
                    {
                        var outcomes = Outcomes(parent, input, session);
                        if (outcomes.Any(o => o.probability < 0) || Math.Abs(outcomes.Sum(o => o.probability) - 1) > 0.000001)
                            throw new AnalysisBoundaryException("Module sampling requires explicit probabilities summing to 1: " + input.moduleId);
                        outcome = outcomes[SimulationIdentity.Select(outcomes.Select(o => o.probability).ToList(), ref next.moduleRandom)];
                    }
                    else
                    {
                        if (!adapter.Contract.exhaustiveOutcomes) throw new AnalysisBoundaryException("No exhaustive outcome contract: " + input.moduleId);
                        var outcomes = Outcomes(parent, input, session);
                        outcome = outcomes.FirstOrDefault(o => o.id == input.outcomeId) ?? throw new AnalysisBoundaryException("Missing enumerated module outcome");
                    }
                    next.input = new SimulationInput { kind = input.kind, id = input.id, moduleId = input.moduleId, outcomeId = outcome.id, randomSeed = sampleSeed };
                    if (outcome.terminalCause != "" && outcome.terminalCause != "death" && outcome.terminalCause != "failure") throw new ArgumentException("Unknown module terminal cause: " + outcome.terminalCause);
                    if (!string.IsNullOrEmpty(outcome.terminalCause) && adapter.Contract.kind != ModuleAnalysisKind.ResultModel)
                        throw new AnalysisBoundaryException("Core ModuleResult has no terminal-cause contract. Use actual ending IDs or an explicitly hypothetical result model: " + input.moduleId);
                    if (adapter.Contract.kind == ModuleAnalysisKind.ResultModel)
                    {
                        var assumption = "Result model: " + input.moduleId + "@" + adapter.Contract.version;
                        if (!next.assumptions.Contains(assumption)) next.assumptions.Add(assumption);
                        if (!Report.manifest.assumptions.Contains(assumption)) Report.manifest.assumptions.Add(assumption);
                        Report.manifest.hasModels = true;
                    }
                    var context = session.BeginModule(input.id);
                    if (Settings.useCommittedPlan && outcome.result.elapsedDays * project.time.PeriodsPerDay != project.time.Duration(project.activities.Find(a => a.id == input.id)))
                        throw new AnalysisBoundaryException("Committed plans require modules with their reserved duration; this outcome has a variable duration.");
                    session.CompleteModule(new ModuleResult { sessionId = context.SessionId, elapsedDays = outcome.result.elapsedDays,
                        messageKey = outcome.result.messageKey, effects = outcome.result.effects });
                    next.modules.RemoveAll(m => m.id == input.moduleId); next.modules.Add(new ModuleState { id = input.moduleId, payload = outcome.nextState });
                    session.Observations.Record("Module", input.moduleId, "Completed", session.State.Day);
                    foreach (var observed in outcome.contentIds.Distinct(StringComparer.Ordinal))
                    {
                        var known = adapter.Contract.contentIds.Contains(observed);
                        session.Observations.Record("ModuleInternal", input.moduleId + "/" + observed, known ? "Observed" : "OutsideCatalog", session.State.Day);
                        if (!known) AddProblem("Observed module content outside registered catalog: " + input.moduleId + "/" + observed);
                    }
                    if (outcome.terminalCause == "death") { next.result = PlayResult.Death; next.reason = "Module reported terminal death"; }
                    else if (outcome.terminalCause == "failure") { next.result = PlayResult.OtherFailure; next.reason = "Module reported terminal failure"; }
                }
                session.Observations.Record(input.kind, input.id, "Selected", session.State.Day);
                session.Observations.CommitFrom(0);
                next.state = session.Capture();
                if (Settings.useCommittedPlan && input.kind == "Activity") next.planCursor++;
                if (!string.IsNullOrEmpty(next.state.endingId) && string.IsNullOrEmpty(parent.state.endingId)) ObserveEndingCompetition(session);
            }
            catch (Exception ex) { next.result = ResultFor(ex); next.reason = ex.Message; Boundary(next); }
            next.observations = session.Observations.Entries;
            return next;
        }
        private bool Classify(SimulationRecord record)
        {
            if (record.result != PlayResult.Running) return true;
            if (Settings.useCommittedPlan && string.IsNullOrEmpty(record.state.pendingEventId) && string.IsNullOrEmpty(record.state.pendingActivityId))
            {
                var expected = Report.manifest.initial.day * project.time.PeriodsPerDay + Report.manifest.initial.period +
                    Settings.committedActivities.Take(record.planCursor).Sum(id => project.time.Duration(project.activities.Find(a => a.id == id)));
                if (record.state.day * project.time.PeriodsPerDay + record.state.period != expected)
                { record.result = PlayResult.Deadlock; record.reason = "An activity stopped before its reserved time completed. The committed plan was not shifted or replanned."; return true; }
            }
            if (!string.IsNullOrEmpty(record.state.endingId))
            {
                record.result = Settings.deathEndingIds.Contains(record.state.endingId) ? PlayResult.Death : Settings.failureEndingIds.Contains(record.state.endingId) ? PlayResult.OtherFailure : PlayResult.Ending;
                return true;
            }
            if (record.state.day >= project.durationDays && string.IsNullOrEmpty(record.state.pendingEventId))
            { record.result = PlayResult.OtherFailure; record.reason = "Calendar ended without a matching ending"; return true; }
            if (Settings.useCommittedPlan && record.planCursor == Settings.committedActivities.Count && string.IsNullOrEmpty(record.state.pendingEventId))
            { record.result = PlayResult.PlanComplete; record.reason = "The precommitted plan finished; no further activities were selected adaptively."; return true; }
            if (record.depth >= Settings.maxSteps || record.state.day > Settings.horizonDays || record.state.day == Settings.horizonDays && string.IsNullOrEmpty(record.state.pendingEventId))
            { record.result = PlayResult.Budget; record.reason = "Step/day horizon reached"; return true; }
            return false;
        }
        private void ObserveEndingCompetition(GameSession session)
        {
            var pureRules = new RuleEngine(project);
            foreach (var ending in project.endings)
            {
                if (ending.conditions.Any(c => c.kind == ValueKind.Custom))
                { session.Observations.Record("EndingEligibility", ending.id, "Unknown", session.State.Day); continue; }
                if (pureRules.Matches(session.State, ending.conditions))
                    session.Observations.Record("EndingEligibility", ending.id, ending.id == session.State.EndingId ? "Selected" : "PriorityLoss", session.State.Day);
            }
        }
        private static PlayResult ResultFor(Exception ex) => ex is AnalysisBoundaryException ? PlayResult.Unsupported : PlayResult.RuntimeError;
        private void Boundary(SimulationRecord record)
        {
            if (record.result == PlayResult.Unsupported) Report.manifest.hasUnsupported = true;
            if (record.result == PlayResult.Unsupported || record.result == PlayResult.RuntimeError) AddProblem(record.reason);
        }
        private void AddProblem(string problem) { if (!Report.problems.Contains(problem) && Report.problems.Count < 256) Report.problems.Add(problem); }
        private void FinishPlay(int id, PlayResult result, string reason)
        {
            var record = checkpoint.records[id]; record.result = result; record.reason = reason; Boundary(record);
            var retain = Report.coverage.Any(r => r.firstRecord >= checkpoint.currentRunStart) || Report.branches.Any(r => r.firstRecord >= checkpoint.currentRunStart) || Report.ledger.Any(r => r.firstRecord >= checkpoint.currentRunStart) || !Report.plays.Any(p => p.result == result && p.endingId == record.state.endingId);
            var root = checkpoint.records[checkpoint.currentRunStart];
            Report.plays.Add(new PlaySummary { run = record.run, record = retain ? id : -1, result = result, reason = reason, day = record.state.day, endingId = record.state.endingId,
                policySeed = unchecked((uint)Settings.seed + (uint)record.run * 747796405u), gameplaySeed = root.state.randomState,
                moduleSeed = unchecked((uint)Settings.seed ^ (uint)record.run * 2891336453u ^ 0xa341316cu),
                finalState = GameState.FromData(record.state).ToData(), reached = new List<string>(checkpoint.currentRunSeen), firstMoneyBlockedDay = checkpoint.firstMoneyBlockedDay,
                qualificationDays = checkpoint.qualificationDays.ConvertAll(x => new IntEntry { id = x.id, value = x.value }), modifierActiveDays = checkpoint.modifierActiveDays.ConvertAll(x => new IntEntry { id = x.id, value = x.value }) });
            Report.retainedBytes += 160 + (reason?.Length ?? 0) * 2L + record.state.endingId.Length * 2L + Key(record).Length * 2L + checkpoint.currentRunSeen.Sum(x => 24L + x.Length * 2L);
            if (!retain)
            {
                for (var i = checkpoint.currentRunStart; i < checkpoint.records.Count; i++) Report.retainedBytes -= EstimatedBytes(checkpoint.records[i]);
                checkpoint.records.RemoveRange(checkpoint.currentRunStart, checkpoint.records.Count - checkpoint.currentRunStart);
            }
            checkpoint.activeRunRecord = -1; RefreshCounts();
        }
        private int Append(SimulationRecord record)
        {
            record.recordId = checkpoint.records.Count;
            checkpoint.records.Add(record);
            Report.retainedBytes += EstimatedBytes(record);
            return checkpoint.records.Count - 1;
        }
        private long EstimatedBytes(SimulationRecord record) => 1024 + Key(record).Length * 2L + record.observations.Count * 192L + (record.inputs?.Count ?? 0) * 96L;
        private string Key(SimulationRecord record) => SimulationIdentity.StateKey(record.state, record.modules) + "|plan:" + record.planCursor +
            (adapters.Modules.Any() || adapters.Rules.Any() ? "|revision:" + record.state.revision : "") + "|assumptions:" + SimulationIdentity.Pack(record.assumptions.OrderBy(a => a, StringComparer.Ordinal));
        private string Source(string id) => id != null && sourceIds.TryGetValue(id, out var source) ? source : id ?? "";
        private static string BranchKey(string owner, string source, string port) => "branch:" + SimulationIdentity.Pack(new[] { owner, source, port });
        private void Aggregate(int id) => AggregateObservations(id, checkpoint.records[id].observations);
        private void AggregateObservations(int id, IEnumerable<RuntimeObservation> observations)
        {
            var record = checkpoint.records[id];
            foreach (var observation in observations)
            {
                AggregateBalance(record, observation);
                if (observation.kind == "Edge")
                {
                    if (!observation.committed || !branches.TryGetValue(BranchKey(observation.ownerId, observation.id, observation.outcome), out var branch)) continue;
                    branch.occurrences++;
                    if (branch.firstRecord < 0 || branch.status == ReachStatus.ModelOnly && record.assumptions.Count == 0) branch.firstRecord = id;
                    if (record.assumptions.Count == 0) branch.status = ReachStatus.Reached; else if (branch.status != ReachStatus.Reached) branch.status = ReachStatus.ModelOnly;
                    if (Settings.mode == ExplorationMode.MonteCarlo && !checkpoint.currentRunSeen.Contains(branch.id)) { checkpoint.currentRunSeen.Add(branch.id); branch.runsReached++; }
                    continue;
                }
                if (!coverage.TryGetValue(Source(observation.id), out var row)) continue;
                if (row.kind == "EventDefinition" && observation.kind == "Node" &&
                    (!compiledKinds.TryGetValue(observation.id, out var compiledKind) || compiledKind != PresentationStepKind.Effect)) continue;
                if (observation.kind == "Advance" || observation.kind == "Branch") continue;
                if (observation.kind == "EventEligibility") { row.eligible++; continue; }
                if (observation.kind == "Economy")
                {
                    if (observation.committed) { row.moneySpent += observation.cost; row.moneyEarned += observation.income; }
                    continue;
                }
                if (observation.committed && (observation.kind == "Activity" || observation.kind == "Choice") && record.parent >= 0)
                {
                    row.daysSpent += record.state.day - checkpoint.records[record.parent].state.day;
                    row.transactionMoneyDelta += (long)record.state.money - checkpoint.records[record.parent].state.money;
                }
                if (observation.kind == "EndingEligibility")
                {
                    if (observation.outcome == "Selected" || observation.outcome == "PriorityLoss") row.eligible++;
                    if (observation.outcome == "PriorityLoss") row.priorityLosses++;
                    continue;
                }
                if ((observation.kind == "Ending" || observation.kind == "Event") && observation.committed) row.selected++;
                if (observation.kind == "Condition")
                {
                    if (observation.outcome == "True") row.trueCount++;
                    else if (observation.outcome == "False") row.falseCount++;
                    else row.unevaluatedCount++;
                }
                if (observation.kind == "Effect") { row.attempted++; if (observation.committed) row.committed++; }
                var seen = observation.kind == "Condition" ? observation.outcome == "True" || observation.outcome == "False" : observation.committed;
                if (!seen) continue;
                row.occurrences++; row.dayTotal += observation.day;
                if (row.firstDay < 0 || row.firstDay > observation.day) row.firstDay = observation.day;
                if (row.firstRecord < 0 || row.status == ReachStatus.ModelOnly && record.assumptions.Count == 0) row.firstRecord = id;
                if (record.assumptions.Count == 0) row.status = ReachStatus.Reached;
                else if (row.status != ReachStatus.Reached) row.status = ReachStatus.ModelOnly;
                row.reason = row.status == ReachStatus.Reached ? "Observed from the recorded initial state" : string.Join("; ", record.assumptions);
                if (Settings.mode == ExplorationMode.MonteCarlo && !checkpoint.currentRunSeen.Contains(row.id)) { checkpoint.currentRunSeen.Add(row.id); row.runsReached++; }
            }
        }
        private void SampleState(SimulationRecord record)
        {
            SampleRuleIntervals(record);
            if (Report.samples.Count > 0 && Report.samples[Report.samples.Count - 1].run == record.run && Report.samples[Report.samples.Count - 1].day == record.state.day)
            { Report.retainedBytes -= 96 + Report.samples[Report.samples.Count - 1].stats.Count * 48; Report.samples.RemoveAt(Report.samples.Count - 1); }
            Report.samples.Add(new StateSample { run = record.run, day = record.state.day,
                age = project.character.startingAge + record.state.day / (project.daysPerMonth * project.monthsPerYear), money = record.state.money, stats = record.state.stats });
            Report.retainedBytes += 96 + record.state.stats.Count * 48;
        }
        private void RefreshCounts()
        {
            Report.states = Settings.mode == ExplorationMode.MonteCarlo ? 0 : visited.Count;
            Report.frontier = Math.Max(0, checkpoint.frontier.Count - checkpoint.front);
            Report.runningRuns = checkpoint.activeRunRecord >= 0 ? 1 : 0;
        }
        public List<SimulationRecord> Path(int recordId)
        {
            if (recordId < 0 || recordId >= checkpoint.records.Count) throw new ArgumentOutOfRangeException(nameof(recordId));
            var result = new List<SimulationRecord>();
            for (var id = recordId; id >= 0; id = checkpoint.records[id].parent) result.Add(checkpoint.records[id]);
            result.Reverse(); return result;
        }
        public ReplayVerification VerifyPath(int recordId)
        {
            var path = Path(recordId);
            var result = ReplayInputs(path, path[0].state);
            return new ReplayVerification { matches = result.sameStates, checkedInputs = result.path.Count - 1,
                detail = result.sameStates ? "Recorded inputs reproduced the path from its initial state; models remain assumptions." : result.reason };
        }

        public EndingPathReplay ReplayInputs(IReadOnlyList<SimulationRecord> path, StateData initial)
        {
            var result = new EndingPathReplay();
            foreach (var step in ReplaySteps(path, initial, result)) { }
            return result;
        }

        public IEnumerable<SimulationRecord> ReplaySteps(IReadOnlyList<SimulationRecord> path, StateData initial, EndingPathReplay result)
        {
            if (path == null || path.Count == 0 || path.Count > Settings.maxSteps + 1) throw new ArgumentException("Invalid replay path length.");
            var replay = new SimulationExplorer(project, codec, new AnalysisManifest { initial = Report.manifest.initial, settings = Settings,
                contentFingerprint = Report.manifest.contentFingerprint, runtimeFingerprint = Report.manifest.runtimeFingerprint }, adapters);
            var session = new GameSession(project, extensions); session.Restore(initial);
            var current = new SimulationRecord { state = session.Capture(), policyRandom = path[0].policyRandom, moduleRandom = path[0].moduleRandom };
            result.path.Add(current);
            if (replay.Key(current) != Key(path[0])) { result.sameStates = false; result.firstDifference = 0; result.reason = "Initial state changed."; }
            yield return current;
            for (var i = 1; i < path.Count; i++)
            {
                if (adapters.Fingerprint != replay.Report.manifest.adapterFingerprint)
                    throw new InvalidOperationException("Adapter fingerprint changed during replay.");
                var input = path[i].input;
                if (input.kind == "Activity" && (Settings.forbiddenActivities.Contains(input.id) || Settings.allowedActivities.Count > 0 && !Settings.allowedActivities.Contains(input.id)))
                { result.sameStates = false; result.firstDifference = i; result.reason = "Activity is excluded by this test: " + input.id; break; }
                var actual = replay.Transition(current, input);
                actual.recordId = i; replay.Classify(actual); result.path.Add(actual);
                if (replay.Key(actual) != Key(path[i]) || actual.input.outcomeId != path[i].input.outcomeId || actual.result != path[i].result)
                {
                    if (result.firstDifference < 0) { result.firstDifference = i; result.reason = "First difference at input " + i + ": " + input.id + ". " + actual.reason; }
                    result.sameStates = false;
                }
                current = actual;
                yield return actual;
                if (actual.result != PlayResult.Running) break;
            }
            result.endingId = current.state.endingId;
            result.goalReached = result.endingId == Settings.targetId && !string.IsNullOrEmpty(Settings.targetId);
            if (result.path.Count != path.Count) result.sameStates = false;
        }
        public ModelComparison RecordModelObservation(string observationId, string source, StateData before, string activityId, ModuleResult observed)
        {
            if (string.IsNullOrWhiteSpace(observationId) || observationId.Length > 128 || string.IsNullOrWhiteSpace(source) || source.Length > 2048) throw new ArgumentException("Reported observations require a bounded stable deduplication ID and source.");
            if (adapters.Fingerprint != Report.manifest.adapterFingerprint) throw new InvalidOperationException("Model/provider changed; start a new comparison report.");
            var previous = Report.modelComparisons.Find(c => c.observationId == observationId);
            if (previous != null) return previous;
            if (Report.modelComparisons.Count >= 1000) throw new InvalidOperationException("External observation report budget reached.");
            var activity = project.activities.Find(a => a.id == activityId) ?? throw new ArgumentException("Unknown activity.");
            var comparison = AnalysisModelComparison.Compare(project, before, activityId, observed, adapters.Find(activity.moduleId));
            comparison.observationId = observationId; comparison.source = source;
            Report.modelComparisons.Add(comparison); Report.retainedBytes += 512 + source.Length * 2L + observationId.Length * 2L;
            if (!comparison.matches) AddProblem("Reported external observation model mismatch: " + observationId + " · " + comparison.moduleId);
            return comparison;
        }
    }
}
