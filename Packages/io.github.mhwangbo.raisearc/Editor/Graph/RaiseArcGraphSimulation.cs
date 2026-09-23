using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using UnityEditor;
using UnityEngine;

namespace PrincessStudio.Editor.Graph
{
    public sealed partial class GraphWorkbenchWindow
    {
        [InitializeOnLoadMethod]
        private static void ConnectSimulationNavigation()
        {
            RaiseArc.Editor.SimulationJobs.NavigateRequested -= OpenSimulationResult;
            RaiseArc.Editor.SimulationJobs.NavigateRequested += OpenSimulationResult;
        }
        private static void OpenSimulationResult(GameProjectAsset source, string id, SimulationRecord record, RuntimeObservation observation)
        {
            var window = GetWindow<GraphWorkbenchWindow>();
            if (window.inspectorDirty || window.changes != null && window.changes.edits.Count > 0)
                throw new InvalidOperationException(StudioText.Language == "ko" ? "워크벤치의 변경을 먼저 저장하거나 취소하세요. 분석 결과가 편집 초안을 덮어쓰지 않습니다." : "Save or discard the Workbench draft before viewing analysis results.");
            if (window.asset != source || window.project == null || window.api.Revision != source.Revision) Open(source);
            var sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var ev in window.project.events) RaiseArc.Core.RaiseArcFlowReuse.Expand(window.project, ev, sourceIds);
            string SourceId(string value) => value != null && sourceIds.TryGetValue(value, out var original) ? original : value ?? "";
            id = SourceId(id);
            if (window.index.Find(id) == null && record != null) id = record.observations.Select(o => SourceId(o.id)).FirstOrDefault(x => window.index.Find(x) != null) ?? record.eventId;
            if (window.index.Find(id) == null) { Selection.activeObject = source; EditorGUIUtility.PingObject(source); return; }
            window.Show(); window.Navigate(id);
            if (record == null || window.canvas == null) return;
            var from = SourceId(observation?.id ?? (record.nodeId.Length > 0 ? record.nodeId : record.eventId));
            var next = SourceId(observation?.relatedId ?? record.state.presentationStepId);
            var notes = new List<KeyValuePair<string, string>>();
            foreach (var condition in record.observations.Where(o => o.kind == "Condition"))
            {
                var owner = window.index.Owner(SourceId(condition.id));
                var mark = condition.outcome == "True" ? "✓" : condition.outcome == "False" ? "✗" : "—";
                notes.Add(new KeyValuePair<string, string>(owner, mark + " " + SourceId(condition.id) + (condition.hasActual ? " · " + condition.actual + " " + condition.comparison + " " + condition.expected : " · " + condition.outcome)));
            }
            if (record.assumptions.Count > 0) notes.Add(new KeyValuePair<string, string>(from, "MODEL · " + string.Join(", ", record.assumptions)));
            if (record.result == PlayResult.Unsupported || record.result == PlayResult.RuntimeError) notes.Add(new KeyValuePair<string, string>(from, "! " + record.reason));
            window.canvas.ShowExecution(from, next, record.input?.kind == "Choice" ? SourceId(record.input.id) : null, notes);
            window.Notify((StudioText.Language == "ko" ? "시뮬레이션 경로 · " : "Simulation path · ") + record.input?.id +
                (record.assumptions.Count == 0 ? "" : " · MODEL: " + string.Join(", ", record.assumptions)));
        }
    }
}
