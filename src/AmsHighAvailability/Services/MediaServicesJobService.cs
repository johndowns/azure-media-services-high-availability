using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AmsHighAvailability.Models;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Rest;

namespace AmsHighAvailability.Services
{
    public interface IMediaServicesJobService
    {
        Task<(bool success, IEnumerable<AmsAsset> assets)> SubmitJobToMediaServicesEndpointAsync(
            string subscriptionId, string resourceGroupName, string mediaServicesInstanceName,
            string inputMediaUrl,
            string jobName);

        Task<(string storageAccountName, string container)> GetAssetDetails(
            string subscriptionId, string resourceGroupName, string mediaServicesInstanceName,
            string assetName);

        Task<AmsJobCurrentState> GetJobStatus(
            string subscriptionId, string resourceGroupName, string mediaServicesInstanceName,
            string jobName);
    }

    public class MediaServicesJobService : IMediaServicesJobService
    {
        // The implementation in this class is based on this example:
        // https://github.com/Azure-Samples/media-services-v3-dotnet-quickstarts/tree/master/AMSV3Quickstarts/EncodeAndStreamFiles

        private const string AdaptiveStreamingTransformName = "MyTransformWithAdaptiveStreamingPresetMultiple";
        private static readonly TransformOutput[] Outputs = new TransformOutput[]
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

        public async Task<(bool success, IEnumerable<AmsAsset> assets)> SubmitJobToMediaServicesEndpointAsync(
            string subscriptionId, string resourceGroupName, string mediaServicesInstanceName,
            string inputMediaUrl,
            string jobName)
        {
            var client = await GetAzureMediaServicesClient(subscriptionId); // TODO should this be reused?

            // Ensure the transform profile exists.
            await GetOrCreateTransformAsync(client, resourceGroupName, mediaServicesInstanceName, AdaptiveStreamingTransformName);

            // Create output assets to receive the job outputs.
            var outputAssets = new List<AmsAsset>();
            for (var i = 0; i < Outputs.Length; i++)
            {
                var assetName = $"{jobName}|{i}";
                var asset = await CreateOutputAssetAsync(client, resourceGroupName, mediaServicesInstanceName, assetName);
                outputAssets.Add(new AmsAsset
                {
                    StorageAccountName = asset.StorageAccountName,
                    Container = asset.Container,
                    Name = asset.Name
                });
            }

            // Submit the job to Media Services.
            try
            {
                var job = await SubmitJobAsync(client, resourceGroupName, mediaServicesInstanceName, AdaptiveStreamingTransformName, outputAssets.Select(a => a.Name).ToArray(), jobName, inputMediaUrl);
                var success = job.State == JobState.Queued || job.State == JobState.Scheduled;
                return (success, outputAssets);
            }
            catch (ApiErrorException ex)
            {
                throw new Exception(ex.Body.Error.Message);
            }
        }

        public async Task<(string storageAccountName, string container)> GetAssetDetails(
            string subscriptionId, string resourceGroupName, string mediaServicesInstanceName,
            string assetName)
        {
            var client = await GetAzureMediaServicesClient(subscriptionId); // TODO should this be reused?

            var asset = await client.Assets.GetAsync(
                resourceGroupName, mediaServicesInstanceName,
                assetName);
            return (asset.StorageAccountName, asset.Container);
        }

        public async Task<AmsJobCurrentState> GetJobStatus(
            string subscriptionId, string resourceGroupName, string mediaServicesInstanceName,
            string jobName)
        {
            var client = await GetAzureMediaServicesClient(subscriptionId); // TODO should this be reused?

            var job = await client.Jobs.GetAsync(
                resourceGroupName, mediaServicesInstanceName,
                AdaptiveStreamingTransformName, jobName);
            var outputStates = job.Outputs.Select(o => new AmsJobOutputCurrentState
            {
                Label = o.Label,
                State = o.State.ToExtendedJobState(),
                Progress = o.Progress
            });
            return new AmsJobCurrentState
            {
                State = job.State.ToExtendedJobState(),
                OutputStates = outputStates
            };            
        }

        private async Task<AzureMediaServicesClient> GetAzureMediaServicesClient(string subscriptionId)
        {
            // Authenticate to Azure.
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var accessToken = await azureServiceTokenProvider.GetAccessTokenAsync("https://management.azure.com");
            ServiceClientCredentials credentials = new TokenCredentials(accessToken);

            // Establish a connection to Media Services.
            return new AzureMediaServicesClient(credentials)
            {
                SubscriptionId = subscriptionId
            };
        }

        private async Task<Transform> GetOrCreateTransformAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName, string mediaServicesInstanceName,
            string transformName)
        {
            // Does a Transform already exist with the desired name? Assume that an existing Transform with the desired name
            // also uses the same recipe or Preset for processing content.
            var transform = await client.Transforms.GetAsync(
                resourceGroupName, mediaServicesInstanceName,
                transformName);
            if (transform != null) return transform;

            // Create the Transform with the output defined above
            return await client.Transforms.CreateOrUpdateAsync(
                resourceGroupName, mediaServicesInstanceName,
                transformName,
                Outputs);
        }

        private Task<Asset> CreateOutputAssetAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName, string mediaServicesInstanceName,
            string assetName)
        {
            return client.Assets.CreateOrUpdateAsync(
                resourceGroupName, mediaServicesInstanceName,
                assetName,
                new Asset());
        }

        private async Task<Job> SubmitJobAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName, string mediaServicesInstanceName,
            string transformName,string[] outputAssetNames, string jobName, string inputMediaUrl)
        {
            // This example shows how to encode from any HTTPs source URL - a new feature of the v3 API.  
            // Change the URL to any accessible HTTPs URL or SAS URL from Azure.
            var jobInput = new JobInputHttp(files: new[] { inputMediaUrl });

            JobOutput[] jobOutputs = outputAssetNames.Select(n => new JobOutputAsset(n, label: n)).ToArray();

            // In this example, we are assuming that the job name is unique.
            //
            // If you already have a job with the desired name, use the Jobs.Get method
            // to get the existing job. In Media Services v3, Get methods on entities returns null 
            // if the entity doesn't exist (a case-insensitive check on the name).
            var job = await client.Jobs.CreateAsync(
                resourceGroupName, mediaServicesInstanceName,
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

    public class AmsJobCurrentState
    {
        public ExtendedJobState State { get; set; }

        public IEnumerable<AmsJobOutputCurrentState> OutputStates { get; set; }
    }

    public class AmsJobOutputCurrentState
    {
        public string Label { get; set; }

        public ExtendedJobState State { get; set; }

        public int Progress { get; set; }
    }
}
