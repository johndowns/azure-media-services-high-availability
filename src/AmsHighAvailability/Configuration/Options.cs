using AmsHighAvailability.Models;
using System;

namespace AmsHighAvailability.Configuration
{
    public class Options
    {
        public TimeSpan JobRunAttemptStatusTimeoutCheckInterval { get; set; } // How often we check whether the attempt has timed out.

        public TimeSpan JobRunAttemptTimeoutThreshold { get; set; } // How long the job attempt has to have gone without a status update before it is considered to have timed out.

        public StampRoutingMethod StampRoutingMethod { get; set; }

        public string HomeStampId { get; set; }
        public string AllStampIds { get; set; }
        public string[] AlllStampIdsArray { get { return AllStampIds.Split(';'); } }

        public StampConfiguration GetStampConfiguration(string stampId)
        {
            var stampConfiguration = new StampConfiguration
            {
                MediaServicesEndpointUrl = Environment.GetEnvironmentVariable($"Options:Stamps:{stampId}:MediaServicesEndpointUrl"),
                MediaServicesKey = Environment.GetEnvironmentVariable($"Options:Stamps:{stampId}:MediaServicesKey"),
                StampId = stampId
            };

            return stampConfiguration.MediaServicesEndpointUrl == null && stampConfiguration.MediaServicesKey == null ? null : stampConfiguration;
        }
    }
}
