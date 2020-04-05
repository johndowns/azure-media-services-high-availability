using System;
using System.Collections.Generic;
using System.Text;

namespace AmsHighAvailability.Models
{
    public class JobRunAttemptStatusHistory
    {
        public DateTimeOffset StatusTime { get; set; }
        public JobRunAttemptStatus Status { get; set; }
        public DateTimeOffset TimeReceived { get; set; }
    }
}
