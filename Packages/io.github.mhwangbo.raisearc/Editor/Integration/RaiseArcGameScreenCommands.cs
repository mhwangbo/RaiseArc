using System;
using PrincessStudio.Unity;
using RaiseArc.Unity;
using UnityEngine;

namespace RaiseArc.Editor
{
    [Serializable] public sealed class GameScreenCommand
    {
        public string operation;
        public int expectedScreenRevision;
        public GameScreenDefinition screen;
    }
    [Serializable] public sealed class GameScreenResponse
    {
        public bool success;
        public string error;
        public int screenRevision, contentRevision;
        public GameScreenDefinition screen;
    }
    public static class GameScreenCommands
    {
        public static bool Handles(string operation) => operation == "ReadGameScreen" || operation == "UpdateGameScreen";
        public static string Execute(GameProjectAsset project, string json)
        {
            var response = new GameScreenResponse();
            try
            {
                var command = JsonUtility.FromJson<GameScreenCommand>(json);
                if (!Handles(command.operation)) throw new ArgumentException("Unknown screen operation.");
                var saved = GameScreenAuthoring.Find(project) ?? throw new InvalidOperationException("Create the game screen using Window > RaiseArc > Make a game first.");
                if (command.operation == "UpdateGameScreen") saved = GameScreenAuthoring.Save(project, saved.Skin, command.screen, command.expectedScreenRevision);
                response.screen = saved.Read(); response.screenRevision = saved.Revision; response.contentRevision = project.Read().revision;
                response.success = true;
            }
            catch (Exception e) { response.error = e.Message; }
            return JsonUtility.ToJson(response);
        }
    }
}
