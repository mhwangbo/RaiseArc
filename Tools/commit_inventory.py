"""Inventory package bytes from a committed Git tree, never Windows worktree bytes."""

import hashlib
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = "Packages/io.github.mhwangbo.raisearc/"
REVIEW = ROOT / "review"
FOCUSED_METHODS = set("""
Editor/Graph/Tests/RaiseArcScreenCompositionTests.cs
Editor/Graph/Tests/RaiseArcTimePlanTests.cs
Editor/Integration/RaiseArcScreenComposer.cs
Editor/Integration/RaiseArcTimePlanEditor.cs
Editor/Graph/RaiseArcBalancePanel.cs
Editor/Graph/RaiseArcEndingTestPanel.cs
Editor/Integration/RaiseArcEndingReplayJobs.cs
Editor/Localization/LocalizationTableBridge.cs
Editor/Localization/RaiseArcGameScreenBuild.cs
Editor/Localization/RaiseArcGameSetupWindow.cs
Editor/Shared/StudioText.cs
Runtime/Composer/ForestSampleEndpoint.cs
Runtime/Composer/RaiseArcGiftExtension.cs
Runtime/Composer/RaiseArcIntegrationScreen.cs
Runtime/Composer/RaiseArcOutingModule.cs
Runtime/Composer/SampleGameController.Template.cs
Runtime/Composer/SampleGameController.cs
Runtime/Core/Authoring/AuthoringService.cs
Runtime/Core/Authoring/ContentIndex.cs
Runtime/Core/Authoring/GraphProjection.cs
Runtime/Core/Authoring/PresentationAuthoring.cs
Runtime/Core/Authoring/RaiseArcActivityCopy.cs
Runtime/Core/Content/EventSequence.cs
Runtime/Core/Content/PresentationDefinition.cs
Runtime/Core/Content/ProjectDefinition.cs
Runtime/Core/Content/RaiseArcActivityRules.cs
Runtime/Core/Content/RaiseArcReusableRules.cs
Runtime/Core/Modules/Contracts.cs
Runtime/Core/Modules/RaiseArcExtensionDefinition.cs
Runtime/Core/Simulation/RaiseArcActivityAvailability.cs
Runtime/Core/Simulation/RaiseArcBalanceLedger.cs
Runtime/Core/Simulation/RaiseArcSimulationBalance.cs
Runtime/Core/Simulation/RaiseArcSimulationContracts.cs
Runtime/Core/Simulation/RuleEngine.cs
Runtime/Unity/PresentationSkin.cs
Runtime/Unity/PresentationStage.cs
Runtime/Unity/RaiseArcComposedScreen.cs
Runtime/Unity/RaiseArcExtensionAsset.cs
Runtime/Unity/RaiseArcGameScreenSettings.cs
Runtime/Unity/RaiseArcScreenComposition.cs
Runtime/Core/Authoring/WorkbenchAuthoring.cs
Runtime/Core/Content/RaiseArcFlowReuse.cs
Runtime/Core/Content/RaiseArcGamePlan.cs
Runtime/Core/Content/RaiseArcTimePlanRules.cs
Runtime/Core/Simulation/GameSession.cs
Runtime/Core/Simulation/GameSession.Presentation.cs
Runtime/Core/Simulation/GameSession.Trace.cs
Runtime/Core/Simulation/GameState.cs
Runtime/Core/Simulation/RaiseArcAnalysisAdapters.cs
Runtime/Core/Simulation/RaiseArcBalanceTest.cs
Runtime/Core/Simulation/RaiseArcEndingTest.cs
Runtime/Core/Simulation/RaiseArcGameplayRandom.cs
Runtime/Core/Simulation/RaiseArcModelComparison.cs
Runtime/Core/Simulation/RaiseArcSimulationExplorer.cs
Runtime/Core/Simulation/RaiseArcSimulationIdentity.cs
Runtime/Core/Simulation/RaiseArcRuntimeObservations.cs
Runtime/Core/Validation/EventSequenceValidator.cs
Runtime/Core/Validation/PlaythroughSimulator.cs
Runtime/Core/Validation/PresentationValidator.cs
Runtime/Core/Validation/ProjectValidator.cs
Runtime/Localization/LocalizedAssetBindings.cs
Runtime/Localization/LocalizedLabelBinding.cs
Runtime/Localization/RaiseArcDialogueVoice.cs
Runtime/NativeUI/RaiseArcGame.cs
Runtime/NativeUI/RaiseArcUIBindings.cs
Runtime/NativeUI/RaiseArcUIConnection.cs
Runtime/NativeUI/RaiseArcUIDocument.cs
Runtime/Unity/GameProjectAsset.cs
Runtime/Unity/JsonRootFields.cs
Runtime/Unity/RaiseArcPlanningRehearsal.cs
Runtime/Unity/RaiseArcSessionHost.cs
Runtime/Unity/Persistence/SaveStore.cs
Runtime/Unity/Localization/TranslationCsv.cs
Runtime/Unity/Modules/CustomSceneAdapter.cs
Integrations/MCP/raisearc_mcp.py
Editor/Graph/GraphWorkbenchWindow.cs
Editor/Graph/GraphWorkbenchWindow.Diagnostics.cs
Editor/Graph/GraphWorkbenchWindow.Graph.cs
Editor/Graph/GraphWorkbenchWindow.Inspector.cs
Editor/Graph/RaiseArcSimulationWindow.cs
Editor/Graph/Tests/GraphWorkbenchTests.cs
Editor/Graph/Tests/KoreanWorkbenchTests.cs
Editor/Graph/Tests/RaiseArcSimulationWindowTests.cs
Editor/Integration/AuthoringPipeServer.cs
Editor/Integration/RaiseArcBalanceTests.cs
Editor/Integration/RaiseArcEndingTestAsset.cs
Editor/Integration/RaiseArcSimulationJobs.cs
Editor/Integration/WindowsPipeFactory.cs
Editor/Localization/GameScreenPrefabBuilder.cs
Editor/Localization/RaiseArcBasicGameSetup.cs
Editor/StudioWindow.cs
Editor/StudioWindow.GameScreen.cs
Editor/StudioWindow.References.cs
Editor/StudioWindow.Tools.cs
""".split())


def git(*args: str) -> bytes:
    return subprocess.check_output(["git", "-c", f"safe.directory={ROOT.as_posix()}", *args], cwd=ROOT)


def write_tsv(path: Path, rows: list[tuple[str, ...]]) -> None:
    content = "".join("\t".join(row) + "\n" for row in rows)
    if "--check" in sys.argv:
        if path.read_text(encoding="utf-8") != content:
            raise SystemExit(f"Committed inventory differs: {path.relative_to(ROOT)}")
    else:
        path.write_text(content, encoding="utf-8", newline="\n")


def main() -> None:
    paths = git("ls-tree", "-r", "--name-only", "HEAD", PACKAGE).decode().splitlines()
    inventory = []
    source_hashes = {}
    for path in paths:
        data = git("show", f"HEAD:{path}")
        digest = hashlib.sha256(data).hexdigest().upper()
        inventory.append((digest, str(len(data)), path))
        if path.endswith((".cs", ".py")):
            source_hashes[path.removeprefix(PACKAGE)] = digest
    write_tsv(REVIEW / "package-sha256.tsv", inventory)

    prior = {}
    for line in (REVIEW / "source-review-status.tsv").read_text(encoding="utf-8").splitlines():
        cells = line.split("\t")
        if len(cells) >= 2:
            # Supports both the original status/path and new status/hash/path forms.
            prior[cells[-1]] = cells[0]
    rows = []
    for path, digest in sorted(source_hashes.items()):
        status = "focused-method-read" if path in FOCUSED_METHODS else prior.get(path, "inventory-and-automated-scan-only")
        rows.append((status, digest, path))
    write_tsv(REVIEW / "source-review-status.tsv", rows)
    print(f"Committed package: {len(inventory)} files; {len(rows)} C#/Python sources")


if __name__ == "__main__":
    main()
