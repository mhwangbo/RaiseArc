using System;
using PrincessStudio.Core;
using UnityEngine;

namespace PrincessStudio.Unity
{
    [CreateAssetMenu(menuName = "RaiseArc/Game Project", fileName = "GameProject")]
    public sealed class GameProjectAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private ProjectDefinition definition = new ProjectDefinition();
        [SerializeField] private System.Collections.Generic.List<RaiseArc.Unity.RaiseArcExtensionAsset> extensions = new System.Collections.Generic.List<RaiseArc.Unity.RaiseArcExtensionAsset>();
        public ExtensionRegistry CreateExtensions(ProjectDefinition content = null)
        {
            var registry = new ExtensionRegistry();
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var provider in extensions)
            {
                if (provider == null) { registry.ConfigurationErrors.Add("An enabled extension asset is missing. Restore its package or explicitly remove the project binding."); continue; }
                try
                {
                    if (!ProjectValidator.IsIdentifier(provider.ExtensionId) || !ids.Add(provider.ExtensionId)) throw new ArgumentException("Invalid or duplicate extension provider: " + provider.ExtensionId);
                    provider.Register(content ?? Read(), registry);
                }
                catch (Exception error) { registry.ConfigurationErrors.Add(provider.name + ": " + error.Message); }
            }
            return registry;
        }
        public int Revision => definition.revision;
        public ProjectDefinition Read() => new UnityProjectCodec().Clone(definition);
        public void Write(ProjectDefinition value, ExtensionRegistry extensions = null)
        {
            var report = ProjectValidator.Validate(value, extensions ?? CreateExtensions(value));
            if (report.HasErrors)
                throw new ArgumentException(report.Summary);
            definition = new UnityProjectCodec().Clone(value);
        }
    }
    public sealed class UnityProjectCodec : IProjectCodec
    {
        public const int MaxJsonCharacters = 8 * 1024 * 1024;
        public ProjectDefinition Clone(ProjectDefinition project) => JsonUtility.FromJson<ProjectDefinition>(JsonUtility.ToJson(project));
        public string ToJson(ProjectDefinition project) => JsonUtility.ToJson(project, true);
        public ProjectDefinition FromJson(string json, ExtensionRegistry extensions = null)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonCharacters)
                throw new ArgumentException("JSON is empty or exceeds 8 MiB character limit.");
            // Require an explicit schema marker; JsonUtility otherwise defaults missing scalar fields.
            if (!JsonRootFields.HasIntegerValue(json, "schemaVersion", ProjectDefinition.CurrentSchema))
                throw new ArgumentException("Explicit supported schemaVersion is required.");
            var project = JsonUtility.FromJson<ProjectDefinition>(json);
            var report = ProjectValidator.Validate(project, extensions);
            if (report.HasErrors)
                throw new ArgumentException(report.Summary);
            return project;
        }
    }
    public static class GameFactory
    {
        public static GameSession Create(GameProjectAsset asset, uint randomSeed) => Create(asset.Read(), randomSeed, asset.CreateExtensions());
        public static GameSession Create(ProjectDefinition project, ExtensionRegistry extensions = null) =>
            Create(project, BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0), extensions);
        public static GameSession Create(ProjectDefinition project, uint randomSeed, ExtensionRegistry extensions = null) =>
            new GameSession(new UnityProjectCodec().Clone(project), extensions, randomSeed);
    }
}
