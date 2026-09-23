using System;
using System.Threading;
using System.Threading.Tasks;
using PrincessStudio.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrincessStudio.Unity
{
    /// <summary>Scene endpoint. User modules subclass it and call Complete; no core state is exposed.</summary>
    public abstract class SceneModuleEndpoint : MonoBehaviour
    {
        private TaskCompletionSource<ModuleResult> completion;
        public ModuleContext Context
        {
            get; private set;
        }
        internal Task<ModuleResult> StartModule(ModuleContext context)
        {
            Context = context;
            completion = new TaskCompletionSource<ModuleResult>();
            OnBegin(context);
            return completion.Task;
        }
        protected abstract void OnBegin(ModuleContext context);
        protected void Complete(ModuleResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            result.sessionId = Context.SessionId;
            completion?.TrySetResult(result);
        }
        protected virtual void OnDestroy()
        {
            completion?.TrySetCanceled();
        }
    }
    public sealed class CustomSceneAdapter : IGameModeModule
    {
        public string Id
        {
            get;
        }
        private readonly string scenePath;
        private bool running;
        public CustomSceneAdapter(string id, string scenePath)
        {
            Id = id;
            this.scenePath = scenePath;
        }
        public async Task<ModuleResult> ExecuteAsync(ModuleContext context, CancellationToken cancellationToken)
        {
            if (context.ModuleId != Id || running)
                throw new InvalidOperationException("Module mismatch or concurrent scene execution.");
            if (!Application.CanStreamedLevelBeLoaded(scenePath))
                throw new InvalidOperationException("Module scene must be included in Build Settings: " + scenePath);
            if (SceneManager.GetSceneByPath(scenePath).isLoaded)
                throw new InvalidOperationException("Module scene is already loaded.");
            cancellationToken.ThrowIfCancellationRequested();
            running = true;
            var previous = SceneManager.GetActiveScene();
            Scene loaded = default;
            try
            {
                var load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive) ?? throw new InvalidOperationException("Scene load failed.");
                // Unity cannot cancel an in-flight load. Finish it so finally can unload owned content.
                await Wait(load);
                loaded = SceneManager.GetSceneByPath(scenePath);
                cancellationToken.ThrowIfCancellationRequested();
                SceneManager.SetActiveScene(loaded);
                SceneModuleEndpoint endpoint = null;
                foreach (var root in loaded.GetRootGameObjects())
                    foreach (var candidate in root.GetComponentsInChildren<SceneModuleEndpoint>(true))
                    {
                        if (endpoint != null)
                            throw new InvalidOperationException("A module scene requires exactly one endpoint.");
                        endpoint = candidate;
                    }
                if (endpoint == null)
                    throw new InvalidOperationException("No SceneModuleEndpoint in module scene.");
                var task = endpoint.StartModule(context);
                var canceled = new TaskCompletionSource<bool>();
                using (cancellationToken.Register(() => canceled.TrySetResult(true)))
                {
                    if (await Task.WhenAny(task, canceled.Task) != task)
                        throw new OperationCanceledException(cancellationToken);
                    return await task;
                }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                if (loaded.IsValid() && loaded.isLoaded)
                {
                    var unload = SceneManager.UnloadSceneAsync(loaded);
                    if (unload != null)
                        await Wait(unload);
                }
                running = false;
            }
        }
        private static Task Wait(AsyncOperation operation)
        {
            if (operation.isDone)
                return Task.CompletedTask;
            var done = new TaskCompletionSource<bool>();
            operation.completed += _ => done.TrySetResult(true);
            return done.Task;
        }
    }
    public static class ModuleRunner
    {
        public static async Task RunAsync(GameSession session, string activityId, IGameModeModule module, CancellationToken cancellationToken, IResultMapper mapper = null)
        {
            var context = session.BeginModule(activityId);
            try
            {
                if (module.Id != context.ModuleId)
                    throw new InvalidOperationException("Wrong module.");
                var result = await module.ExecuteAsync(context, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                session.CompleteModule(result, mapper);
            }
            catch { if (session.HasPendingModule) session.CancelModule(context.SessionId); throw; }
        }
    }
}
