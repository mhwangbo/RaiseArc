using System;
using System.Collections.Generic;
using PrincessStudio.Core;

namespace RaiseArc.Core
{
    [Serializable]
    public sealed class TimePlanRules
    {
        // An empty list preserves the original day-based calendar.
        public List<string> periodNameKeys = new List<string>();
        public int planningDays = 7;
        public int PeriodsPerDay => Math.Max(1, periodNameKeys.Count);
        public int Capacity => checked(PeriodsPerDay * planningDays);
        public int Duration(ActivityDefinition activity) => activity.periods > 0 ? activity.periods : checked(activity.days * PeriodsPerDay);
        public int Tick(StateSnapshot state) => checked(state.Day * PeriodsPerDay + state.Period);
        public IEnumerable<string> Errors(ProjectDefinition project)
        {
            if (periodNameKeys == null || periodNameKeys.Count > 12 || planningDays < 1 || planningDays > 366)
            { yield return "Use up to 12 named periods and a planning range of 1–366 days."; yield break; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in periodNameKeys)
                if (!ProjectValidator.IsIdentifier(key) || !names.Add(key)) yield return "Time periods need unique localization keys.";
            foreach (var activity in project.activities)
            {
                if (activity.periods < 0 || activity.periods > (long)project.durationDays * PeriodsPerDay)
                    yield return "Activity period duration is outside the game duration: " + activity.id;
                if (activity.periods == 0) continue;
                var frequency = activity.checkFrequency == ActivityCheckFrequency.ProjectDefault ? project.defaultActivityCheckFrequency : activity.checkFrequency;
                if (frequency != ActivityCheckFrequency.OncePerActivity || !string.IsNullOrEmpty(activity.moduleId))
                    yield return "Period activities currently use one cost/outcome per activity; day-based activities retain daily checks and module support: " + activity.id;
            }
        }
    }
}
