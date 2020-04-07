using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Rest;

namespace AmsHighAvailability.Services
{
    public interface IMediaServicesJobService
    {
        Task<bool> SubmitJobToMediaServicesEndpointAsync(
            string subscriptionId, string resourceGroupName, string mediaServicesInstanceName,
            string inputMediaUrl,
            string jobName);
    }

    public class MediaServicesJobService : IMediaServicesJobService
    {
        // The implementation in this class is based on this example:
        // https://github.com/Azure-Samples/media-services-v3-dotnet-quickstarts/tree/master/AMSV3Quickstarts/EncodeAndStreamFiles

        private const string AdaptiveStreamingTransformName = "MyTransformWithAdaptiveStreamingPresetMultiple";
        private const int OutputCount = 2;

        public async Task<bool> SubmitJobToMediaServicesEndpointAsync(
            string subscriptionId, string resourceGroupName, string mediaServicesInstanceName,
            string inputMediaUrl,
            string jobName)
        {
            // Authenticate to Azure.
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var accessToken = await azureServiceTokenProvider.GetAccessTokenAsync("https://management.azure.com");
            ServiceClientCredentials credentials = new TokenCredentials(accessToken);

            // Establish a connection to Media Services.
            var client = new AzureMediaServicesClient(credentials)
            {
                SubscriptionId = subscriptionId
            };

            // Ensure the transform profile exists.
            await GetOrCreateTransformAsync(client, resourceGroupName, mediaServicesInstanceName, AdaptiveStreamingTransformName);

            // Create output assets to receive the job outputs.
            var outputAssetNames = new string[OutputCount];
            for (var i = 0; i < OutputCount; i++)
            {
                outputAssetNames[i] = $"{jobName}|{i}";
                await CreateOutputAssetAsync(client, resourceGroupName, mediaServicesInstanceName, outputAssetNames[i]);
            }

            // Submit the job to Media Services
            try
            {
                var job = await SubmitJobAsync(client, resourceGroupName, mediaServicesInstanceName, AdaptiveStreamingTransformName, outputAssetNames, jobName, inputMediaUrl);
                return job.State == JobState.Queued || job.State == JobState.Scheduled;
            }
            catch (ApiErrorException ex)
            {
                throw new Exception(ex.Body.Error.Message);
            }            
        }

        private async Task<Transform> GetOrCreateTransformAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName)
        {
            // Does a Transform already exist with the desired name? Assume that an existing Transform with the desired name
            // also uses the same recipe or Preset for processing content.
            var transform = await client.Transforms.GetAsync(resourceGroupName, accountName, transformName);
            if (transform != null) return transform;

            // You need to specify what you want it to produce as an output
            var output = new TransformOutput[]
            {
                // There are two outputs on this transform.
                new TransformOutput
                {
                    // The preset for the Transform is set to one of Media Services built-in sample presets.
                    // You can  customize the encoding settings by changing this to use "StandardEncoderPreset" class.
                    Preset = new BuiltInStandardEncoderPreset()
                    {
                        // This sample uses the built-in encoding preset for Adaptive Bitrate Streaming.
                        PresetName = EncoderNamedPreset.AdaptiveStreaming
                    }
                },
                new TransformOutput(new AudioAnalyzerPreset("en-US"))
            };

            // Create the Transform with the output defined above
            return await client.Transforms.CreateOrUpdateAsync(resourceGroupName, accountName, transformName, output);
        }

        private Task<Asset> CreateOutputAssetAsync(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string assetName)
        {
            return client.Assets.CreateOrUpdateAsync(resourceGroupName, accountName, assetName, new Asset());
        }

        private async Task<Job> SubmitJobAsync(IAzureMediaServicesClient client,
            string resourceGroup,
            string accountName,
            string transformName,
            string[] outputAssetNames,
            string jobName,
            string inputMediaUrl)
        {
            // This example shows how to encode from any HTTPs source URL - a new feature of the v3 API.  
            // Change the URL to any accessible HTTPs URL or SAS URL from Azure.
            var jobInput = new JobInputHttp(files: new[] { inputMediaUrl });

            JobOutput[] jobOutputs = outputAssetNames.Select(n => new JobOutputAsset(n)).ToArray();

            // In this example, we are assuming that the job name is unique.
            //
            // If you already have a job with the desired name, use the Jobs.Get method
            // to get the existing job. In Media Services v3, Get methods on entities returns null 
            // if the entity doesn't exist (a case-insensitive check on the name).
            var job = await client.Jobs.CreateAsync(
                resourceGroup,
                accountName,
                transformName,
                jobName,
                new Job
                {
                    Input = jobInput,
                    Outputs = jobOutputs,
                });

            return job;
        }

    }
}
