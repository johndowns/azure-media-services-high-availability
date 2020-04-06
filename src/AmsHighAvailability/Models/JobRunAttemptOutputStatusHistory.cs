using System;

namespace AmsHighAvailability.Models
{
    public class JobRunAttemptOutputStatusHistory
    {
        public DateTimeOffset StatusTime { get; set; }
        public AmsStatus Status { get; set; }
        public int Progress { get; set; }
        public DateTimeOffset TimeReceived { get; set; }
    }
}
