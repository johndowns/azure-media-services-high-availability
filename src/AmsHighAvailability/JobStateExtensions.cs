using AmsHighAvailability.Models;
using Microsoft.Azure.Management.Media.Models;
using System;

namespace AmsHighAvailability
{
    public static class JobStateExtensions
    {
        public static AmsStatus ToAmsStatus(this JobState state)
        {
            if (state == JobState.Queued || state == JobState.Scheduled)
            {
                return AmsStatus.Submitted;
            }
            if (state == JobState.Processing)
            {
                return AmsStatus.Processing;
            }
            if (state == JobState.Finished)
            {
                return AmsStatus.Succeeded;
            }
            if (state == JobState.Error || state == JobState.Canceling || state == JobState.Canceled)
            {
                return AmsStatus.Failed;
            }

            throw new InvalidOperationException($"Unexpected value for event state: {state}");
        }
    }
}
