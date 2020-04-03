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

namespace AmsHighAvailability
{
    public class EventGridFunctions
    {
        [FunctionName("AmsStatusUpdate")]
        public async Task AmsStatusUpdate(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            log.LogInformation(eventGridEvent.Data.ToString());

            var jobRunAttemptId = "TODO"; //req.Query["jobRunAttemptId"].ToString();
            var status = JobRunAttemptStatus.Processing;

            await SendStatusUpdate(durableEntityClient, jobRunAttemptId, status);
        }

        [FunctionName("SimulateStatusEvent")]
        public async Task<IActionResult> SimulateStatusEvent(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            var jobRunAttemptId = req.Query["jobRunAttemptId"].ToString();
            var status = JobRunAttemptStatus.Processing;

            await SendStatusUpdate(durableEntityClient, jobRunAttemptId, status);

            return new OkResult();
        }

        private async Task SendStatusUpdate(IDurableEntityClient durableEntityClient, string jobRunAttemptId, JobRunAttemptStatus status)
        {
            var entityId = new EntityId(nameof(JobRunAttempt), jobRunAttemptId);
            await durableEntityClient.SignalEntityAsync<IJobRunAttempt>(entityId, proxy => proxy.StatusUpdate((status, DateTimeOffset.UtcNow)));
        }
    }
}
