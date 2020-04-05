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
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace AmsHighAvailability
{
    public class EventGridFunctions
    {
        private static Regex EventSubjectRegex = new Regex(@".*\/jobs\/(.*)");

        [FunctionName("AmsStatusUpdate")]
        public async Task AmsStatusUpdate(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            log.LogInformation("Received Event Grid event of type {EventGridEventType} for subject {EventGridEventSubject}.", eventGridEvent.EventType, eventGridEvent.Subject);
            var jobRunAttemptId = GetJobRunAttemptIdFromEventSubject(eventGridEvent.Subject);
            var statusTime = eventGridEvent.EventTime;

            if (eventGridEvent.EventType != "Microsoft.Media.JobStateChange") return; // TODO handle Microsoft.Media.JobOutputStateChange events - looking for any forward progress

            // Map the event state to the job run attempt status.
            string eventState = Convert.ToString(((dynamic)eventGridEvent.Data).state);
            JobRunAttemptStatus jobRunAttemptStatus;
            switch (eventState)
            {
                case "Queued":
                case "Scheduled":
                    // We don't need to listen for status messages where the job has been queued or scheduled.
                    // We aren't interested until the job actually starts getting processed.
                    return;
                case "Processing":
                    jobRunAttemptStatus = JobRunAttemptStatus.Processing;
                    break;
                case "Finished":
                    jobRunAttemptStatus = JobRunAttemptStatus.Succeeded;
                    break;
                case "Error":
                case "Canceling":
                case "Canceled":
                    jobRunAttemptStatus = JobRunAttemptStatus.Failed;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected value for event state: {eventState}");
            }

            log.LogInformation("Updating job run attempt status from Event Grid event. JobRunAttemptId={JobRunAttemptId}, JobRunAttemptStatus={JobRunAttemptStatus}, StatusTime={StatusTime}", jobRunAttemptId, jobRunAttemptStatus, statusTime);
            await SendStatusUpdate(durableEntityClient, jobRunAttemptId, jobRunAttemptStatus, statusTime);
        }

        private static string GetJobRunAttemptIdFromEventSubject(string eventSubject)
        {
            var match = EventSubjectRegex.Match(eventSubject);
            if (!match.Success) return null;

            return match.Groups[1].Value;
        }

        [FunctionName("SimulateStatusEvent")]
        public async Task<IActionResult> SimulateStatusEvent(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            var jobRunAttemptId = req.Query["jobRunAttemptId"].ToString();
            var status = Enum.Parse<JobRunAttemptStatus>(req.Query["status"]);

            await SendStatusUpdate(durableEntityClient, jobRunAttemptId, status, DateTime.UtcNow);

            return new OkResult();
        }

        private async Task SendStatusUpdate(IDurableEntityClient durableEntityClient, string jobRunAttemptId, JobRunAttemptStatus status, DateTimeOffset statusTime)
        {
            var entityId = new EntityId(nameof(JobRunAttempt), jobRunAttemptId);
            await durableEntityClient.SignalEntityAsync<IJobRunAttempt>(entityId, proxy => proxy.StatusUpdate((status, statusTime)));
        }
    }
}
