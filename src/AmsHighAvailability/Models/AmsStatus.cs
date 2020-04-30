namespace AmsHighAvailability.Models
{
    public enum AmsStatus // TODO rename to ExtendedJobState?
    {
        Submitted,
        Processing,
        Succeeded,
        Failed,
        TimedOut
    }
}
