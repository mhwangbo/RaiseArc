using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.UI
{
    public enum UIData
    {
        None, Money, Stat, Day, Period, CharacterName, CharacterImage, BackgroundImage,
        ActivityName, ActivityDescription, ActivityCost, ActivityDuration, ActivityImage,
        SlotDate, SlotPeriod, SlotActivity, PlanStatus, Dialogue, Speaker, Ending, Status, CommandReason, ChoiceText, EventVisible, EndingVisible,
        LocalizedText, ElapsedDays, DayPosition, PlanTargetDate
    }
    public enum UICommand { None, SelectSlot, PlaceActivity, ClearSlot, CopyNextDay, ConfirmPlan, RunNext, ContinueDialogue, Choose, Save, Load, Restart, NewPlan, OpenPanel, ClosePanel, RunPlan }
    public enum UIItems { Activities, PlanSlots, Choices }
    public enum UIItemOrder { Authored, NameAscending, NameDescending }

    [Serializable] public sealed class UIConnection
    {
        public string target = "";
        public UIData data;
        public string contentId = "";
        public UICommand command;
        public string panel = "";
    }
    [Serializable] public sealed class UIRepeat
    {
        public string target = "";
        public UIItems items;
        public RaiseArcUIBindings template;
        public string category = "";
        public UIItemOrder order;
    }

    [CreateAssetMenu(menuName = "RaiseArc/UI bindings", fileName = "UIBindings")]
    public sealed class RaiseArcUIBindings : ScriptableObject
    {
        public GameProjectAsset project;
        public VisualTreeAsset document;
        public List<UIConnection> connections = new List<UIConnection>();
        public List<UIRepeat> repeats = new List<UIRepeat>();

        public List<string> Validate(VisualElement root, bool itemTemplate = false, UIItems? itemKind = null)
        {
            var errors = new List<string>();
            if (document == null) errors.Add("Document / UXML is missing.");
            var used = new HashSet<string>();
            for (var i = 0; i < connections.Count; i++)
            {
                var c = connections[i]; var label = $"Connection {i + 1} [{c.target}]";
                var matches = Named(root, c.target);
                if (matches.Count != 1) { errors.Add($"{label}: expected one named element, found {matches.Count}."); continue; }
                if (!used.Add(c.target)) errors.Add($"{label}: target has more than one connection.");
                if (itemKind.HasValue)
                {
                    var required = c.data >= UIData.ActivityName && c.data <= UIData.ActivityImage || c.command == UICommand.PlaceActivity ? UIItems.Activities :
                        c.data >= UIData.SlotDate && c.data <= UIData.SlotActivity || c.command == UICommand.SelectSlot ? UIItems.PlanSlots :
                        c.data == UIData.ChoiceText || c.command == UICommand.Choose ? (UIItems?)UIItems.Choices : null;
                    if (required.HasValue && required != itemKind) errors.Add($"{label}: {required} data/action cannot be used in {itemKind}.");
                }
                var element = matches[0];
                var image = c.data == UIData.CharacterImage || c.data == UIData.BackgroundImage || c.data == UIData.ActivityImage;
                if (image && !(element is Image)) errors.Add($"{label}: image data requires a standard Image.");
                else if (!image && c.data != UIData.EventVisible && c.data != UIData.EndingVisible && c.data != UIData.None && !(element is TextElement) && !(element is ProgressBar && c.data == UIData.Stat))
                    errors.Add($"{label}: this data requires text, or a ProgressBar for a stat.");
                if (c.command != UICommand.None && c.data != UIData.CommandReason && !(element is Button))
                    errors.Add($"{label}: commands require a standard Button.");
                if ((c.command == UICommand.OpenPanel || c.command == UICommand.ClosePanel) && Named(root, c.panel).Count != 1)
                    errors.Add($"{label}: panel [{c.panel}] must identify one element.");
                if (c.data == UIData.Stat && (project == null || !project.Read().stats.Any(s => s.id == c.contentId)))
                    errors.Add($"{label}: select a defined stat.");
                if (!itemTemplate && (IsItemData(c.data) || c.command == UICommand.SelectSlot || c.command == UICommand.PlaceActivity || c.command == UICommand.Choose))
                    errors.Add($"{label}: item data/action belongs in an activity, slot or choice template.");
            }
            foreach (var repeat in repeats)
            {
                var label = $"Repeated {repeat.items} [{repeat.target}]";
                if (itemTemplate) errors.Add($"{label}: nested repeaters are not supported.");
                if (Named(root, repeat.target).Count != 1) errors.Add($"{label}: select one container.");
                if (Named(root, repeat.target).Any(e => e is TextElement || e is Image || e is ProgressBar)) errors.Add($"{label}: use a VisualElement or ScrollView container.");
                if (!used.Add(repeat.target)) errors.Add($"{label}: container already has a connection.");
                if (repeat.template == null || repeat.template.document == null) errors.Add($"{label}: select a template binding asset with UXML.");
                else if (repeat.template == this || repeat.template.repeats.Count != 0) errors.Add($"{label}: cyclic/nested templates are not supported.");
                else
                    foreach (var error in repeat.template.Validate(repeat.template.document.CloneTree(), true, repeat.items))
                        errors.Add(label + " / " + error);
            }
            return errors;
        }
        public static List<VisualElement> Named(VisualElement root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return new List<VisualElement>();
            return root.Query<VisualElement>(name).ToList();
        }
        public static bool IsItemData(UIData value) => (value >= UIData.ActivityName && value <= UIData.SlotActivity) || value == UIData.ChoiceText;
    }
}
