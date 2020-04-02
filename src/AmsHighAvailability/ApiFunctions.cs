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
    public static class ApiFunctions
    {
        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [DurableClient]IDurableEntityClient durableEntityClient,
            ILogger log)
        {
            var jobId = Guid.NewGuid().ToString();
            var inputData = "TODO-inputdata";
            var entityId = new EntityId(nameof(Job), jobId);

            await durableEntityClient.SignalEntityAsync<IJob>(entityId, proxy => proxy.Start(inputData));

            return new OkResult();
        }
    }
}