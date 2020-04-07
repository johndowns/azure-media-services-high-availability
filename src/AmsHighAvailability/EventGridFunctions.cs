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
            log.LogInformation("Received job status Event Grid event of type {EventGridEventType} for subject {EventGridEventSubject}.", eventGridEvent.EventType, eventGridEvent.Subject);
            if (eventGridEvent.EventType != "Microsoft.Media.JobStateChange") return;

            var jobRunAttemptId = GetJobRunAttemptIdFromEventSubject(eventGridEvent.Subject);
            var statusTime = eventGridEvent.EventTime;

            var eventData = ((JObject)eventGridEvent.Data).ToObject<JobStateChangeEventData>();
            var jobRunAttemptStatus = GetAmsStatusFromEventState(eventData.State);

            // We don't need to listen for status messages where the job has been queued or scheduled.
            // We aren't interested until the job actually starts getting processed.
            if (jobRunAttemptStatus == AmsStatus.Received) return;

            log.LogInformation("Updating job run attempt status from Event Grid event. JobRunAttemptId={JobRunAttemptId}, JobRunAttemptStatus={JobRunAttemptStatus}, StatusTime={StatusTime}", jobRunAttemptId, jobRunAttemptStatus, statusTime);
            var entityId = new EntityId(nameof(JobRunAttempt), jobRunAttemptId);
            await durableEntityClient.SignalEntityAsync<IJobRunAttempt>(entityId, proxy => proxy.StatusUpdate((jobRunAttemptStatus, statusTime)));
        }

        [FunctionName("AmsJobOutputStatusUpdate")]
        public async Task AmsJobOutputStatusUpdate(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            log.LogInformation("Received job output status Event Grid event of type {EventGridEventType} for subject {EventGridEventSubject}.", eventGridEvent.EventType, eventGridEvent.Subject);
            if (eventGridEvent.EventType != "Microsoft.Media.JobOutputStateChange") return;

            var jobRunAttemptId = GetJobRunAttemptIdFromEventSubject(eventGridEvent.Subject);
            var statusTime = eventGridEvent.EventTime;

            var eventData = ((JObject)eventGridEvent.Data).ToObject<JobOutputStateChangeEventData>();
            var jobRunAttemptOutputId = eventData.Output.AssetName;
            var jobRunAttemptOutputProgress = eventData.Output.Progress;
            var jobRunAttemptOutputStatus = GetAmsStatusFromEventState(eventData.Output.State);

            // We don't need to listen for status messages where the job has been queued or scheduled.
            // We aren't interested until the job actually starts getting processed.
            if (jobRunAttemptOutputStatus == AmsStatus.Received) return;

            log.LogInformation("Updating job run attempt output status from Event Grid event. JobRunAttemptOutputId={JobRunAttemptOutputId}, JobRunAttemptId={JobRunAttemptId}, JobRunAttemptOutputStatus={JobRunAttemptOutputStatus}, JobRunAttemptOutputProgress={JobRUnAttemptOutputProgress}, StatusTime={StatusTime}", jobRunAttemptOutputId, jobRunAttemptId, jobRunAttemptOutputStatus, jobRunAttemptOutputProgress, statusTime);
            var entityId = new EntityId(nameof(JobRunAttemptOutput), jobRunAttemptOutputId);
            await durableEntityClient.SignalEntityAsync<IJobRunAttemptOutput>(entityId, proxy => proxy.StatusUpdate((jobRunAttemptOutputStatus, jobRunAttemptOutputProgress, statusTime)));
        }

        private static string GetJobRunAttemptIdFromEventSubject(string eventSubject)
        {
            var match = EventSubjectRegex.Match(eventSubject);
            if (!match.Success) return null;

            return match.Groups[1].Value;
        }

        private static AmsStatus GetAmsStatusFromEventState(string state)
        {
            switch (state)
            {
                case "Queued":
                case "Scheduled":
                    return AmsStatus.Received;
                case "Processing":
                    return AmsStatus.Processing;
                case "Finished":
                    return AmsStatus.Succeeded;
                case "Error":
                case "Canceling":
                case "Canceled":
                    return AmsStatus.Failed;
                default:
                    throw new InvalidOperationException($"Unexpected value for event state: {state}");
            }
        }
    }

    #region Event Grid schema types
    public class JobStateChangeEventData
    {
        [JsonProperty("state")]
        public string State { get; set; }
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
            public string State { get; set; }
        }
    }
    #endregion
}
