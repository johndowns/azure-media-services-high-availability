using AmsHighAvailability.Configuration;
using AmsHighAvailability.Models;
using AmsHighAvailability.Services;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AmsHighAvailability.Entities
{
    public interface IJobTracker
    {
        Task Start((string inputMediaFileUrl, string amsInstanceId) arguments);

        Task StatusUpdate((AmsStatus newStatus, DateTimeOffset statusTime) arguments);

        Task CheckForStatusTimeout();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class JobTrackerEntity : IJobTracker
    {
        [JsonProperty("jobCoordinatorEntityId")]
        public string JobCoordinatorEntityId => JobTrackerEntityId.Split('|')[0];

        [JsonProperty("jobTrackerEntityId")]
        public string JobTrackerEntityId => Entity.Current.EntityKey;

        [JsonProperty("amsInstanceId")]
        public string AmsInstanceId { get; set; }

        [JsonProperty("amsInstanceConfiguration")]
        public AmsInstanceConfiguration AmsInstanceConfiguration { get; set; }

        [JsonProperty("assets")]
        public IEnumerable<AmsAsset> Assets { get; set; }

        [JsonProperty("submittedTime")]
        public DateTimeOffset SubmittedTime { get; set; }

        [JsonProperty("completedTime")]
        public DateTimeOffset? CompletedTime { get; set; } = null;

        [JsonProperty("status")]
        public AmsStatus CurrentStatus { get; set; }

        private async Task UpdateCurrentStatus(AmsStatus status)
        {
            CurrentStatus = status;

            if (CurrentStatus == AmsStatus.Received || CurrentStatus == AmsStatus.Processing)
            {
                return;
            }

            // Signal the coordinator that we have an update.
            _log.LogInformation("Job tracker is updating job coordinator. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, JobCoordinatorEntityId={JobCoordinatorEntityId}, Status={Status}",
                JobCoordinatorEntityId, JobTrackerEntityId, JobCoordinatorEntityId, CurrentStatus);
            switch (CurrentStatus)
            {
                case AmsStatus.TimedOut:
                    Entity.Current.SignalEntity<IJobCoordinatorEntity>(new EntityId(nameof(JobCoordinatorEntity), JobCoordinatorEntityId), proxy => proxy.MarkTrackerAsTimedOut(JobTrackerEntityId));
                    break;
                case AmsStatus.Succeeded:
                    await UpdateAssets();
                    Entity.Current.SignalEntity<IJobCoordinatorEntity>(new EntityId(nameof(JobCoordinatorEntity), JobCoordinatorEntityId), proxy => proxy.MarkTrackerAsSucceeded((JobTrackerEntityId, Assets)));
                    break;
                case AmsStatus.Failed:
                    Entity.Current.SignalEntity<IJobCoordinatorEntity>(new EntityId(nameof(JobCoordinatorEntity), JobCoordinatorEntityId), proxy => proxy.MarkTrackerAsFailed(JobTrackerEntityId));
                    break;
            }
        }

        public HashSet<JobTrackerStatusHistory> StatusHistory { get; set; } = new HashSet<JobTrackerStatusHistory>();

        [JsonProperty("lastStatusUpdateReceivedTime")]
        public DateTimeOffset? LastStatusUpdateReceivedTime { get; set; } = null;

        [JsonIgnore]
        private readonly IMediaServicesJobService _mediaServicesJobService;

        [JsonIgnore]
        private readonly Configuration.Options _settings;

        [JsonIgnore]
        private readonly ILogger _log;

        public JobTrackerEntity(ILogger log, IMediaServicesJobService mediaServicesJobService, IOptions<Configuration.Options> options)
        {
            this._log = log;
            this._mediaServicesJobService = mediaServicesJobService;
            this._settings = options.Value;
        }

        [FunctionName(nameof(JobTrackerEntity))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
            => ctx.DispatchAsync<JobTrackerEntity>(log);

        public async Task Start((string inputMediaFileUrl, string amsInstanceId) arguments)
        {
            // Set up the internal metadata.
            AmsInstanceId = arguments.amsInstanceId;
            SubmittedTime = DateTime.UtcNow;

            // Find the details of the AMS instance that will be used for this job tracker.
            AmsInstanceConfiguration = _settings.GetAmsInstanceConfiguration(AmsInstanceId);

            // Submit the job for processing.
            var (isSubmittedSuccessfully, assets) = await _mediaServicesJobService.SubmitJobToMediaServicesEndpointAsync(
                AmsInstanceConfiguration.MediaServicesSubscriptionId, AmsInstanceConfiguration.MediaServicesResourceGroupName, AmsInstanceConfiguration.MediaServicesInstanceName,
                arguments.inputMediaFileUrl,
                JobTrackerEntityId);

            // Update this entity's status.
            if (isSubmittedSuccessfully)
            {
                _log.LogInformation("Successfully submitted job to Azure Media Services. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, AmsInstanceId={AmsInstanceId}",
                    JobCoordinatorEntityId, JobTrackerEntityId, AmsInstanceId);
                Assets = assets;
                await UpdateCurrentStatus(AmsStatus.Processing);

                // Make a note to ourselves to check for a status update timeout.
                LastStatusUpdateReceivedTime = DateTime.UtcNow;
                ScheduleNextStatusTimeoutCheck();
            }
            else
            {
                _log.LogInformation("Failed to start job in Azure Media Services. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, AmsInstanceId={AmsInstanceId}",
                    JobCoordinatorEntityId, JobTrackerEntityId, AmsInstanceId);
                await UpdateCurrentStatus(AmsStatus.Failed);
            }
        }

        public async Task StatusUpdate((AmsStatus newStatus, DateTimeOffset statusTime) arguments)
        {
            _log.LogInformation("Received status update for job tracker. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, Time={StatusTime}, JobTrackerStatus={JobTrackerStatus}",
                JobCoordinatorEntityId, JobTrackerEntityId, arguments.statusTime, arguments.newStatus);
            StatusHistory.Add(new JobTrackerStatusHistory { Status = arguments.newStatus, StatusTime = arguments.statusTime, TimeReceived = DateTimeOffset.Now });

            // If this status update shows forward progress, we will mark it as the current status and update the job accordingly.
            if (arguments.statusTime > LastStatusUpdateReceivedTime)
            {
                LastStatusUpdateReceivedTime = arguments.statusTime;
                await UpdateCurrentStatus(arguments.newStatus);
            }

            // If the AMS job is still processing, make sure we schedule a timeout check.
            if (CurrentStatus == AmsStatus.Processing)
            {
                ScheduleNextStatusTimeoutCheck();
            }
        }

        public async Task CheckForStatusTimeout()
        {
            _log.LogInformation("Checking for status timeout on job tracker. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, LastStatusUpdateReceivedTime={lastStatusUpdateReceivedTime}",
                JobCoordinatorEntityId, JobTrackerEntityId, LastStatusUpdateReceivedTime);

            if (CurrentStatus != AmsStatus.Processing)
            {
                // We don't need to time out if the job isn't actively processing.
                _log.LogInformation("Tracker is no longer in 'processing' state, so no further status updates are needed. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, LastStatusUpdateReceivedTime={lastStatusUpdateReceivedTime}",
                    JobCoordinatorEntityId, JobTrackerEntityId, LastStatusUpdateReceivedTime);
                return;
            }

            if (LastStatusUpdateReceivedTime < DateTime.UtcNow.Subtract(_settings.JobTrackerTimeoutThreshold))
            {
                // The last time we received a status update was more than 60 minutes ago.
                // This means we consider the job to have timed out.
                await UpdateCurrentStatus(AmsStatus.TimedOut);
            }
        }

        private void ScheduleNextStatusTimeoutCheck()
        {
            if (CurrentStatus == AmsStatus.Succeeded || CurrentStatus== AmsStatus.Failed || CurrentStatus == AmsStatus.TimedOut)
            {
                _log.LogInformation("Skipped scheduling a new status check since tracker status is terminal. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, CurrentStatus={CurrentStatus}",
                    JobCoordinatorEntityId, JobTrackerEntityId, CurrentStatus);
                return;
            }

            var statusTimeoutTimeUtc = DateTime.UtcNow.Add(_settings.JobTrackerStatusTimeoutCheckInterval);
            Entity.Current.SignalEntity<IJobTracker>(Entity.Current.EntityId, statusTimeoutTimeUtc, proxy => proxy.CheckForStatusTimeout());

            _log.LogInformation("Scheduled tracker to check for a status timeout. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, CheckTime={CheckTime}",
                JobCoordinatorEntityId, JobTrackerEntityId, statusTimeoutTimeUtc);
        }

        private async Task UpdateAssets()
        {
            foreach (var asset in Assets)
            {
                var (storageAccountName, container) = await _mediaServicesJobService.GetAssetDetails(
                    AmsInstanceConfiguration.MediaServicesSubscriptionId, AmsInstanceConfiguration.MediaServicesResourceGroupName, AmsInstanceConfiguration.MediaServicesInstanceName,
                    asset.Name);
                asset.StorageAccountName = storageAccountName;
                asset.Container = container;
            }
        }
    }
}
