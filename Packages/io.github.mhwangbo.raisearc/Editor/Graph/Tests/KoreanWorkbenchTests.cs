using System.IO;
using System.Linq;
using NUnit.Framework;
using PrincessStudio.Core;
using PrincessStudio.Unity;

namespace PrincessStudio.Editor.Graph.Tests
{
    public sealed class KoreanWorkbenchTests
    {
        [Test] public void CatalogHasUniqueKeysAndCompleteKoreanText()
        {
            var entries = StudioText.Entries.ToArray();
            Assert.That(entries, Is.Not.Empty);
            Assert.That(entries.All(e => !string.IsNullOrWhiteSpace(e.key) && !string.IsNullOrWhiteSpace(e.en) && !string.IsNullOrWhiteSpace(e.ko)), Is.True);
            Assert.That(entries.Select(e => e.key).Distinct().Count(), Is.EqualTo(entries.Length));
            Assert.That(entries.Select(e => e.en).Distinct().Count(), Is.EqualTo(entries.Length));
        }
        [Test] public void ToolLanguageDoesNotChangeSerializedEnumValues()
        {
            var previous = StudioText.Language;
            try
            {
                StudioText.Language = "ko";
                Assert.That(StudioText.T("Conditions"), Is.EqualTo("조건"));
                var field = StudioText.EnumField("Kind", ValueKind.Flag);
                Assert.That(field.value, Is.EqualTo(ValueKind.Flag));
                Assert.That(field.formatSelectedValueCallback(field.value), Is.EqualTo("플래그"));
                StudioText.Language = "en"; Assert.That(StudioText.T("Conditions"), Is.EqualTo("Conditions"));
            }
            finally { StudioText.Language = previous; }
        }
        [Test] public void ShowcaseBranchesCompleteWithScholarship()
        {
            var codec = new UnityProjectCodec();
            var project = codec.FromJson(File.ReadAllText("Assets/PrincessStudio/Samples/Showcase/RoyalEvening.json"));
            var api = new AuthoringService(project, codec);
            foreach (var branch in new[] { "court", "study", "help" })
            {
                var trace = api.SimulateFromNode(GraphShowcaseBuilder.EventId, GraphShowcaseBuilder.EventId,
                    choices: new System.Collections.Generic.List<string> { "royal.choice." + branch });
                Assert.That(trace.status, Is.EqualTo("Completed"), trace.error);
                Assert.That(trace.finalState.flags.Find(x => x.id == "royal-scholarship").value, Is.EqualTo(1));
            }
        }
    }
}
