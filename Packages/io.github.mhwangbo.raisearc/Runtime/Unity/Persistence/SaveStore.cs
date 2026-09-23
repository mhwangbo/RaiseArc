using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PrincessStudio.Core;
using UnityEngine;

namespace PrincessStudio.Unity
{
    [Serializable]
    public sealed class ParticipantData
    {
        public string id, payload; public int version;
    }
    [Serializable]
    public sealed class SavePayload
    {
        public int schemaVersion = 1;
        public string projectId, contentHash;
        public StateData state;
        public List<ParticipantData> participants = new List<ParticipantData>();
    }
    [Serializable]
    internal sealed class SaveEnvelope
    {
        public int version = 1; public string checksum, payload;
    }
    public sealed class SaveStore
    {
        private const int MaximumBytes = 16 * 1024 * 1024;
        private readonly string root;
        private readonly string projectId, contentHash;
        private readonly List<ISaveParticipant> participants = new List<ISaveParticipant>();
        public SaveStore(string directory, ProjectDefinition project)
        {
            root = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            projectId = project.id;
            var copy = new UnityProjectCodec().Clone(project);
            copy.revision = 0;
            contentHash = Hash(ContentJson(copy));
        }
        private static string ContentJson(ProjectDefinition project)
        {
            // Preserve firstgame.1 fingerprints when the added optional rules have their old, inert defaults.
            // JsonUtility escapes quotes inside strings, so these complete property tokens cannot match authored text.
            return JsonUtility.ToJson(project)
                .Replace("\"time\":{\"periodNameKeys\":[],\"planningDays\":7},", "")
                .Replace("\"periods\":0,", "")
                .Replace("\"evaluateEachPeriod\":false,", "")
                .Replace("\"anyGroup\":\"\",", "")
                .Replace("\"evaluation\":{\"enabled\":false,\"statId\":\"\",\"passingScore\":0,\"qualificationFlagId\":\"\"},", "")
                .Replace(",\"permanent\":false", "")
                .Replace("\"modifiers\":[],", "");
        }
        public void Register(ISaveParticipant participant)
        {
            if (participant == null || !ProjectValidator.IsIdentifier(participant.Id) || participants.Exists(p => p.Id == participant.Id))
                throw new ArgumentException("Invalid or duplicate save participant.");
            participants.Add(participant);
        }
        public void Save(string slot, GameSession session)
        {
            var payload = new SavePayload { projectId = projectId, contentHash = contentHash, state = session.Capture() };
            foreach (var p in participants)
                payload.participants.Add(new ParticipantData { id = p.Id, version = p.Version, payload = p.Capture() });
            var json = JsonUtility.ToJson(payload);
            var envelope = JsonUtility.ToJson(new SaveEnvelope { payload = json, checksum = Hash(json) });
            var bytes = Encoding.UTF8.GetBytes(envelope);
            if (bytes.Length > MaximumBytes)
                throw new IOException("Save exceeds size limit.");
            Directory.CreateDirectory(root);
            var path = PathFor(slot);
            var temp = path + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temp, path, path + ".bak");
                else
                    File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public bool Load(string slot, GameSession session)
        {
            var path = PathFor(slot);
            SavePayload payload;
            bool recovered = false;
            try
            {
                payload = Read(path);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidOperationException)
            {
                payload = Read(path + ".bak");
                recovered = true;
            }
            if (payload.projectId != projectId || payload.contentHash != contentHash)
                throw new InvalidOperationException("Save belongs to different content. An explicit migration is required.");
            if (payload.participants == null || payload.participants.Count != participants.Count)
                throw new InvalidOperationException("Save participant set changed.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var old = new List<ParticipantData>();
            foreach (var data in payload.participants)
            {
                if (data == null || !seen.Add(data.id))
                    throw new InvalidOperationException("Duplicate save participant.");
                var p = participants.Find(x => x.Id == data.id) ?? throw new InvalidOperationException("Missing save participant: " + data.id);
                p.Validate(data.version, data.payload);
                old.Add(new ParticipantData { id = p.Id, version = p.Version, payload = p.Capture() });
            }
            var prior = session.Capture();
            session.Restore(payload.state);
            try
            {
                foreach (var data in payload.participants)
                    participants.Find(p => p.Id == data.id).Restore(data.version, data.payload);
            }
            catch (Exception original)
            {
                session.Restore(prior);
                var failures = new List<Exception> { original };
                foreach (var data in old)
                    try
                    {
                        participants.Find(p => p.Id == data.id).Restore(data.version, data.payload);
                    }
                    catch (Exception rollback) { failures.Add(rollback); }
                throw new AggregateException("Save restore failed; rollback attempted for all participants.", failures);
            }
            return recovered;
        }
        private SavePayload Read(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes)
                throw new IOException("Save missing or oversized.");
            var e = JsonUtility.FromJson<SaveEnvelope>(File.ReadAllText(path, Encoding.UTF8));
            if (e == null || e.version != 1 || e.payload == null || e.checksum != Hash(e.payload))
                throw new InvalidOperationException("Save integrity check failed.");
            var p = JsonUtility.FromJson<SavePayload>(e.payload);
            if (p == null || p.schemaVersion != 1 || p.state == null)
                throw new InvalidOperationException("Unsupported save schema.");
            return p;
        }
        private string PathFor(string slot)
        {
            if (!ProjectValidator.IsIdentifier(slot) || slot.Contains(".."))
                throw new ArgumentException("Invalid save slot.");
            return Path.Combine(root, slot + ".json");
        }
        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }
}
