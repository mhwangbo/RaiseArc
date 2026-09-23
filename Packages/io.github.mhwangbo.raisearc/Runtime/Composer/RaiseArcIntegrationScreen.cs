using System;
using System.IO;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using RaiseArc.Unity;
using UnityEngine;

namespace RaiseArc.Samples
{
    /// <summary>Two deliberately small layouts share the official host. Only the exam question belongs to this screen.</summary>
    public sealed class RaiseArcIntegrationScreen : MonoBehaviour
    {
        [SerializeField] private GameProjectAsset project;
        private RaiseArcSessionHost host;
        private bool cards;
        private string message = "";
        public void Configure(GameProjectAsset asset) { project = asset; }
        private void Start() { host = new RaiseArcSessionHost(project, Path.Combine(Application.persistentDataPath, "RaiseArcIntegration"), 123); }
        private void OnGUI()
        {
            if (host == null) return;
            GUILayout.BeginArea(new Rect(20, 20, Mathf.Min(Screen.width - 40, 900), Screen.height - 40), GUI.skin.box);
            GUILayout.Label("RaiseArc integration example — " + (cards ? "Cards" : "List"));
            if (GUILayout.Button("Switch screen")) cards = !cards;
            GUILayout.Label("Day " + host.State.Day + " / Money " + host.State.Money + " / Wisdom " + host.State.Stats["wisdom"]);
            GUILayout.Label("Luna " + host.State.Relationships["luna"] + " / Mira " + host.State.Relationships["mira"] + " / Rose " + host.State.Items["rose"] + " / Tea " + host.State.Items["tea"]);
            GUILayout.Label("Waiting: " + host.Wait + " / Schedule " + host.ScheduleCursor + "/" + host.Schedule.Count);
            if (host.Wait == SessionWait.ActivityBlocked) GUILayout.Label("Blocked: " + host.BlockedActivity.Reason);
            if (host.Module != null)
            {
                GUILayout.Label("Exam: 2 + 3 = ?");
                Answer("5 — correct", true); Answer("4 — incorrect", false);
                if (GUILayout.Button("Cancel exam (keep current item)")) Act(() => host.CancelModule(host.Module.SessionId));
            }
            else if (host.State.PendingEventId.Length > 0)
            {
                var step = host.CurrentStep;
                if (step != null) GUILayout.Label(host.Text(step.nameKey));
                foreach (var choice in host.CurrentChoices)
                    if (GUILayout.Button(host.Text(choice.nameKey))) Act(() => host.Choose(choice.id));
                if (step != null && step.kind != PresentationStepKind.Choice && GUILayout.Button("Continue dialogue"))
                    Act(() => host.AdvanceDialogue(step.id));
            }
            else
            {
                if (GUILayout.Button("Training > exam > work")) Act(() => host.StartSchedule(new[] { "train", "exam", "work" }, 5));
                if (cards) GUILayout.BeginHorizontal();
                foreach (var activity in host.Activities())
                    if (GUILayout.Button(host.Text(activity.NameKey), cards ? GUILayout.Width(140) : GUILayout.ExpandWidth(true))) Act(() => host.StartActivity(activity.Id));
                if (cards) GUILayout.EndHorizontal();
                if (GUILayout.Button("Resume schedule")) Act(host.Resume);
                if (GUILayout.Button("Skip current item")) Act(host.SkipCurrent);
                if (GUILayout.Button("Clear remaining schedule")) Act(host.ClearSchedule);
            }
            GUILayout.BeginHorizontal();
            GUI.enabled = host.CanSave;
            if (GUILayout.Button("Save")) Act(() => host.Save("shared"));
            GUI.enabled = true;
            if (GUILayout.Button("Load")) Act(() => { var backup = host.Load("shared"); message = backup ? "Loaded backup" : "Loaded primary save"; });
            if (GUILayout.Button("Restart")) Act(() => host.Restart(123));
            GUILayout.EndHorizontal();
            if (!host.CanSave) GUILayout.Label(host.SaveBlockedReason);
            GUILayout.Label(message);
            GUILayout.EndArea();
        }
        private void Answer(string label, bool correct)
        {
            if (GUILayout.Button(label)) Act(() => host.CompleteModule(new ModuleResult {
                sessionId = host.Module.SessionId, elapsedDays = 1,
                effects = new System.Collections.Generic.List<EffectSpec> {
                    new EffectSpec { kind = ValueKind.Flag, target = correct ? "passed" : "failed", operation = EffectOperation.Set, value = 1 }
                }
            }));
        }
        private void Act(Action action)
        {
            try { message = ""; action(); }
            catch (Exception error) { message = error.Message; }
        }
    }
}
