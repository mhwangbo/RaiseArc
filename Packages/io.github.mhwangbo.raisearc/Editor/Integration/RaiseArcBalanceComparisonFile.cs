using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace RaiseArc.Editor
{
    /// <summary>Portable comparison of two preserved results; does not resume or replay either job.</summary>
    public static class BalanceComparisonFile
    {
        [Serializable] private sealed class Document
        {
            public int formatVersion = 1;
            public string comparisonMode = "policy-id-v1";
            public BalanceArtifact before, after;
        }

        private const long MaximumBytes = 512 * 1048576L;

        public static void Export(string path, BalanceResultComparison comparison)
        {
            if (comparison == null) throw new ArgumentException("Open a comparison first.");
            var document = new Document { before = comparison.beforeSource, after = comparison.afterSource };
            BalanceTests.ValidateArtifact(document.before);
            BalanceTests.ValidateArtifact(document.after);
            var json = JsonUtility.ToJson(document);
            if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new ArgumentException("Comparison exceeds 512 MiB; keep the two original result files instead.");
            using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew))) writer.Write(json);
        }

        public static BalanceResultComparison Open(string path)
        {
            if (new FileInfo(path).Length > MaximumBytes) throw new ArgumentException("Comparison import exceeds 512 MiB.");
            var document = JsonUtility.FromJson<Document>(File.ReadAllText(path));
            if (document == null || document.formatVersion != 1 || document.comparisonMode != "policy-id-v1")
                throw new ArgumentException("Unsupported comparison format or settings.");
            BalanceTests.ValidateArtifact(document.before);
            BalanceTests.ValidateArtifact(document.after);
            return BalanceTests.Compare(document.before, document.after);
        }
    }
}
