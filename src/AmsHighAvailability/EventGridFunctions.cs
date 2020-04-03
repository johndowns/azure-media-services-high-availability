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
            var status = "TODO"; // req.Query["status"].ToString();

            var entityId = new EntityId(nameof(JobRunAttempt), jobRunAttemptId);
            await durableEntityClient.SignalEntityAsync<IJobRunAttempt>(entityId, proxy => proxy.StatusUpdate((status, DateTimeOffset.UtcNow)));
        }
    }
}
