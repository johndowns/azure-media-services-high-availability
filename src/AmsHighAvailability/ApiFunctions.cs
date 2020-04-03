using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using AmsHighAvailability.Entities;

namespace AmsHighAvailability
{
    public class ApiFunctions
    {
        [FunctionName("CreateJob")]
        public async Task<IActionResult> CreateJob(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            var jobId = Guid.NewGuid().ToString();
            var inputData = "TODO-inputdata";
            var entityId = new EntityId(nameof(Job), jobId);

            await durableEntityClient.SignalEntityAsync<IJob>(entityId, proxy => proxy.Start(inputData));

            log.LogInformation("Started job. JobId={JobId}", jobId);

            return new OkResult();
        }
    }
}
