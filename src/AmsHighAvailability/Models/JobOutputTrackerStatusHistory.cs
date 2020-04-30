using System;

namespace AmsHighAvailability.Models
{
    public class JobOutputTrackerStatusHistory
    {
        public DateTimeOffset StatusTime { get; set; }
        public ExtendedJobState Status { get; set; }
        public int Progress { get; set; }
        public DateTimeOffset TimeReceived { get; set; }
    }
}
