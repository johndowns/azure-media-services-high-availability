﻿using System;

namespace AmsHighAvailability.Models
{
    public class JobRunAttemptStatusHistory
    {
        public DateTimeOffset StatusTime { get; set; }
        public AmsStatus Status { get; set; }
        public DateTimeOffset TimeReceived { get; set; }
    }
}
