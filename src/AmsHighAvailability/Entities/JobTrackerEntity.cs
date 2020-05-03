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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AmsHighAvailability.Entities
{
    public interface IJobTrackerEntity
    {
        Task Start((string inputMediaFileUrl, string amsInstanceId) arguments);

        Task ReceiveStatusUpdate((ExtendedJobState newStatus, DateTimeOffset statusTime) arguments);

        void ReceiveOutputStatusUpdate(DateTimeOffset statusTime);

        Task CheckIfJobStateIsCurrent();

        Task CheckIfJobHasTimedOut();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class JobTrackerEntity : IJobTrackerEntity
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
        public ExtendedJobState CurrentStatus { get; set; }

        [JsonProperty("statusHistory")]
        public HashSet<JobTrackerStatusHistory> StatusHistory { get; set; } = new HashSet<JobTrackerStatusHistory>();

        [JsonIgnore]
        public DateTimeOffset? LastTimeSeenStatusUpdate => StatusHistory.Max(sh => sh.StatusTime);

        [JsonProperty("lastTimeSeenJobProgress")]
        public DateTimeOffset? LastTimeSeenJobProgress { get; set; } = null;

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
            if (! isSubmittedSuccessfully)
            {
                _log.LogInformation("Failed to start job in Azure Media Services. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, AmsInstanceId={AmsInstanceId}",
                    JobCoordinatorEntityId, JobTrackerEntityId, AmsInstanceId);
                await UpdateCurrentStatus(ExtendedJobState.Failed);
                return;
            }

            _log.LogInformation("Successfully submitted job to Azure Media Services. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, AmsInstanceId={AmsInstanceId}",
                JobCoordinatorEntityId, JobTrackerEntityId, AmsInstanceId);

            // Update our internal bookkeeping to track the fact that the job has just been submitted.
            var timeJobSubmitted = DateTime.UtcNow;
            Assets = assets;
            await UpdateCurrentStatus(ExtendedJobState.Processing);
            LastTimeSeenJobProgress = timeJobSubmitted;
            StatusHistory.Add(new JobTrackerStatusHistory { Status = ExtendedJobState.Submitted, StatusTime = timeJobSubmitted, TimeReceived = timeJobSubmitted });

            // Schedule the checks we need to perform to detect state update failures and job timeouts.
            ScheduleNextJobStateCurrencyCheck();
            ScheduleNextJobTimeoutCheck();
        }

        private async Task UpdateCurrentStatus(ExtendedJobState status)
        {
            CurrentStatus = status;

            if (CurrentStatus == ExtendedJobState.Submitted || CurrentStatus == ExtendedJobState.Processing)
            {
                return;
            }

            // Signal the coordinator that we have an update.
            _log.LogInformation("Job tracker is updating job coordinator. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, JobCoordinatorEntityId={JobCoordinatorEntityId}, Status={Status}",
                JobCoordinatorEntityId, JobTrackerEntityId, JobCoordinatorEntityId, CurrentStatus);
            
            if (CurrentStatus == ExtendedJobState.Succeeded)
            {
                // Get the latest details about the job's output assets so we can send them back in API responses.
                await UpdateAssets();
                Entity.Current.SignalEntity<IJobCoordinatorEntity>(
                    new EntityId(nameof(JobCoordinatorEntity), JobCoordinatorEntityId),
                    proxy => proxy.MarkTrackerAsSucceeded((JobTrackerEntityId, Assets)));
            }
            else
            {
                Entity.Current.SignalEntity<IJobCoordinatorEntity>(
                    new EntityId(nameof(JobCoordinatorEntity), JobCoordinatorEntityId),
                    proxy => proxy.MarkTrackerAsFailed((JobTrackerEntityId, CurrentStatus)));
            }
        }

        public void ReceiveOutputStatusUpdate(DateTimeOffset statusTime)
        {
            _log.LogInformation("Received status update for job tracker from output. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, Time={StatusTime}",
                JobCoordinatorEntityId, JobTrackerEntityId, statusTime);
            StatusHistory.Add(new JobTrackerStatusHistory { StatusTime = statusTime, TimeReceived = DateTimeOffset.Now });

            // Any updates of a job output are considered to be progress in the job, since the output entity will have filtered them if they aren't.
            if (statusTime > LastTimeSeenJobProgress)
            {
                LastTimeSeenJobProgress = statusTime;
            }
        }

        public async Task ReceiveStatusUpdate((ExtendedJobState newStatus, DateTimeOffset statusTime) arguments)
        {
            _log.LogInformation("Received status update for job tracker. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, Time={StatusTime}, JobTrackerStatus={JobTrackerStatus}",
                JobCoordinatorEntityId, JobTrackerEntityId, arguments.statusTime, arguments.newStatus);
            StatusHistory.Add(new JobTrackerStatusHistory { Status = arguments.newStatus, StatusTime = arguments.statusTime, TimeReceived = DateTimeOffset.Now });

            // If this status update shows the job has made some progress, we will mark it as the current status and update the job accordingly.
            if ((CurrentStatus == ExtendedJobState.Submitted && arguments.newStatus != ExtendedJobState.Submitted) ||
                (CurrentStatus == ExtendedJobState.Processing && arguments.newStatus != ExtendedJobState.Processing))
            {
                if (arguments.statusTime > LastTimeSeenJobProgress)
                {
                    LastTimeSeenJobProgress = arguments.statusTime;
                    await UpdateCurrentStatus(arguments.newStatus);
                }
            }

            // If the AMS job is still processing, make sure we schedule the next 'pull' status check.
            if (CurrentStatus == ExtendedJobState.Processing)
            {
                ScheduleNextJobStateCurrencyCheck();
            }
        }

        private void ScheduleNextJobStateCurrencyCheck()
        {
            if (CurrentStatus == ExtendedJobState.Succeeded || CurrentStatus == ExtendedJobState.Failed || CurrentStatus == ExtendedJobState.TimedOut)
            {
                _log.LogInformation("Skipped scheduling a new status check since tracker is in a final state. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, CurrentStatus={CurrentStatus}",
                    JobCoordinatorEntityId, JobTrackerEntityId, CurrentStatus);
                return;
            }

            var statusTimeoutTimeUtc = DateTime.UtcNow.Add(_settings.JobTrackerCurrencyCheckInterval);
            Entity.Current.SignalEntity(
                Entity.Current.EntityId,
                statusTimeoutTimeUtc,
                nameof(CheckIfJobStateIsCurrent)); // HACK: this is not using the SignalEntity<T> overload due to this bug: https://github.com/Azure/azure-functions-durable-extension/issues/1282

            _log.LogInformation("Scheduled tracker to check if the job state is current. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, CheckTime={CheckTime}",
                JobCoordinatorEntityId, JobTrackerEntityId, statusTimeoutTimeUtc);
        }

        public async Task CheckIfJobStateIsCurrent()
        {
            _log.LogInformation("Checking whether job tracker has a current job state. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, LastTimeSeenStatusUpdate={LastTimeSeenStatusUpdate}",
                JobCoordinatorEntityId, JobTrackerEntityId, LastTimeSeenStatusUpdate);

            if (CurrentStatus == ExtendedJobState.Succeeded || CurrentStatus == ExtendedJobState.Failed || CurrentStatus == ExtendedJobState.TimedOut)
            {
                // We don't expect state updates if the job isn't actively processing.
                _log.LogInformation("Tracker is no longer in 'processing' state, so no further state updates are needed. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, LastTimeSeenJobProgress={LastTimeSeenJobProgress}",
                    JobCoordinatorEntityId, JobTrackerEntityId, LastTimeSeenJobProgress);
                return;
            }

            if (LastTimeSeenStatusUpdate < DateTime.UtcNow.Subtract(_settings.JobTrackerCurrencyThreshold))
            {
                // We haven't seen any updates from this job recently, so we need to trigger a manual poll of the job status.
                _log.LogInformation("Pulling job state. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, LastTimeSeenJobProgress={LastTimeSeenJobProgress}",
                    JobCoordinatorEntityId, JobTrackerEntityId, LastTimeSeenJobProgress);

                var retrievedTime = DateTimeOffset.UtcNow;
                var jobCurrentState = await _mediaServicesJobService.GetJobStatus(
                    AmsInstanceConfiguration.MediaServicesSubscriptionId, AmsInstanceConfiguration.MediaServicesResourceGroupName, AmsInstanceConfiguration.MediaServicesInstanceName,
                    JobTrackerEntityId);

                // We then send the updates back to the relevant entities through the standard status notification process.
                Entity.Current.SignalEntity<IJobTrackerEntity>(
                    Entity.Current.EntityId,
                    proxy => proxy.ReceiveStatusUpdate((jobCurrentState.State, retrievedTime)));

                foreach (var outputState in jobCurrentState.OutputStates)
                {
                    _log.LogInformation($"TODO signalling entity {outputState.Label}"); // TODO check labels are the entity ID (they should be)
                    Entity.Current.SignalEntity<IJobOutputTrackerEntity>(
                        new EntityId(nameof(JobOutputTrackerEntity), outputState.Label),
                        proxy => proxy.ReceiveStatusUpdate((outputState.State, outputState.Progress, retrievedTime)));
                }
            }

            ScheduleNextJobStateCurrencyCheck();
        }

        private void ScheduleNextJobTimeoutCheck()
        {
            if (CurrentStatus == ExtendedJobState.Succeeded || CurrentStatus == ExtendedJobState.Failed || CurrentStatus == ExtendedJobState.TimedOut)
            {
                _log.LogInformation("Skipped scheduling a new timeout check since tracker is in a final state. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, CurrentStatus={CurrentStatus}",
                    JobCoordinatorEntityId, JobTrackerEntityId, CurrentStatus);
                return;
            }

            var statusTimeoutTimeUtc = DateTime.UtcNow.Add(_settings.JobTrackerTimeoutCheckInterval);
            Entity.Current.SignalEntity(
                Entity.Current.EntityId,
                statusTimeoutTimeUtc,
                nameof(CheckIfJobHasTimedOut)); // HACK: this is not using the SignalEntity<T> overload due to this bug: https://github.com/Azure/azure-functions-durable-extension/issues/1282

            _log.LogInformation("Scheduled tracker to check if the job has timed out. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, CheckTime={CheckTime}",
                JobCoordinatorEntityId, JobTrackerEntityId, statusTimeoutTimeUtc);
        }

        public async Task CheckIfJobHasTimedOut()
        {
            _log.LogInformation("Checking whether job has timed out. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, LastTimeSeenJobProgress={LastTimeSeenJobProgress}",
                  JobCoordinatorEntityId, JobTrackerEntityId, LastTimeSeenJobProgress);

            if (CurrentStatus == ExtendedJobState.Succeeded || CurrentStatus == ExtendedJobState.Failed || CurrentStatus == ExtendedJobState.TimedOut)
            {
                // We don't expect state updates if the job isn't actively processing.
                _log.LogInformation("Tracker is no longer in 'processing' state, so no further state updates are needed. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, LastTimeSeenJobProgress={LastTimeSeenJobProgress}",
                    JobCoordinatorEntityId, JobTrackerEntityId, LastTimeSeenJobProgress);
                return;
            }

            if (LastTimeSeenJobProgress < DateTime.UtcNow.Subtract(_settings.JobTrackerTimeoutThreshold))
            {
                // It has been too long since we have seen any progress being made on the job.
                // This means we consider the job to have timed out.
                _log.LogInformation("Job has not seen progress within the timeout threshold. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}",
                  JobCoordinatorEntityId, JobTrackerEntityId, LastTimeSeenJobProgress);

                await UpdateCurrentStatus(ExtendedJobState.TimedOut);
            }

            ScheduleNextJobTimeoutCheck();
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
