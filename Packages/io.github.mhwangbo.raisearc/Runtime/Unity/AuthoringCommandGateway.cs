using System;
using PrincessStudio.Core;
using UnityEngine;

namespace PrincessStudio.Unity
{
    [Serializable]
    public sealed class AuthoringCommand
    {
        public string operation = "";
        public int expectedRevision = -1;
        public string targetId = "", moduleId = "";
        public string assetGuid = "";
        public ActivityDefinition activity;
        public EventDefinition eventDefinition;
        public EndingDefinition ending;
        public ConditionSpec condition;
        public EffectSpec effect;
        public TranslationEntry localization;
        public ActorDefinition actor;
        public AppearanceProfile profile;
        public AppearanceRule rule;
        public StageSlot stageSlot;
        public System.Collections.Generic.List<PresentationStep> steps;
        public StateData state;
        public AuthoringChangeSet changeSet;
        public GraphEdit graphEdit;
        public string eventId = "", nodeId = "", locale = "", previewToken = "";
        public bool inbound = true;
        public System.Collections.Generic.List<string> choiceIds;
        public string kind = "", query = "";
        public int offset, limit = 50;
        public int seed = 1, runs = 32, maxSteps = 10000;
    }
    [Serializable]
    public sealed class CommandResponse
    {
        public bool success;
        public int revision;
        public string error = "";
        public ValidationReport validation;
        public SimulationReport simulation;
        public ReferencePage references;
        public ActorPresentationState presentation;
        public ContentIndexData contentIndex;
        public GraphProjection graph;
        public System.Collections.Generic.List<ContentReference> contentReferences;
        public System.Collections.Generic.List<LocalizationStatus> localizationStatus;
        public AuthoringChangeSet changeSet;
        public ChangeSetPreview changePreview;
        public DeletionImpact deletionImpact;
        public ExecutionTrace trace;
        public System.Collections.Generic.List<RaiseArc.Core.ExtensionDefinition> extensions;
    }
    /// <summary>Local JSON gateway. A host may expose this through authenticated MCP; no network listener or eval.</summary>
    public sealed class AuthoringCommandGateway
    {
        private readonly AuthoringService service;
        public AuthoringCommandGateway(AuthoringService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }
        public string ExecuteJson(string json)
        {
            var response = new CommandResponse();
            try
            {
                if (string.IsNullOrWhiteSpace(json) || json.Length > 262144)
                    throw new ArgumentException("Command size limit: 256 KiB characters.");
                var c = JsonUtility.FromJson<AuthoringCommand>(json) ?? throw new ArgumentException("Missing command.");
                if (!IsReadOnly(c.operation) && c.expectedRevision < 0)
                    throw new ArgumentException("Mutating commands require expectedRevision.");
                switch (c.operation)
                {
                    case "GetExtensions": response.extensions = service.GetExtensions(); break;
                    case "GetContentIndex": response.contentIndex = service.GetContentIndex(c.locale); break;
                    case "SearchContent": response.contentIndex = service.SearchContent(c.query, c.locale, c.offset, c.limit); break;
                    case "GetGraphProjection": response.graph = service.GetGraphProjection(c.eventId, c.locale); break;
                    case "GetReferences": response.contentReferences = service.GetReferences(c.targetId, c.inbound); break;
                    case "GetProblems": case "ValidateGraph": response.validation = service.GetProblems(); break;
                    case "GetLocalizationStatus": response.localizationStatus = service.GetLocalizationStatus(c.targetId); break;
                    case "GetDeletionImpact": response.deletionImpact = service.GetDeletionImpact(c.targetId); break;
                    case "BeginChangeSet": response.changeSet = service.BeginChangeSet(); break;
                    case "PreviewChangeSet": response.changePreview = service.PreviewChangeSet(c.changeSet); break;
                    case "CommitChangeSet": service.CommitChangeSet(c.changeSet, c.previewToken, c.expectedRevision); break;
                    case "RollbackChangeSet": service.RollbackChangeSet(c.changeSet); response.changeSet = c.changeSet; break;
                    case "SimulateFromNode": case "GetExecutionTrace": case "ExplainConditionFailure":
                        response.trace = service.SimulateFromNode(c.eventId, c.nodeId, JsonRootFields.Contains(json, "state") ? c.state : null, c.choiceIds, Math.Min(c.maxSteps, 2048)); break;
                    case "CreateNode": case "InsertNodeBetween": case "ConnectNodes": case "DisconnectNodes": case "CreateChoice": case "CreateBranch": case "DuplicateFlow": case "DeleteWithImpactCheck": case "RenameContent":
                        var edit = c.graphEdit ?? throw new ArgumentException("graphEdit required"); edit.operation = c.operation;
                        response.changeSet = new AuthoringChangeSet { baseRevision = c.expectedRevision, edits = new System.Collections.Generic.List<GraphEdit> { edit } };
                        response.changePreview = service.PreviewChangeSet(response.changeSet); break;
                    case "ImportLegacyActors": service.ImportLegacyActors(c.expectedRevision); break;
                    case "UpsertActor": service.UpsertActor(c.actor, c.expectedRevision); break;
                    case "UpsertAppearanceProfile": service.UpsertAppearanceProfile(c.profile, c.expectedRevision); break;
                    case "UpsertAppearanceRule": service.UpsertAppearanceRule(c.rule, c.expectedRevision); break;
                    case "UpsertStageSlot": service.UpsertStageSlot(c.stageSlot, c.expectedRevision); break;
                    case "SetEventPresentation": service.SetEventPresentation(c.targetId, c.steps, c.expectedRevision); break;
                    case "SetDialogueVoice": service.SetDialogueVoice(c.targetId, c.locale, c.assetGuid, c.expectedRevision); break;
                    case "ListReferences": response.references = service.ListReferences(c.kind, c.query, c.offset, c.limit); break;
                    case "PreviewPresentation": response.presentation = service.PreviewPresentation(c.targetId, JsonRootFields.Contains(json, "state") ? c.state : null); break;
                    case "CreateActivity":
                        service.CreateActivity(c.activity ?? throw new ArgumentException("activity required"), c.expectedRevision);
                        break;
                    case "CreateEvent":
                        service.CreateEvent(c.eventDefinition ?? throw new ArgumentException("eventDefinition required"), c.expectedRevision);
                        break;
                    case "CreateEnding":
                        service.CreateEnding(c.ending ?? throw new ArgumentException("ending required"), c.expectedRevision);
                        break;
                    case "AddCondition":
                        service.AddCondition(c.targetId, c.condition ?? throw new ArgumentException("condition required"), c.expectedRevision);
                        break;
                    case "AddEffect":
                        service.AddEffect(c.targetId, c.effect ?? throw new ArgumentException("effect required"), c.expectedRevision);
                        break;
                    case "AttachGameMode":
                        service.AttachGameMode(c.targetId, c.moduleId, c.expectedRevision);
                        break;
                    case "AddLocalization":
                        service.AddLocalization(c.localization ?? throw new ArgumentException("localization required"), c.expectedRevision);
                        break;
                    case "ValidateProject":
                        response.validation = service.ValidateProject();
                        break;
                    case "SimulatePlaythrough":
                        response.simulation = service.SimulatePlaythrough(c.seed, c.runs, c.maxSteps);
                        break;
                    default:
                        throw new ArgumentException("Operation is not allowlisted: " + c.operation);
                }
                response.success = true;
            }
            catch (Exception e) { response.error = e.Message; }
            response.revision = service.Revision;
            return JsonUtility.ToJson(response, true);
        }
        public static bool IsReadOnly(string operation)
        {
            switch (operation)
            {
                case "ValidateProject": case "SimulatePlaythrough": case "ListReferences": case "PreviewPresentation":
                case "GetExtensions": case "GetContentIndex": case "SearchContent": case "GetGraphProjection": case "GetReferences": case "GetProblems": case "ValidateGraph":
                case "GetLocalizationStatus": case "GetDeletionImpact": case "BeginChangeSet": case "PreviewChangeSet": case "RollbackChangeSet":
                case "SimulateFromNode": case "GetExecutionTrace": case "ExplainConditionFailure": return true;
                default: return false;
            }
        }
    }
}
