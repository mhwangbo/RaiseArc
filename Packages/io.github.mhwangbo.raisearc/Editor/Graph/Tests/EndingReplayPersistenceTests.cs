using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RaiseArc.Analysis;
using RaiseArc.Editor;

namespace RaiseArc.Editor.Tests
{
    public sealed class EndingReplayPersistenceTests
    {
        [Test]
        public void FailedReplayWriteLeavesNoPreservedIdOrRepeatedUpdateFailure()
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            var isolated = Path.Combine(Path.GetTempPath(), "RaiseArcReplayFailure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolated);
            File.WriteAllText(Path.Combine(isolated, "RaiseArcAnalysisResults"), "blocks directory creation");
            var job = new EndingReplayJob { id = Guid.NewGuid().ToString("N") };
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(EndingReplayJob).GetField("steps", fields).SetValue(job, Enumerable.Empty<SimulationRecord>().GetEnumerator());
            typeof(EndingReplayJob).GetField("clock", fields).SetValue(job, Stopwatch.StartNew());
            var jobs = (Dictionary<string, EndingReplayJob>)typeof(EndingReplayJobs).GetField("jobs", flags).GetValue(null);
            try
            {
                Directory.SetCurrentDirectory(isolated);
                jobs.Add(job.id, job);
                typeof(EndingReplayJobs).GetMethod("Finish", flags).Invoke(null, new object[] { job, "Completed" });
                Assert.That(job.status, Is.EqualTo("Error"));
                Assert.That(job.artifactId, Is.Empty);
                Assert.That(job.replay.sameStates, Is.False);
                Assert.That(job.error, Does.Contain("could not be preserved"));
                Assert.DoesNotThrow(() => typeof(EndingReplayJobs).GetMethod("Update", flags).Invoke(null, null));
                Assert.That(Directory.GetFiles(isolated), Has.Length.EqualTo(1));
            }
            finally
            {
                jobs.Remove(job.id);
                Directory.SetCurrentDirectory(originalDirectory);
                File.Delete(Path.Combine(isolated, "RaiseArcAnalysisResults"));
                Directory.Delete(isolated);
            }
        }
    }
}
