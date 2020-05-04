using AmsHighAvailability.Entities;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Threading;

namespace AmsHighAvailability.Telemetry
{
    public static class TelemetryContext
    {
        internal static readonly AsyncLocal<string> JobCoordinatorId = new AsyncLocal<string>();
        internal static readonly AsyncLocal<string> JobTrackerId = new AsyncLocal<string>();
        internal static readonly AsyncLocal<string> JobOutputTrackerId = new AsyncLocal<string>();

        public static void SetEntityId(EntityId entityId)
        {
            if (entityId.EntityName.Equals(nameof(JobCoordinatorEntity), System.StringComparison.InvariantCultureIgnoreCase))
            {
                JobCoordinatorId.Value = entityId.EntityKey;
            }
            else if (entityId.EntityName.Equals(nameof(JobTrackerEntity), System.StringComparison.InvariantCultureIgnoreCase))
            {
                JobCoordinatorId.Value = entityId.EntityKey.Split('|')[0];
                JobTrackerId.Value = entityId.EntityKey.Split('|')[1];
            }
            else if (entityId.EntityName.Equals(nameof(JobOutputTrackerEntity), System.StringComparison.InvariantCultureIgnoreCase))
            {
                JobCoordinatorId.Value = entityId.EntityKey.Split('|')[0];
                JobTrackerId.Value = entityId.EntityKey.Split('|')[1];
                JobOutputTrackerId.Value = entityId.EntityKey.Split('|')[2];
            }
        }

        public static void Reset()
        {
            JobCoordinatorId.Value = null;
            JobTrackerId.Value = null;
            JobOutputTrackerId.Value = null;
        }
    }
}
