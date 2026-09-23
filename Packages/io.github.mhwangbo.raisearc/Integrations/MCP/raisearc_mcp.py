"""RaiseArc MCP stdio adapter. Python 3.10+, Windows; standard library only.

Unity's opt-in named pipe owns authoring. This process never reads or writes Unity files.
Pass the pipe name shown in Studio > LLM commands with --pipe.
"""
import argparse
import ctypes
import json
import os
import struct
import sys

OPERATIONS = ("ReadProject", "CreateActivity", "CreateEvent", "AddCondition", "AddEffect",
              "CreateEnding", "AttachGameMode", "AddLocalization", "ValidateProject", "SimulatePlaythrough",
              "ListReferences", "UpsertActor", "UpsertAppearanceProfile", "UpsertAppearanceRule",
              "UpsertStageSlot", "SetEventPresentation", "SetDialogueVoice", "PreviewPresentation", "ImportLegacyActors")
SCHEMAS = {
    "SetDialogueVoice": {"targetId": {"type": "string", "description": "Original dialogue line ID"}, "locale": {"type": "string"}, "assetGuid": {"type": "string", "description": "Imported AudioClip GUID; empty removes this locale's voice (silence)."}},
    "ImportLegacyActors": {},
    "ListReferences": {"kind": {"type": "string"}, "query": {"type": "string"}, "offset": {"type": "integer", "minimum": 0}, "limit": {"type": "integer", "minimum": 1, "maximum": 200}},
    "UpsertActor": {"actor": {"type": "object", "required": ["id", "nameKey", "profileId"]}},
    "UpsertAppearanceProfile": {"profile": {"type": "object", "required": ["id", "nameKey", "slots"]}},
    "UpsertAppearanceRule": {"rule": {"type": "object", "required": ["id", "nameKey", "actorId", "slots"]}},
    "UpsertStageSlot": {"stageSlot": {"type": "object", "required": ["id", "nameKey"]}},
    "SetEventPresentation": {"targetId": {"type": "string"}, "steps": {"type": "array", "maxItems": 512, "items": {
        "type": "object", "required": ["id", "nameKey"], "properties": {
            "id": {"type": "string"}, "nameKey": {"type": "string"},
            "kind": {"type": "integer", "enum": [0, 1, 2], "description": "0 Dialogue, 1 Image only, 2 Choice"},
            "nextStepId": {"type": "string", "description": "Empty = list order; $end = finish event; otherwise same-event step ID."},
            "backgroundChange": {"type": "integer", "enum": [0, 1, 2], "description": "0 Replace (legacy default), 1 Keep, 2 Clear"},
            "actorsChange": {"type": "integer", "enum": [0, 1, 2]},
            "imagesChange": {"type": "integer", "enum": [0, 1, 2]},
            "speakerActorId": {"type": "string"}, "backgroundKey": {"type": "string"},
            "actors": {"type": "array", "items": {"type": "object", "required": ["actorId", "slotId"]}},
            "images": {"type": "array", "maxItems": 32, "items": {"type": "object", "required": ["id", "resourceKey"], "properties": {
                "id": {"type": "string"}, "resourceKey": {"type": "string"},
                "x": {"type": "number", "minimum": 0, "maximum": 1}, "y": {"type": "number", "minimum": 0, "maximum": 1},
                "width": {"type": "number", "exclusiveMinimum": 0, "maximum": 1}, "height": {"type": "number", "exclusiveMinimum": 0, "maximum": 1},
                "opacity": {"type": "number", "minimum": 0, "maximum": 1},
                "plane": {"type": "integer", "enum": [0, 1], "description": "0 Behind actors, 1 In front of actors; list order within plane."}
            }}},
            "choices": {"type": "array", "maxItems": 32, "items": {"type": "object", "required": ["id", "nameKey"], "properties": {
                "id": {"type": "string"}, "nameKey": {"type": "string"}, "nextStepId": {"type": "string"},
                "conditions": {"type": "array", "items": {"type": "object"}}, "effects": {"type": "array", "items": {"type": "object"}}
            }}}
        }
    }}},
    "PreviewPresentation": {"targetId": {"type": "string"}, "state": {"type": "object"}},
    "ReadProject": {}, "ValidateProject": {},
    "CreateActivity": {"activity": {"type": "object"}},
    "CreateEvent": {"eventDefinition": {"type": "object"}},
    "CreateEnding": {"ending": {"type": "object"}},
    "AddCondition": {"targetId": {"type": "string"}, "condition": {"type": "object"}},
    "AddEffect": {"targetId": {"type": "string"}, "effect": {"type": "object"}},
    "AttachGameMode": {"targetId": {"type": "string"}, "moduleId": {"type": "string"}},
    "AddLocalization": {"localization": {"type": "object"}},
    "SimulatePlaythrough": {"seed": {"type": "integer"}, "runs": {"type": "integer", "minimum": 1, "maximum": 1000}, "maxSteps": {"type": "integer", "minimum": 1, "maximum": 100000}},
}
READ_ONLY = {"ReadProject", "ValidateProject", "SimulatePlaythrough", "ListReferences", "PreviewPresentation"}

# Graph edits propose a reviewed change set. Only CommitChangeSet persists it.
GRAPH_QUERIES = ("GetExtensions", "GetContentIndex", "SearchContent", "GetGraphProjection", "GetReferences", "GetProblems", "ValidateGraph", "GetLocalizationStatus", "GetDeletionImpact", "BeginChangeSet", "PreviewChangeSet", "RollbackChangeSet", "SimulateFromNode", "GetExecutionTrace", "ExplainConditionFailure")
GRAPH_EDITS = ("CreateNode", "InsertNodeBetween", "ConnectNodes", "DisconnectNodes", "CreateChoice", "CreateBranch", "DuplicateFlow", "DeleteWithImpactCheck", "RenameContent")
OPERATIONS += GRAPH_QUERIES + GRAPH_EDITS + ("CommitChangeSet",)
READ_ONLY.update(GRAPH_QUERIES)
SCHEMAS.update({
    "GetExtensions": {},
    "GetContentIndex": {"locale": {"type": "string"}},
    "SearchContent": {"query": {"type": "string"}, "locale": {"type": "string"}, "offset": {"type": "integer", "minimum": 0}, "limit": {"type": "integer", "minimum": 1, "maximum": 500}},
    "GetGraphProjection": {"eventId": {"type": "string"}, "locale": {"type": "string"}},
    "GetReferences": {"targetId": {"type": "string"}, "inbound": {"type": "boolean"}},
    "GetProblems": {}, "ValidateGraph": {}, "BeginChangeSet": {},
    "GetLocalizationStatus": {"targetId": {"type": "string"}},
    "GetDeletionImpact": {"targetId": {"type": "string"}},
    "PreviewChangeSet": {"changeSet": {"type": "object", "required": ["baseRevision", "edits"]}},
    "RollbackChangeSet": {"changeSet": {"type": "object", "required": ["baseRevision", "edits"]}},
    "CommitChangeSet": {"changeSet": {"type": "object", "required": ["baseRevision", "edits"]}, "previewToken": {"type": "string"}},
})
for _name in ("SimulateFromNode", "GetExecutionTrace", "ExplainConditionFailure"):
    SCHEMAS[_name] = {"eventId": {"type": "string"}, "nodeId": {"type": "string"}, "state": {"type": "object"}, "choiceIds": {"type": "array", "items": {"type": "string"}}, "maxSteps": {"type": "integer", "minimum": 1, "maximum": 2048}}
for _name in GRAPH_EDITS:
    SCHEMAS[_name] = {"graphEdit": {"type": "object", "description": "Proposal fields: eventId, targetId, portId (next/false/choice ID), nextId, node, choice, locale, text, impactToken. Stable IDs required for new nodes. Read graph projection first."}}
SCHEMAS["SetEventPresentation"]["steps"]["items"]["properties"]["kind"] = {"type": "integer", "enum": [0, 1, 2, 3, 4, 5], "description": "Dialogue, Image, Choice, Condition, Effect, Merge"}
SCHEMAS["SetEventPresentation"]["steps"]["items"]["properties"].update({
    "falseStepId": {"type": "string", "description": "Condition false output: step ID, $end, or $choices."},
    "conditions": {"type": "array", "items": {"type": "object"}},
    "effects": {"type": "array", "items": {"type": "object"}},
})


SIMULATION_TOOLS = ("SimulationCapabilities", "StartSimulation", "GetSimulationProgress", "PauseSimulation",
                    "ResumeSimulation", "CancelSimulation", "ForgetSimulation", "SaveSimulationCheckpoint", "LoadSimulationCheckpoint",
                    "GetSimulationCoverage", "GetSimulationBranches", "GetSimulationOutcomes", "GetUnverifiedContent", "GetSimulationPath", "VerifySimulationPath", "ExportSimulationReport")
OPERATIONS += SIMULATION_TOOLS
READ_ONLY.update(SIMULATION_TOOLS)
SIMULATION_MUTATIONS = {"StartSimulation", "PauseSimulation", "ResumeSimulation", "CancelSimulation", "ForgetSimulation", "SaveSimulationCheckpoint", "LoadSimulationCheckpoint"}
for _name in SIMULATION_TOOLS:
    SCHEMAS[_name] = {"jobId": {"type": "string"}}
SCHEMAS["SimulationCapabilities"] = {}
SCHEMAS["StartSimulation"] = {
    "settings": {"type": "object", "description": "mode: 0 QuickCheck, 1 TargetSearch, 2 Exhaustive, 3 MonteCarlo; targetId; policy uniform-v1/weighted-v1; weights [{id,weight}]; seed,runs,maxStates,maxTransitions,maxSteps,horizonDays,maxSeconds,maxMemoryMiB,maxCheckpointMiB; deathEndingIds,failureEndingIds. Read capabilities first."},
    "useInitial": {"type": "boolean", "description": "Explicitly opt into the supplied initial StateData; false starts a fresh actual game."},
    "initial": {"type": "object"},
}
for _name in ("GetSimulationCoverage", "GetSimulationBranches", "GetUnverifiedContent", "GetSimulationOutcomes", "GetSimulationPath"):
    SCHEMAS[_name].update({"offset": {"type": "integer", "minimum": 0}, "limit": {"type": "integer", "minimum": 1, "maximum": 500}})
SCHEMAS["GetSimulationPath"].update({"recordId": {"type": "integer", "minimum": 0}, "targetId": {"type": "string"}})
SCHEMAS["VerifySimulationPath"]["recordId"] = {"type": "integer", "minimum": 0}

ENDING_TEST_TOOLS = ("CreateEndingTest", "ReadEndingTest", "UpdateEndingTest", "ListEndingTests", "ValidateEndingTest",
                     "RunEndingTest", "GetEndingTestResult", "PreserveEndingTestResult", "GetEndingTestPath", "ReplayEndingTest", "GetEndingReplay", "CancelEndingReplay")
OPERATIONS += ENDING_TEST_TOOLS
READ_ONLY.update(ENDING_TEST_TOOLS)
SIMULATION_MUTATIONS.update({"CreateEndingTest", "UpdateEndingTest", "RunEndingTest", "PreserveEndingTestResult", "ReplayEndingTest", "CancelEndingReplay"})
for _name in ENDING_TEST_TOOLS:
    SCHEMAS[_name] = {}
for _name in ("CreateEndingTest", "UpdateEndingTest", "ValidateEndingTest"):
    SCHEMAS[_name]["definition"] = {"type": "object", "description": "Version 1: id,name,projectId,endingId,start=new-game,overrideMoney,money,stats=[{id,value}],settings. Reads saved content; overrides never change it."}
for _name in ("CreateEndingTest", "UpdateEndingTest"):
    SCHEMAS[_name]["expectedTestRevision"] = {"type": "integer", "minimum": 0}
for _name in ("ReadEndingTest", "UpdateEndingTest", "RunEndingTest"):
    SCHEMAS[_name]["testId"] = {"type": "string"}
for _name in ("GetEndingTestResult", "PreserveEndingTestResult", "GetEndingTestPath", "GetEndingReplay", "CancelEndingReplay"):
    SCHEMAS[_name]["jobId"] = {"type": "string"}
SCHEMAS["GetEndingTestPath"].update({"offset": {"type": "integer", "minimum": 0}, "limit": {"type": "integer", "minimum": 1, "maximum": 100}})
SCHEMAS["GetEndingReplay"].update({"offset": {"type": "integer", "minimum": 0}, "limit": {"type": "integer", "minimum": 1, "maximum": 100}})
SCHEMAS["ReplayEndingTest"] = {"resultId": {"type": "string"}, "currentCandidate": {"type": "boolean"}}


SCREEN_TOOLS = ("ReadGameScreen", "UpdateGameScreen")
OPERATIONS += SCREEN_TOOLS
READ_ONLY.update(SCREEN_TOOLS)
SIMULATION_MUTATIONS.add("UpdateGameScreen")
SCHEMAS["ReadGameScreen"] = {}
SCHEMAS["UpdateGameScreen"] = {
    "expectedScreenRevision": {"type": "integer", "minimum": 0},
    "screen": {"type": "object", "description": "Complete version 1 screen definition returned by ReadGameScreen. Changes presentation only; content revision is independent."},
}


BALANCE_TOOLS = ("BalanceCapabilities", "CreateBalanceTest", "ReadBalanceTest", "UpdateBalanceTest", "ListBalanceTests", "ValidateBalanceTest", "RunBalanceTest",
                 "GetBalanceResult", "GetBalanceMetrics", "GetBalanceCoverage", "GetBalanceRuns", "PauseBalanceTest", "ResumeBalanceTest", "CancelBalanceTest", "CloseBalanceTest",
                 "SaveBalanceCheckpoint", "LoadBalanceCheckpoint", "PreserveBalanceResult", "ExportBalanceReport", "GetBalancePath", "ReplayBalanceRun", "CompareBalanceResults",
                 "SaveTestSuite", "ReadTestSuite", "ListTestSuites", "RunTestSuite", "GetTestSuiteResult", "CancelTestSuite")
OPERATIONS += BALANCE_TOOLS
READ_ONLY.update(BALANCE_TOOLS)
SIMULATION_MUTATIONS.update(set(BALANCE_TOOLS) - {"BalanceCapabilities", "ReadBalanceTest", "ListBalanceTests", "ValidateBalanceTest", "GetBalanceResult", "GetBalanceMetrics", "GetBalanceCoverage", "GetBalanceRuns", "GetBalancePath", "CompareBalanceResults", "ReadTestSuite", "ListTestSuites", "GetTestSuiteResult"})
for _name in BALANCE_TOOLS:
    SCHEMAS[_name] = {}
for _name in ("CreateBalanceTest", "UpdateBalanceTest", "ValidateBalanceTest"):
    SCHEMAS[_name]["definition"] = {"type": "object"}
for _name in ("CreateBalanceTest", "UpdateBalanceTest", "SaveTestSuite"):
    SCHEMAS[_name]["expectedTestRevision"] = {"type": "integer", "minimum": 0}
for _name in ("ReadBalanceTest", "UpdateBalanceTest", "RunBalanceTest"):
    SCHEMAS[_name]["testId"] = {"type": "string"}
SCHEMAS["RunBalanceTest"]["candidateGuid"] = {"type": "string"}
for _name in ("GetBalanceResult", "GetBalanceMetrics", "GetBalanceCoverage", "GetBalanceRuns", "PauseBalanceTest", "ResumeBalanceTest", "CancelBalanceTest", "CloseBalanceTest", "SaveBalanceCheckpoint", "PreserveBalanceResult", "ExportBalanceReport", "GetTestSuiteResult", "CancelTestSuite"):
    SCHEMAS[_name]["jobId"] = {"type": "string"}
for _name in ("GetBalanceMetrics", "GetBalanceCoverage", "GetBalanceRuns", "GetBalancePath"):
    SCHEMAS[_name].update({"policyIndex": {"type": "integer", "minimum": 0}, "offset": {"type": "integer", "minimum": 0}, "limit": {"type": "integer", "minimum": 1, "maximum": 100}})
for _name in ("GetBalancePath", "ReplayBalanceRun", "LoadBalanceCheckpoint", "CompareBalanceResults"):
    SCHEMAS[_name]["resultId"] = {"type": "string"}
for _name in ("GetBalancePath", "ReplayBalanceRun"):
    SCHEMAS[_name].update({"policyIndex": {"type": "integer", "minimum": 0}, "runId": {"type": "integer", "minimum": 0}})
SCHEMAS["ReplayBalanceRun"]["currentCandidate"] = {"type": "boolean"}
SCHEMAS["ReplayBalanceRun"].update({"offset": {"type": "integer", "minimum": 0}, "limit": {"type": "integer", "minimum": 1, "maximum": 100}})
SCHEMAS["CompareBalanceResults"]["otherResultId"] = {"type": "string"}
SCHEMAS["CompareBalanceResults"].update({"offset": {"type": "integer", "minimum": 0}, "limit": {"type": "integer", "minimum": 1, "maximum": 100}})
SCHEMAS["SaveTestSuite"].update({"suite": {"type": "object"}, "suiteId": {"type": "string"}})
for _name in ("ReadTestSuite", "RunTestSuite"):
    SCHEMAS[_name]["suiteId"] = {"type": "string"}


def tool_schema(name):
    props = dict(SCHEMAS[name])
    required = [] if name in READ_ONLY else list(props) + ["expectedRevision"]
    if name == "PreviewPresentation":
        required = ["targetId"]
    if name in ("GetGraphProjection", "SimulateFromNode", "GetExecutionTrace", "ExplainConditionFailure"):
        required = ["eventId"]
    if name in ("GetReferences", "GetDeletionImpact"):
        required = ["targetId"]
    if name in ("PreviewChangeSet", "RollbackChangeSet"):
        required = ["changeSet"]
    if name in SIMULATION_TOOLS and name not in ("SimulationCapabilities", "StartSimulation"):
        required = ["jobId"]
    if name == "VerifySimulationPath":
        required = ["jobId", "recordId"]
    if name in ENDING_TEST_TOOLS:
        required = [key for key in props if key not in ("offset", "limit")]
    if name in SCREEN_TOOLS:
        required = list(props)
    if name in BALANCE_TOOLS:
        required = [key for key in props if key not in ("offset", "limit", "candidateGuid", "currentCandidate") and not (name == "SaveTestSuite" and key == "suiteId")]
    if name not in READ_ONLY:
        props["expectedRevision"] = {"type": "integer", "minimum": 0}
    description = (f"{name} through RaiseArc Simulation Explorer. Analysis jobs do not edit authored data. Read manifest scope, completeness, assumptions and started-run denominator; notFound is not unreachable. Mode 1 searches settings.targetId from the recorded initial state." if name in SIMULATION_TOOLS else
                   f"{name} through Unity RaiseArc Authoring API. ReadProject returns current revision and content; use it before mutations.")
    if name in ENDING_TEST_TOOLS:
        description = f"{name} through the shared RaiseArc ending-test authoring API. Test revisions are separate from content revisions. RunEndingTest returns a job ID; poll GetEndingTestResult and use CancelSimulation to cancel. ReplayEndingTest also returns a job ID; poll GetEndingReplay or CancelEndingReplay. A completed job is not proof of goal attainment. Original results remain unchanged."
    if name in SCREEN_TOOLS:
        description = f"{name} through the same RaiseArc screen authoring API as Make a game. Requires an existing generated screen. Use screenRevision for updates, not contentRevision; player progress is unchanged."
    if name in BALANCE_TOOLS:
        description = f"{name} through the saved RaiseArc balance-test API shared with Simulation Explorer. Separate test/content revisions and job/criteria outcomes. Counts include all started runs; incomplete or optional-stopped samples are inconclusive. Original artifacts remain immutable. Paths use policyIndex and stable runId. Exports contain original content/checkpoints."
    return {"name": name, "description": description,
            "inputSchema": {"type": "object", "properties": props, "required": required, "additionalProperties": False},
            "annotations": {"readOnlyHint": name in READ_ONLY and name not in SIMULATION_MUTATIONS, "destructiveHint": False, "openWorldHint": False}}


def read_exact(pipe, size):
    chunks = bytearray()
    while len(chunks) < size:
        chunk = pipe.read(size - len(chunks))
        if not chunk:
            raise ConnectionError("Unity disconnected before returning a response")
        chunks.extend(chunk)
    return bytes(chunks)


def call_unity(pipe_name, name, arguments):
    if name not in OPERATIONS or not isinstance(arguments, dict):
        raise ValueError("Unknown operation or invalid arguments")
    schema = tool_schema(name)["inputSchema"]
    if any(key not in schema["properties"] for key in arguments) or any(key not in arguments for key in schema["required"]):
        raise ValueError("Arguments do not match the tool schema")
    command = dict(arguments, operation=name)
    payload = json.dumps(command, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    if len(payload) > 262144:
        raise ValueError("Command exceeds 256 KiB")
    # CurrentUserOnly is enforced by Unity. The pipe name is supplied by the user, never a file path.
    pipe_path = "\\\\.\\pipe\\" + pipe_name
    wait = ctypes.WinDLL("kernel32", use_last_error=True).WaitNamedPipeW
    wait.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32]
    wait.restype = ctypes.c_int
    if not wait(pipe_path, 5000):
        raise ctypes.WinError(ctypes.get_last_error())
    with open(pipe_path, "r+b", buffering=0) as pipe:
        pipe.write(struct.pack("<I", len(payload)) + payload)
        size = struct.unpack("<I", read_exact(pipe, 4))[0]
        if size > 16 * 1024 * 1024:
            raise ValueError("Response size exceeds limit")
        return json.loads(read_exact(pipe, size))


def dispatch(request, pipe_name):
    method = request.get("method")
    if method == "initialize":
        return {"protocolVersion": "2024-11-05", "capabilities": {"tools": {}}, "serverInfo": {"name": "raisearc", "version": "0.1.0-preview.0"}}
    if method == "ping":
        return {}
    if method == "tools/list":
        return {"tools": [tool_schema(name) for name in OPERATIONS]}
    if method == "tools/call":
        params = request.get("params", {})
        result = call_unity(pipe_name, params.get("name"), params.get("arguments", {}))
        return {"content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}], "isError": result.get("success") is False}
    raise ValueError("Unsupported method: " + str(method))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pipe", required=True)
    args = parser.parse_args()
    if not args.pipe.startswith("princess-studio-") or not all(c.isalnum() or c == "-" for c in args.pipe):
        parser.error("Use the pipe name displayed by Unity")
    if os.name != "nt":
        parser.error("This named-pipe adapter currently supports Windows only")
    while True:
        line = sys.stdin.buffer.readline(262145)
        if not line:
            break
        request = None
        try:
            if len(line) > 262144:
                raise ValueError("MCP request size limit exceeded")
            request = json.loads(line)
            if not isinstance(request, dict):
                raise ValueError("JSON-RPC request must be an object")
            if "id" not in request:
                continue
            response = {"jsonrpc": "2.0", "id": request["id"], "result": dispatch(request, args.pipe)}
        except Exception as error:
            response = {"jsonrpc": "2.0", "id": request.get("id") if isinstance(request, dict) else None,
                        "error": {"code": -32603, "message": str(error)}}
        print(json.dumps(response, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
