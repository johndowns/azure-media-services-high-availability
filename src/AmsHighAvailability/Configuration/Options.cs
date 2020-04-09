using AmsHighAvailability.Models;
using System;

namespace AmsHighAvailability.Configuration
{
    public class Options
    {
        public TimeSpan JobTrackerStatusTimeoutCheckInterval { get; set; } // How often we check whether the tracker has timed out.

        public TimeSpan JobTrackerTimeoutThreshold { get; set; } // How long the job tracker has to have gone without a status update before it is considered to have timed out.

        public AmsInstanceRoutingMethod AmsInstanceRoutingMethod { get; set; }

        public string PrimaryAmsInstanceId { get; set; }
        public string AllAmsInstanceIds { get; set; }
        public string[] AlllAmsInstanceIdsArray { get { return AllAmsInstanceIds.Split(';'); } }

        public AmsInstanceConfiguration GetAmsInstanceConfiguration(string amsInstanceId)
        {
            var amsInstanceConfiguration = new AmsInstanceConfiguration
            {
                MediaServicesSubscriptionId = Environment.GetEnvironmentVariable($"Options:AmsInstances:{amsInstanceId}:MediaServicesSubscriptionId"),
                MediaServicesResourceGroupName = Environment.GetEnvironmentVariable($"Options:AmsInstances:{amsInstanceId}:MediaServicesResourceGroupName"),
                MediaServicesInstanceName = Environment.GetEnvironmentVariable($"Options:AmsInstances:{amsInstanceId}:MediaServicesInstanceName"),
                AmsInstanceId = amsInstanceId
            };

            return (amsInstanceConfiguration.MediaServicesSubscriptionId == null && amsInstanceConfiguration.MediaServicesResourceGroupName == null && amsInstanceConfiguration.MediaServicesInstanceName == null)
                ? null 
                : amsInstanceConfiguration;
        }
    }
}
