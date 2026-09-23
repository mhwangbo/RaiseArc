using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PrincessStudio.Core;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEngine;

namespace PrincessStudio.Editor
{
    /// <summary>Opt-in current-user IPC. Unity object access stays on the Editor main thread.</summary>
    [InitializeOnLoad]
    public static class AuthoringPipeServer
    {
        private sealed class Request
        {
            public string json; public TaskCompletionSource<string> completion;
        }
        private static readonly ConcurrentQueue<Request> requests = new ConcurrentQueue<Request>();
        private static CancellationTokenSource cancellation;
        private static NamedPipeServerStream pipe;
        private static GameProjectAsset target;
        public static string LastError
        {
            get; private set;
        }
        public static string PipeName
        {
            get; private set;
        }
        public static bool IsRunning => cancellation != null && string.IsNullOrEmpty(LastError);
        public static bool IsTarget(GameProjectAsset project) => ReferenceEquals(target, project);
        static AuthoringPipeServer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.update += Pump;
        }
        public static void Start(GameProjectAsset project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            Stop();
            LastError = null;
            target = project;
            using (var hash = SHA256.Create())
                PipeName = "princess-studio-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(Application.dataPath).ToLowerInvariant()))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            _ = Serve(token);
        }
        public static void Stop()
        {
            cancellation?.Cancel();
            pipe?.Dispose();
            pipe = null;
            cancellation?.Dispose();
            cancellation = null;
            target = null;
            while (requests.TryDequeue(out var request))
                request.completion.TrySetCanceled();
        }
        private static async Task Serve(CancellationToken token)
        {
            try
            {
                using (var connection = WindowsPipeFactory.Create(PipeName))
                {
                    pipe = connection;
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await connection.WaitForConnectionAsync(token).ConfigureAwait(false);
                            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                            {
                                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                                var ct = timeout.Token;
                                var header = new byte[4];
                                await ReadExact(connection, header, ct).ConfigureAwait(false);
                                var length = BitConverter.ToInt32(header, 0);
                                if (length < 1 || length > 262144)
                                    throw new InvalidDataException("Invalid command size.");
                                var bytes = new byte[length];
                                await ReadExact(connection, bytes, ct).ConfigureAwait(false);
                                var request = new Request { json = Encoding.UTF8.GetString(bytes), completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously) };
                                requests.Enqueue(request);
                                using (ct.Register(() => request.completion.TrySetCanceled()))
                                {
                                    var response = Encoding.UTF8.GetBytes(await request.completion.Task.ConfigureAwait(false));
                                    var responseHeader = BitConverter.GetBytes(response.Length);
                                    await connection.WriteAsync(responseHeader, 0, 4, ct).ConfigureAwait(false);
                                    await connection.WriteAsync(response, 0, response.Length, ct).ConfigureAwait(false);
                                    await connection.FlushAsync(ct).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (Exception e) when (e is IOException || e is OperationCanceledException) { }
                        finally { if (!token.IsCancellationRequested && connection.IsConnected) connection.Disconnect(); }
                    }
                }
            }
            catch (Exception) when (token.IsCancellationRequested) { }
            catch (Exception e) { LastError = e.ToString(); }
        }
        private static async Task ReadExact(Stream stream, byte[] buffer, CancellationToken token)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer, offset, buffer.Length - offset, token).ConfigureAwait(false);
                if (count == 0)
                    throw new EndOfStreamException();
                offset += count;
            }
        }
        private static void Pump()
        {
            if (!requests.TryDequeue(out var request) || request.completion.Task.IsCompleted)
                return;
            try
            {
                if (target == null)
                    throw new InvalidOperationException("No authoring project selected.");
                var simulationCommand = JsonUtility.FromJson<RaiseArc.Editor.SimulationCommand>(request.json);
                if (RaiseArc.Editor.BalanceCommands.Handles(simulationCommand.operation))
                {
                    request.completion.TrySetResult(RaiseArc.Editor.BalanceCommands.Execute(target, request.json));
                    return;
                }
                if (RaiseArc.Editor.GameScreenCommands.Handles(simulationCommand.operation))
                {
                    request.completion.TrySetResult(RaiseArc.Editor.GameScreenCommands.Execute(target, request.json));
                    return;
                }
                if (RaiseArc.Editor.EndingTestCommands.Handles(simulationCommand.operation))
                {
                    request.completion.TrySetResult(RaiseArc.Editor.EndingTestCommands.Execute(target, request.json));
                    return;
                }
                if (RaiseArc.Editor.SimulationJobs.Handles(simulationCommand.operation))
                {
                    request.completion.TrySetResult(RaiseArc.Editor.SimulationJobs.Execute(target, request.json));
                    return;
                }
                var api = new AuthoringService(target.Read(), new UnityProjectCodec(), target.CreateExtensions());
                if (request.json == "{\"operation\":\"ReadProject\"}")
                    request.completion.TrySetResult(new UnityProjectCodec().ToJson(api.Snapshot()));
                else
                {
                    var before = api.Revision;
                    var response = new AuthoringCommandGateway(api).ExecuteJson(request.json);
                    if (api.Revision != before)
                    {
                        Undo.RecordObject(target, "LLM authoring command");
                        target.Write(api.Snapshot());
                        EditorUtility.SetDirty(target);
                        AssetDatabase.SaveAssetIfDirty(target);
                        RaiseArc.Editor.RaiseArcVoiceField.Sync(target);
                    }
                    request.completion.TrySetResult(response);
                }
            }
            catch (Exception e) { request.completion.TrySetResult(JsonUtility.ToJson(new CommandResponse { success = false, error = e.Message })); }
        }
    }
}
