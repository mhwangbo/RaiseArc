using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor.Graph
{
    internal sealed class RaiseArcNodeSearch : VisualElement
    {
        internal RaiseArcNodeSearch(IEnumerable<(string title, string detail, Action add)> items, Action cancelled = null)
        {
            var finished = false;
            void Close(Action selected = null)
            {
                if (finished) return;
                finished = true;
                RemoveFromHierarchy();
                if (selected != null) selected(); else cancelled?.Invoke();
            }
            VisualElement eventRoot = null;
            EventCallback<PointerDownEvent> outside = e =>
            {
                if (worldBound.Contains(e.position)) return;
                e.StopImmediatePropagation();
                Close();
            };
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                eventRoot = panel.visualTree;
                eventRoot.RegisterCallback(outside, TrickleDown.TrickleDown);
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                eventRoot?.UnregisterCallback(outside, TrickleDown.TrickleDown);
                eventRoot = null;
            });
            name = "node-search";
            style.position = Position.Absolute;
            style.left = 12; style.top = 40; style.width = 390;
            style.height = 560;
            style.maxHeight = Length.Percent(85);
            style.backgroundColor = new Color(.10f, .15f, .20f);
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 10;
            var heading = new VisualElement(); heading.style.flexDirection = FlexDirection.Row;
            heading.style.height = 28; heading.style.flexShrink = 0;
            var title = new Label(StudioText.T("Add to this flow")); title.style.flexGrow = 1; heading.Add(title);
            heading.Add(new Button(() => Close()) { text = StudioText.T("Close") }); Add(heading);
            var prompt = new Label(StudioText.T("Search nodes or existing content")); prompt.style.flexShrink = 0; Add(prompt);
            var search = new TextField { name = "node-search-query" }; search.style.flexShrink = 0; search.style.marginBottom = 6; Add(search);
            var results = new ScrollView { name = "node-search-results" }; results.style.flexGrow = 1; results.style.minHeight = 0; Add(results);
            var choices = items.ToList();
            Action first = null;
            void Filter()
            {
                results.Clear();
                var matches = choices.Where(item => (item.title + " " + item.detail).IndexOf(search.value ?? "", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                first = matches.Count == 0 ? null : matches[0].add;
                foreach (var item in matches.Take(100))
                {
                    var row = new Button(() => Close(item.add));
                    row.style.alignItems = Align.Stretch; row.tooltip = item.title + "\n" + item.detail;
                    var label = new Label(item.title); label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    label.style.overflow = Overflow.Hidden; label.style.textOverflow = TextOverflow.Ellipsis; row.Add(label);
                    var detail = new Label(item.detail); detail.style.whiteSpace = WhiteSpace.Normal; detail.style.unityTextAlign = TextAnchor.MiddleLeft; row.Add(detail);
                    results.Add(row);
                }
                if (matches.Count == 0) results.Add(new Label(StudioText.T("No matching nodes.")));
                if (matches.Count > 100) results.Add(new Label(StudioText.T("More results available. Refine your search.")));
            }
            search.RegisterValueChangedCallback(_ => Filter());
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Return && first != null) { Close(first); e.StopPropagation(); }
            });
            Filter(); schedule.Execute(() => search.Focus());
        }
    }
}
