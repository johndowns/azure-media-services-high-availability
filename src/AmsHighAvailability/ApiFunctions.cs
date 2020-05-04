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

namespace AmsHighAvailability
{
    public class ApiFunctions
    {
        [FunctionName("CreateJobCoordinator")]
        public async Task<IActionResult> CreateJobCoordinator(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "jobCoordinators")] CreateJobCoordinatorRequest jobRequest,
            HttpRequest req,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            if (string.IsNullOrEmpty(jobRequest.MediaFileUrl))
            {
                return new BadRequestErrorMessageResult("Please provide a MediaFileUrl property in the request body.");
            }

            // Start the job by signalling the entity.
            var jobCoordinatorId = Guid.NewGuid().ToString();
            var entityId = new EntityId(nameof(JobCoordinatorEntity), jobCoordinatorId);
            await durableEntityClient.SignalEntityAsync<IJobCoordinatorEntity>(entityId, proxy => proxy.Start(jobRequest.MediaFileUrl));

            log.LogDebug("Initiated job coordinator. JobCoordinatorEntityId={JobCoordinatorEntityId}", jobCoordinatorId);

            var jobCoordinatorLocation = $"{req.Scheme}://{req.Host}/api/jobCoordinators/{jobCoordinatorId}";
            return new AcceptedResult(jobCoordinatorLocation, null);
        }

        [FunctionName("GetJobCoordinator")]
        public async Task<IActionResult> GetJobCoordinator(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "jobCoordinators/{jobCoordinatorId}")] CreateJobCoordinatorRequest jobRequest,
            HttpRequest req,
            string jobCoordinatorId,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            var entityId = new EntityId(nameof(JobCoordinatorEntity), jobCoordinatorId);
            var entityState = await durableEntityClient.ReadEntityStateAsync<JobCoordinatorEntity>(entityId);
            
            if (!entityState.EntityExists)
            {
                return new NotFoundResult();
            }

            var response = new JobCoordinatorStateResponse
            {
                JobState = entityState.EntityState.State.ToString(),
                MediaFileUrl = entityState.EntityState.InputMediaFileUrl
            };

            if (entityState.EntityState.State == ExtendedJobState.Succeeded)
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
    public class CreateJobCoordinatorRequest
    {
        public string MediaFileUrl { get; set; }
    }

    public class JobCoordinatorStateResponse
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
