namespace AmsHighAvailability.Models
{
    public enum JobRunAttemptStatus
    {
        Unknown,
        Received,
        Processing,
        Succeeded,
        Failed,
        TimedOut
    }
}
