using System;

namespace AmsHighAvailability.Models
{
    public class JobTrackerStateHistory
    {
        public DateTimeOffset Timestamp { get; set; }
        public ExtendedJobState State { get; set; }
        public DateTimeOffset TimeReceived { get; set; }
    }
}
