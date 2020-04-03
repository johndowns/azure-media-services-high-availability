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
            var inputMediaFileUrl = "https://nimbuscdn-nimbuspm.streaming.mediaservices.windows.net/2b533311-b215-4409-80af-529c3e853622/Ignite-short.mp4";
            var entityId = new EntityId(nameof(Job), jobId);

            await durableEntityClient.SignalEntityAsync<IJob>(entityId, proxy => proxy.Start(inputMediaFileUrl));

            log.LogInformation("Initiated job. JobId={JobId}", jobId);

            return new OkObjectResult(new
            {
                jobId
            });
        }
    }
}
