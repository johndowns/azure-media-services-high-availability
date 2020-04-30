using AmsHighAvailability.Models;
using Microsoft.Azure.Management.Media.Models;
using System;

namespace AmsHighAvailability
{
    public static class JobStateExtensions
    {
        public static ExtendedJobState ToExtendedJobState(this JobState state)
        {
            if (state == JobState.Queued || state == JobState.Scheduled)
            {
                return ExtendedJobState.Submitted;
            }
            if (state == JobState.Processing)
            {
                return ExtendedJobState.Processing;
            }
            if (state == JobState.Finished)
            {
                return ExtendedJobState.Succeeded;
            }
            if (state == JobState.Error || state == JobState.Canceling || state == JobState.Canceled)
            {
                return ExtendedJobState.Failed;
            }

            throw new InvalidOperationException($"Unexpected value for event state: {state}");
        }
    }
}
