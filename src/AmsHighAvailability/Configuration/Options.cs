using System;

namespace AmsHighAvailability.Configuration
{
    public class Options
    {
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
