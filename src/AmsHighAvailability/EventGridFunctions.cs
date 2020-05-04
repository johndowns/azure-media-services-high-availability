// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using AmsHighAvailability.Entities;
using System.Threading.Tasks;
using AmsHighAvailability.Models;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AmsHighAvailability
{
    public class EventGridFunctions
    {
        private static readonly Regex EventSubjectRegex = new Regex(@".*\/jobs\/(.*)");
        
        [FunctionName("AmsJobStateUpdate")]
        public async Task AmsJobStateUpdate(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            log.LogDebug("Received job tracker Event Grid event of type {EventGridEventType} for subject {EventGridEventSubject}.",
                eventGridEvent.EventType, eventGridEvent.Subject);
            if (eventGridEvent.EventType != "Microsoft.Media.JobStateChange") return;

            var jobTrackerEntityId = GetJobTrackerEntityIdFromEventSubject(eventGridEvent.Subject);
            var timestamp = eventGridEvent.EventTime;

            var eventData = ((JObject)eventGridEvent.Data).ToObject<JobStateChangeEventData>();
            var state = eventData.State.ToExtendedJobState();

            // We don't need to listen for update messages if the job is in the 'queued' or 'scheduled' state.
            // We aren't interested until the job actually starts getting processed.
            if (state == ExtendedJobState.Submitted) return;

            log.LogDebug("Updating job tracker state from Event Grid event. JobTrackerEntityId={JobTrackerEntityId}, Stater={State}, Timestamp={Timestamp}",
                jobTrackerEntityId, state, timestamp);
            var entityId = new EntityId(nameof(JobTrackerEntity), jobTrackerEntityId);
            await durableEntityClient.SignalEntityAsync<IJobTrackerEntity>(entityId, proxy => proxy.ReceiveStateUpdate((state, timestamp)));
        }

        [FunctionName("AmsJobOutputStateUpdate")]
        public async Task AmsJobOutputStateUpdate(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            log.LogDebug("Received job output tracker Event Grid event of type {EventGridEventType} for subject {EventGridEventSubject}.",
                eventGridEvent.EventType, eventGridEvent.Subject);
            if (eventGridEvent.EventType != "Microsoft.Media.JobOutputStateChange") return;

            var jobTrackerEntityId = GetJobTrackerEntityIdFromEventSubject(eventGridEvent.Subject);
            var timestamp = eventGridEvent.EventTime;

            var eventData = ((JObject)eventGridEvent.Data).ToObject<JobOutputStateChangeEventData>();
            var jobOutputTrackerEntityId = eventData.Output.AssetName;
            var jobOutputTrackerProgress = eventData.Output.Progress;
            var state = eventData.Output.State.ToExtendedJobState();

            // We don't need to listen for update messages if the job output is in the 'queued' or 'scheduled' state.
            // We aren't interested until the job actually starts getting processed.
            if (state == ExtendedJobState.Submitted) return;

            log.LogDebug("Updating job output tracker state from Event Grid event. JobTrackerEntityId={JobTrackerEntityId}, JobOutputTrackerEntityId={JobOutputTrackerEntityId}, State={State}, JobOutputTrackerProgress={JobOutputTrackerProgress}, Timestamp={Timestamp}",
                jobOutputTrackerEntityId, jobTrackerEntityId, state, jobOutputTrackerProgress, timestamp);
            var entityId = new EntityId(nameof(JobOutputTrackerEntity), jobOutputTrackerEntityId);
            await durableEntityClient.SignalEntityAsync<IJobOutputTrackerEntity>(entityId, proxy => proxy.ReceiveStateUpdate((state, jobOutputTrackerProgress, timestamp)));
        }

        private static string GetJobTrackerEntityIdFromEventSubject(string eventSubject)
        {
            var match = EventSubjectRegex.Match(eventSubject);
            if (!match.Success) return null;

            return match.Groups[1].Value;
        }
    }

    #region Event Grid schema types
    public class JobStateChangeEventData
    {
        [JsonProperty("state")]
        public Microsoft.Azure.Management.Media.Models.JobState State { get; set; }
    }

    public class JobOutputStateChangeEventData
    {
        [JsonProperty("output")]
        public OutputData Output { get; set; }

        public class OutputData
        {
            [JsonProperty("assetName")]
            public string AssetName { get; set; }

            [JsonProperty("progress")]
            public int Progress { get; set; }

            [JsonProperty("state")]
            public Microsoft.Azure.Management.Media.Models.JobState State { get; set; }
        }
    }
    #endregion
}
