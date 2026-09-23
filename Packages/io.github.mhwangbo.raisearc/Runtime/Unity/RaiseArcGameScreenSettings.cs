using System;
using System.Collections.Generic;
using PrincessStudio.Unity;
using UnityEngine;

namespace RaiseArc.Unity
{
    public enum GameProgression { ActivitySelection, Schedule }
    public enum GameScreenLayout { SidebarLeft, SidebarRight, Compact }
    public enum GameScreenTheme { Twilight, Paper, Forest }

    [Serializable]
    public sealed class GameScreenDefinition
    {
        public int version = 1;
        public GameProgression progression;
        public GameScreenLayout layout;
        public GameScreenTheme theme;
        public int fontSize = 18;
        public bool showStats = true, showStage = true, showVoiceControls = true, statsFirst, fillBackground = true;
        public float portraitScale = 1;
        public List<string> visibleStats = new List<string>();
        public List<string> activityCategories = new List<string>();
        public bool compositionPreview;
        public List<ScreenPart> parts = new List<ScreenPart>();
    }

    public sealed class RaiseArcGameScreenSettings : ScriptableObject
    {
        [SerializeField] private GameProjectAsset project;
        [SerializeField] private PresentationSkin skin;
        [SerializeField] private int revision;
        [SerializeField, HideInInspector] private string definitionJson = "";
        public GameProjectAsset Project => project;
        public PresentationSkin Skin => skin;
        public int Revision => revision;
        public GameScreenDefinition Read() => string.IsNullOrEmpty(definitionJson) ? new GameScreenDefinition() : JsonUtility.FromJson<GameScreenDefinition>(definitionJson);

        // The editor authoring boundary owns validation, revision checks, Undo and persistence.
        public void Write(GameProjectAsset owner, PresentationSkin presentation, GameScreenDefinition value)
        {
            project = owner; skin = presentation; definitionJson = JsonUtility.ToJson(value); revision++;
        }
    }
}
