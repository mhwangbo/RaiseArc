using NUnit.Framework;
using System.Reflection;
using PrincessStudio.Unity;
using RaiseArc.Editor;
using UnityEngine;

namespace PrincessStudio.Editor.Graph.Tests
{
    public sealed class AuthoringPipeTargetTests
    {
        [Test]
        public void SwitchingStudioProjectStopsTheOldMcpTarget()
        {
            var first = ScriptableObject.CreateInstance<GameProjectAsset>();
            var second = ScriptableObject.CreateInstance<GameProjectAsset>();
            first.Write(EndingTestExample.Definition());
            second.Write(EndingTestExample.Definition());
            var window = ScriptableObject.CreateInstance<StudioWindow>();
            try
            {
                AuthoringPipeServer.Start(first);
                Assert.That(AuthoringPipeServer.IsTarget(first), Is.True);
                typeof(StudioWindow).GetMethod("SetProject", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { second });
                Assert.That(AuthoringPipeServer.IsTarget(first), Is.False);
                Assert.That(AuthoringPipeServer.IsRunning, Is.False);
            }
            finally
            {
                AuthoringPipeServer.Stop();
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
