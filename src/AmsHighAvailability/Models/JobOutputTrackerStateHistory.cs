using System;

namespace AmsHighAvailability.Models
{
    public class JobOutputTrackerStateHistory
    {
        public DateTimeOffset Timestamp { get; set; }
        public ExtendedJobState State { get; set; }
        public int Progress { get; set; }
        public DateTimeOffset TimeReceived { get; set; }
    }
}
