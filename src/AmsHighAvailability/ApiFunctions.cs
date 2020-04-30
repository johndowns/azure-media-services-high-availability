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
using System.Collections.Generic;
using AmsHighAvailability.Models;
using System.Diagnostics;

namespace AmsHighAvailability
{
    public class ApiFunctions
    {
        [FunctionName("CreateJob")]
        public async Task<IActionResult> CreateJob(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "jobs")] CreateJobRequest jobRequest,
            HttpRequest req,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            Activity.Current.AddTag("TODOMyCustomTag", "MyCustomTagValue123");

            if (string.IsNullOrEmpty(jobRequest.MediaFileUrl))
            {
                return new BadRequestErrorMessageResult("Please provide a MediaFileUrl property in the request body.");
            }

            // Start the job by signalling the entity.
            var jobId = Guid.NewGuid().ToString();
            var entityId = new EntityId(nameof(JobCoordinatorEntity), jobId);
            await durableEntityClient.SignalEntityAsync<IJobCoordinatorEntity>(entityId, proxy => proxy.Start(jobRequest.MediaFileUrl));

            log.LogInformation("Initiated job. JobCoordinatorEntityId={JobCoordinatorEntityId}", jobId);

            var checkStatusLocation = $"{req.Scheme}://{req.Host}/api/jobs/{jobId}";
            return new AcceptedResult(checkStatusLocation, null);
        }

        [FunctionName("GetJob")]
        public async Task<IActionResult> GetJob(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "jobs/{jobId}")] CreateJobRequest jobRequest,
            HttpRequest req,
            string jobId,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            var entityId = new EntityId(nameof(JobCoordinatorEntity), jobId);
            var entityState = await durableEntityClient.ReadEntityStateAsync<JobCoordinatorEntity>(entityId);
            
            if (!entityState.EntityExists)
            {
                return new NotFoundResult();
            }

            var response = new JobStatusResponse
            {
                JobState = entityState.EntityState.Status.ToString(),
                MediaFileUrl = entityState.EntityState.InputMediaFileUrl
            };

            if (entityState.EntityState.Status == ExtendedJobState.Succeeded)
            {
                response.Outputs = new JobOutputsResponse
                {
                    ProcessedByAmsInstanceId = entityState.EntityState.CompletedJob.AmsInstanceId,
                    Assets = entityState.EntityState.CompletedJob.Assets
                };
            }

            return new OkObjectResult(response);
        }
    }

    #region API Request and Response Models
    public class CreateJobRequest
    {
        public string MediaFileUrl { get; set; }
    }

    public class JobStatusResponse
    {
        public string JobState { get; set; }

        public string MediaFileUrl { get; set; }

        public JobOutputsResponse Outputs { get; set; }
    }

    public class JobOutputsResponse
    {
        public string ProcessedByAmsInstanceId { get; set; }

        public IEnumerable<AmsAsset> Assets { get; set; }
    }
    #endregion
}
