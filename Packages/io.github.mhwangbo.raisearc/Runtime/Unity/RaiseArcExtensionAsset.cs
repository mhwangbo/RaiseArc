using PrincessStudio.Core;
using UnityEngine;

namespace RaiseArc.Unity
{
    /// <summary>Install a provider asset in one GameProjectAsset. Never register project state globally.</summary>
    public abstract class RaiseArcExtensionAsset : ScriptableObject
    {
        public abstract string ExtensionId { get; }
        public abstract void Register(ProjectDefinition project, ExtensionRegistry registry);
    }
}
