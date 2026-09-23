using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrincessStudio.Editor.Graph
{
    [Serializable] public sealed class NodeLayout
    {
        public string eventId, nodeId, note = "", group = "";
        public float x, y;
        public bool collapsed;
        public List<string> closedDetails = new List<string>(), openDetails = new List<string>();
        public Color color = new Color(.20f, .31f, .38f);
    }
    public sealed class GraphLayoutAsset : ScriptableObject
    {
        public float nodeWidth = 240;
        public List<NodeLayout> nodes = new List<NodeLayout>();
        public List<string> favorites = new List<string>(), recent = new List<string>();
        public NodeLayout Get(string eventId, string nodeId, int index)
        {
            var value = nodes.Find(x => x.eventId == eventId && x.nodeId == nodeId);
            if (value == null) { value = new NodeLayout { eventId = eventId, nodeId = nodeId, x = 30 + index % 3 * 410, y = 35 + index / 3 * 420 }; nodes.Add(value); }
            return value;
        }
    }
}
