using System;
using System.Collections.Generic;
using System.Text;

namespace AmsHighAvailability.Models
{
    public class JobRunAttemptStatusHistory
    {
        public DateTimeOffset Timestamp { get; set; }
        public JobRunAttemptStatus Status { get; set; }
    }
}
