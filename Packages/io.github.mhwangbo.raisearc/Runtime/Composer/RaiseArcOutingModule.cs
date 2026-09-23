using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PrincessStudio.Core;
using RaiseArc.Analysis;

namespace RaiseArc.Samples
{
    /// <summary>A small real module whose seeded gameplay rule is also available without a Scene.</summary>
    public sealed class OutingModule : IGameModeModule, IModuleSampler
    {
        public const string ModuleId = "sample-outing";
        private readonly uint seed;
        public string Id => ModuleId;
        public OutingModule(uint seed = 1) { this.seed = seed; }
        public ModuleAnalysisContract Contract => new ModuleAnalysisContract
        {
            moduleId = Id, provider = "RaiseArc sample", version = "1", fingerprint = "outing-bitmask-rule-v1",
            source = "Samples/Runtime/RaiseArcOutingModule.cs", editOwner = "Imported sample copy", kind = ModuleAnalysisKind.Headless,
            inputContract = "StateSnapshot + explicit uint seed", outputContract = "One day; charm +1 or +2",
            stateContract = "Stateless", randomContract = "Low four seed bits == 0 selects rare outcome",
            exhaustiveOutcomes = true, catalogCompleteness = CatalogCompleteness.Complete,
            reads = new List<string>(), writes = new List<string> { "Stat:charm" }, contentIds = new List<string> { "market", "rare-meeting" }
        };
        public Task<ModuleResult> ExecuteAsync(ModuleContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = Resolve(seed); outcome.result.sessionId = context.SessionId;
            return Task.FromResult(outcome.result);
        }
        public AnalysisOutcome Sample(StateSnapshot state, string activityId, string savedState, uint randomSeed) => Resolve(randomSeed);
        public IReadOnlyList<AnalysisOutcome> Outcomes(StateSnapshot state, string activityId, string savedState) => new[] { Outcome(false), Outcome(true) };
        private static AnalysisOutcome Resolve(uint randomSeed) => Outcome((randomSeed & 15) == 0);
        private static AnalysisOutcome Outcome(bool rare) => new AnalysisOutcome
        {
            id = rare ? "rare" : "ordinary", probability = rare ? 1.0 / 16 : 15.0 / 16,
            result = new ModuleResult { elapsedDays = 1, effects = new List<EffectSpec> { new EffectSpec { kind = ValueKind.Stat, target = "charm", value = rare ? 2 : 1 } } },
            contentIds = rare ? new List<string> { "market", "rare-meeting" } : new List<string> { "market" }
        };
    }
}
