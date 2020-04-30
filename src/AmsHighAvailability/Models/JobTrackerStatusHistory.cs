using System;

namespace AmsHighAvailability.Models
{
    public class JobTrackerStatusHistory
    {
        public DateTimeOffset StatusTime { get; set; }
        public ExtendedJobState Status { get; set; }
        public DateTimeOffset TimeReceived { get; set; }
    }
}
