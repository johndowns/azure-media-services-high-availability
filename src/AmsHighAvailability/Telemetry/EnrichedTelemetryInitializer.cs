using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace AmsHighAvailability.Telemetry
{

    public class EnrichedTelemetryInitializer : ITelemetryInitializer
    {
        public void Initialize(ITelemetry telemetry)
        {
            if (! (telemetry is ISupportProperties propTelemetry)) return;

            if (TelemetryContext.JobCoordinatorId.Value != null)
            {
                propTelemetry.Properties[nameof(TelemetryContext.JobCoordinatorId)] = TelemetryContext.JobCoordinatorId.Value;
            }

            if (TelemetryContext.JobTrackerId.Value != null)
            {
                propTelemetry.Properties[nameof(TelemetryContext.JobTrackerId)] = TelemetryContext.JobTrackerId.Value;
            }
        }
    }
}
