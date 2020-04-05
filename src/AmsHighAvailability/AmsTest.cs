using AmsHighAvailability.Services;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Web.Http;

namespace AmsHighAvailability
{
    public class AmsTest
    {
        [FunctionName("AmsTest")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var subscriptionId = "d178c7c4-ffb7-467e-a397-042c1d428092";
            var resourceGroupName = "AmsJob";
            var mediaServicesInstanceName = "jdamsncus";

            var inputMediaUrl = "https://nimbuscdn-nimbuspm.streaming.mediaservices.windows.net/2b533311-b215-4409-80af-529c3e853622/Ignite-short.mp4";
            var jobName = Guid.NewGuid().ToString();
            var outputAssetName = jobName;

            var service = new MediaServicesJobService();
            var isSubmittedSuccessfully = await service.SubmitJobToMediaServicesEndpointAsync(subscriptionId, resourceGroupName, mediaServicesInstanceName, inputMediaUrl, jobName, outputAssetName);

            return isSubmittedSuccessfully ? new OkResult() : (IActionResult)new InternalServerErrorResult();
        }
    }
}
