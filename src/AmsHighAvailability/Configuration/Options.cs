using AmsHighAvailability.Models;
using System;

namespace AmsHighAvailability.Configuration
{
    public class Options
    {
        public TimeSpan JobTrackerCurrencyCheckInterval { get; set; } // How often we check whether the tracker has received a recent progress update from Azure Media Services.

        public TimeSpan JobTrackerCurrencyThreshold { get; set; } // How long the job tracker has to have gone without getting any updates from Azure Media Services before it polls for an update.

        public TimeSpan JobTrackerTimeoutCheckInterval { get; set; } // How often we check whether the tracker has timed out.

        public TimeSpan JobTrackerTimeoutThreshold { get; set; } // How long the job tracker has to have gone without seeing any job progress before it is considered to have timed out.

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
