// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using System;
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
using Microsoft.Azure.Management.Media.Models;

namespace AmsHighAvailability
{
    public class EventGridFunctions
    {
        private static readonly Regex EventSubjectRegex = new Regex(@".*\/jobs\/(.*)");
        
        [FunctionName("AmsJobStatusUpdate")]
        public async Task AmsJobStatusUpdate(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            log.LogInformation("Received job status Event Grid event of type {EventGridEventType} for subject {EventGridEventSubject}.",
                eventGridEvent.EventType, eventGridEvent.Subject);
            if (eventGridEvent.EventType != "Microsoft.Media.JobStateChange") return;

            var jobTrackerEntityId = GetJobTrackerEntityIdFromEventSubject(eventGridEvent.Subject);
            var statusTime = eventGridEvent.EventTime;

            var eventData = ((JObject)eventGridEvent.Data).ToObject<JobStateChangeEventData>();
            var jobTrackerStatus = eventData.State.ToAmsStatus();

            // We don't need to listen for status messages where the job has been queued or scheduled.
            // We aren't interested until the job actually starts getting processed.
            if (jobTrackerStatus == AmsStatus.Submitted) return;

            log.LogInformation("Updating job tracker status from Event Grid event. JobTrackerEntityId={JobTrackerEntityId}, JobTrackerStatus={JobTrackerStatus}, StatusTime={StatusTime}",
                jobTrackerEntityId, jobTrackerStatus, statusTime);
            var entityId = new EntityId(nameof(JobTrackerEntity), jobTrackerEntityId);
            await durableEntityClient.SignalEntityAsync<IJobTrackerEntity>(entityId, proxy => proxy.ReceiveStatusUpdate((jobTrackerStatus, statusTime)));
        }

        [FunctionName("AmsJobOutputStatusUpdate")]
        public async Task AmsJobOutputStatusUpdate(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            log.LogInformation("Received job output status Event Grid event of type {EventGridEventType} for subject {EventGridEventSubject}.",
                eventGridEvent.EventType, eventGridEvent.Subject);
            if (eventGridEvent.EventType != "Microsoft.Media.JobOutputStateChange") return;

            var jobTrackerEntityId = GetJobTrackerEntityIdFromEventSubject(eventGridEvent.Subject);
            var statusTime = eventGridEvent.EventTime;

            var eventData = ((JObject)eventGridEvent.Data).ToObject<JobOutputStateChangeEventData>();
            var jobOutputTrackerEntityId = eventData.Output.AssetName;
            var jobOutputTrackerProgress = eventData.Output.Progress;
            var jobOutputTrackerStatus = eventData.Output.State.ToAmsStatus();

            // We don't need to listen for status messages where the job has been queued or scheduled.
            // We aren't interested until the job actually starts getting processed.
            if (jobOutputTrackerStatus == AmsStatus.Submitted) return;

            log.LogInformation("Updating job output tracker status from Event Grid event. JobTrackerEntityId={JobTrackerEntityId}, JobOutputTrackerEntityId={JobOutputTrackerEntityId}, jobOutputTrackerStatus={JobOutputTrackerStatus}, JobOutputTrackerProgress={JobOutputTrackerProgress}, StatusTime={StatusTime}",
                jobOutputTrackerEntityId, jobTrackerEntityId, jobOutputTrackerStatus, jobOutputTrackerProgress, statusTime);
            var entityId = new EntityId(nameof(JobOutputTrackerEntity), jobOutputTrackerEntityId);
            await durableEntityClient.SignalEntityAsync<IJobOutputTrackerEntity>(entityId, proxy => proxy.ReceiveStatusUpdate((jobOutputTrackerStatus, jobOutputTrackerProgress, statusTime)));
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
