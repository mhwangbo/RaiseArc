using System;
using PrincessStudio.Unity;
using RaiseArc.Analysis;
using UnityEngine;

namespace RaiseArc.Editor
{
    public sealed class RaiseArcBalanceTestAsset : ScriptableObject
    {
        [SerializeField] private GameProjectAsset project;
        [SerializeField] private int revision;
        [SerializeField, TextArea] private string definitionJson;
        public GameProjectAsset Project => project;
        public int Revision => revision;
        public BalanceTestDefinition Read() => JsonUtility.FromJson<BalanceTestDefinition>(definitionJson);
        internal void Write(GameProjectAsset target, BalanceTestDefinition definition)
        { project = target; definitionJson = JsonUtility.ToJson(definition); revision++; }
    }
}
