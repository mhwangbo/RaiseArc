using System;
using System.Collections.Generic;
using PrincessStudio.Core;

namespace RaiseArc.Core
{
    public enum ActivityCheckFrequency
    {
        ProjectDefault = 0,
        OncePerActivity = 1,
        OncePerDay = 2
    }

    [Serializable]
    public sealed class SuccessChance
    {
        public bool enabled;
        public int basePercent = 100;
        public List<StatChanceBonus> statBonuses = new List<StatChanceBonus>();
    }

    [Serializable]
    public sealed class StatChanceBonus
    {
        public string statId = "";
        public int threshold;
        public int percentPerPoint = 1;
    }

    [Serializable]
    public sealed class ActivityProgression
    {
        public bool enabled;
        public int experienceOnSuccess = 1;
        public int experienceOnFailure;
        public List<ActivityLevel> levels = new List<ActivityLevel>();
    }

    [Serializable]
    public sealed class ActivityLevel : Definition
    {
        public int requiredExperience;
        public int successBonusPercent;
        public List<ConditionSpec> conditions = new List<ConditionSpec>();
        public List<EffectSpec> effects = new List<EffectSpec>();
    }

    public sealed class ActivityInfo
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string DescriptionKey { get; internal set; }
        public string ImageKey { get; internal set; }
        public string CategoryId { get; internal set; }
        public string ModuleId { get; internal set; }
        public string LevelId { get; internal set; }
        public string LevelNameKey { get; internal set; }
        public int Level { get; internal set; }
        public int Experience { get; internal set; }
        public int SuccessPercent { get; internal set; }
        public int Days { get; internal set; }
        public int CostPerCheck { get; internal set; }
        public int IncomePerSuccess { get; internal set; }
        public bool Available { get; internal set; }
        public ActivityCheckFrequency CheckFrequency { get; internal set; }
    }
}
