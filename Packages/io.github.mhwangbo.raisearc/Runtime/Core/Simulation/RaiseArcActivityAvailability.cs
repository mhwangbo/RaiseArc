namespace RaiseArc.Core
{
    public enum ActivityBlock
    {
        None, UnknownActivity, ModulePending, EventPending, ActivityPending,
        GameEnded, InsufficientMoney, InsufficientDays, ConditionsNotMet
    }

    /// <summary>The first blocking rule, evaluated without rolling success or applying effects.</summary>
    public readonly struct ActivityAvailability
    {
        public ActivityBlock Reason { get; }
        public bool Available => Reason == ActivityBlock.None;
        public int Required { get; }
        public int Actual { get; }
        public ActivityAvailability(ActivityBlock reason, int required = 0, int actual = 0)
        { Reason = reason; Required = required; Actual = actual; }
    }
}
