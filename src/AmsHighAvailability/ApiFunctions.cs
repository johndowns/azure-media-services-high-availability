using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using AmsHighAvailability.Entities;
using System.Web.Http;

namespace AmsHighAvailability
{
    public class ApiFunctions
    {
        [FunctionName("CreateJob")]
        public async Task<IActionResult> CreateJob(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] CreateJobRequest jobRequest,
            HttpRequest req,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            if (string.IsNullOrEmpty(jobRequest.MediaFileUrl))
            {
                return new BadRequestErrorMessageResult("Please provide a MediaFileUrl property in the request body.");
            }

            // Start the job by signalling the entity.
            var jobId = Guid.NewGuid().ToString();
            var entityId = new EntityId(nameof(Job), jobId);
            await durableEntityClient.SignalEntityAsync<IJob>(entityId, proxy => proxy.Start(jobRequest.MediaFileUrl));

            log.LogInformation("Initiated job. JobId={JobId}", jobId);

            return new OkObjectResult(new
            {
                jobId
            });
        }
    }

    public class CreateJobRequest
    {
        public string MediaFileUrl { get; set; } // e.g. https://nimbuscdn-nimbuspm.streaming.mediaservices.windows.net/2b533311-b215-4409-80af-529c3e853622/Ignite-short.mp4
    }
}
