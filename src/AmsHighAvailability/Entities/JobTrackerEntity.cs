using AmsHighAvailability.Configuration;
using AmsHighAvailability.Models;
using AmsHighAvailability.Services;
using AmsHighAvailability.Telemetry;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AmsHighAvailability.Entities
{
    public interface IJobTrackerEntity
    {
        Task Start((string inputMediaFileUrl, string amsInstanceId) arguments);

        Task ReceiveStateUpdate((ExtendedJobState state, DateTimeOffset timestamp) arguments);

        void ReceiveOutputStateUpdate(DateTimeOffset timestamp);

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

        [JsonProperty("state")]
        public ExtendedJobState State { get; set; }

        [JsonProperty("stateHistory")]
        public HashSet<JobTrackerStateHistory> StateHistory { get; set; } = new HashSet<JobTrackerStateHistory>();

        [JsonIgnore]
        public DateTimeOffset? LastTimeSeenStateUpdate => StateHistory.Max(sh => sh.Timestamp);

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
        public static async Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
        {
            TelemetryContext.SetEntityId(ctx.EntityId);
            await ctx.DispatchAsync<JobTrackerEntity>(log);
            TelemetryContext.Reset();
        }

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
                _log.LogError("Failed to start job in Azure Media Services. AmsInstanceId={AmsInstanceId}",
                    AmsInstanceId);
                await UpdateCurrentState(ExtendedJobState.Failed);
                return;
            }

            _log.LogInformation("Successfully submitted job to Azure Media Services. AmsInstanceId={AmsInstanceId}",
                AmsInstanceId);

            // Update our internal bookkeeping to track the fact that the job has just been submitted.
            var timeJobSubmitted = DateTime.UtcNow;
            Assets = assets;
            await UpdateCurrentState(ExtendedJobState.Processing);
            LastTimeSeenJobProgress = timeJobSubmitted;
            StateHistory.Add(new JobTrackerStateHistory { State = ExtendedJobState.Submitted, Timestamp = timeJobSubmitted, TimeReceived = timeJobSubmitted });

            // Schedule the checks we need to perform to detect state update failures and job timeouts.
            ScheduleNextJobStateCurrencyCheck();
            ScheduleNextJobTimeoutCheck();
        }

        private async Task UpdateCurrentState(ExtendedJobState state)
        {
            State = state;

            if (State == ExtendedJobState.Submitted || State == ExtendedJobState.Processing)
            {
                return;
            }

            // Signal the coordinator that we have an update.
            _log.LogDebug("Job tracker is updating job coordinator. State={State}",
                State);
            
            if (State == ExtendedJobState.Succeeded)
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
                    proxy => proxy.MarkTrackerAsFailed((JobTrackerEntityId, State)));
            }
        }

        public void ReceiveOutputStateUpdate(DateTimeOffset timestamp)
        {
            _log.LogInformation("Received update for job tracker from job output. Timestamp={Timestamp}",
                timestamp);
            StateHistory.Add(new JobTrackerStateHistory { Timestamp = timestamp, TimeReceived = DateTimeOffset.Now });

            // Any updates of a job output are considered to be progress in the job, since the output entity will have filtered them if they aren't.
            if (timestamp > LastTimeSeenJobProgress)
            {
                LastTimeSeenJobProgress = timestamp;
            }
        }

        public async Task ReceiveStateUpdate((ExtendedJobState state, DateTimeOffset timestamp) arguments)
        {
            _log.LogInformation("Received update for job tracker. Timestamp={Timestamp}, JobTrackerState={JobTrackerState}",
                arguments.timestamp, arguments.state);
            StateHistory.Add(new JobTrackerStateHistory { State = arguments.state, Timestamp = arguments.timestamp, TimeReceived = DateTimeOffset.Now });

            // If this update shows the job has made some progress, we will mark it as the current state and update the job accordingly.
            if ((State == ExtendedJobState.Submitted && arguments.state != ExtendedJobState.Submitted) ||
                (State == ExtendedJobState.Processing && arguments.state != ExtendedJobState.Processing))
            {
                if (arguments.timestamp > LastTimeSeenJobProgress)
                {
                    LastTimeSeenJobProgress = arguments.timestamp;
                    await UpdateCurrentState(arguments.state);
                }
            }

            // If the AMS job is still processing, make sure we schedule the next currency check.
            if (State == ExtendedJobState.Processing)
            {
                ScheduleNextJobStateCurrencyCheck();
            }
        }

        private void ScheduleNextJobStateCurrencyCheck()
        {
            if (State == ExtendedJobState.Succeeded || State == ExtendedJobState.Failed || State == ExtendedJobState.TimedOut)
            {
                return;
            }

            var trackerCurrencyTimeoutTimeUtc = DateTime.UtcNow.Add(_settings.JobTrackerCurrencyCheckInterval);
            Entity.Current.SignalEntity(
                Entity.Current.EntityId,
                trackerCurrencyTimeoutTimeUtc,
                nameof(CheckIfJobStateIsCurrent)); // HACK: this is not using the SignalEntity<T> overload due to this bug: https://github.com/Azure/azure-functions-durable-extension/issues/1282

            _log.LogDebug("Scheduled tracker to check if the job state is current. CheckTime={CheckTime}",
                trackerCurrencyTimeoutTimeUtc);
        }

        public async Task CheckIfJobStateIsCurrent()
        {
            _log.LogDebug("Checking whether job tracker has a current job state. LastTimeSeenStateUpdate={LastTimeSeenStateUpdate}",
                LastTimeSeenStateUpdate);

            if (State == ExtendedJobState.Succeeded || State == ExtendedJobState.Failed || State == ExtendedJobState.TimedOut)
            {
                // We don't expect state updates if the job isn't actively processing.
                _log.LogDebug("Tracker is no longer in 'processing' state, so no further state updates are needed. LastTimeSeenJobProgress={LastTimeSeenJobProgress}",
                    LastTimeSeenJobProgress);
                return;
            }

            if (LastTimeSeenStateUpdate < DateTime.UtcNow.Subtract(_settings.JobTrackerCurrencyThreshold))
            {
                // We haven't seen any updates from this job recently, so we need to trigger a manual pull of the job state.
                _log.LogInformation("Job tracker state is not current. Pulling job state.");

                var retrievedTime = DateTimeOffset.UtcNow;
                var jobCurrentState = await _mediaServicesJobService.GetJobState(
                    AmsInstanceConfiguration.MediaServicesSubscriptionId, AmsInstanceConfiguration.MediaServicesResourceGroupName, AmsInstanceConfiguration.MediaServicesInstanceName,
                    JobTrackerEntityId);

                // We then send the updates back to the relevant entities through the standard job update notification process.
                Entity.Current.SignalEntity<IJobTrackerEntity>(
                    Entity.Current.EntityId,
                    proxy => proxy.ReceiveStateUpdate((jobCurrentState.State, retrievedTime)));

                foreach (var outputState in jobCurrentState.OutputStates)
                {
                    Entity.Current.SignalEntity<IJobOutputTrackerEntity>(
                        new EntityId(nameof(JobOutputTrackerEntity), outputState.Label),
                        proxy => proxy.ReceiveStateUpdate((outputState.State, outputState.Progress, retrievedTime)));
                }
            }
            else
            {
                _log.LogDebug($"Job is still considered to be current and will not be considered to be overdue for job tracker updates until {LastTimeSeenStateUpdate.Value.Add(_settings.JobTrackerCurrencyThreshold)}.");
            }

            ScheduleNextJobStateCurrencyCheck();
        }

        private void ScheduleNextJobTimeoutCheck()
        {
            if (State == ExtendedJobState.Succeeded || State == ExtendedJobState.Failed || State == ExtendedJobState.TimedOut)
            {
                return;
            }

            var timeoutCheckTimeUtc = DateTime.UtcNow.Add(_settings.JobTrackerTimeoutCheckInterval);
            Entity.Current.SignalEntity(
                Entity.Current.EntityId,
                timeoutCheckTimeUtc,
                nameof(CheckIfJobHasTimedOut)); // HACK: this is not using the SignalEntity<T> overload due to this bug: https://github.com/Azure/azure-functions-durable-extension/issues/1282

            _log.LogDebug("Scheduled tracker to check if the job has timed out. CheckTime={CheckTime}",
                timeoutCheckTimeUtc);
        }

        public async Task CheckIfJobHasTimedOut()
        {
            _log.LogDebug("Checking whether job has timed out. LastTimeSeenJobProgress={LastTimeSeenJobProgress}",
                  LastTimeSeenJobProgress);

            if (State == ExtendedJobState.Succeeded || State == ExtendedJobState.Failed || State == ExtendedJobState.TimedOut)
            {
                // We don't expect state updates if the job isn't actively processing.
                _log.LogDebug("Tracker is no longer in 'processing' state, so no further state updates are needed.");
                return;
            }

            if (LastTimeSeenJobProgress < DateTime.UtcNow.Subtract(_settings.JobTrackerTimeoutThreshold))
            {
                // It has been too long since we have seen any progress being made on the job.
                // This means we consider the job to have timed out.
                _log.LogInformation("Job has not seen progress within the timeout threshold. Considering job to have timed out.");

                await UpdateCurrentState(ExtendedJobState.TimedOut);
            }
            else
            {
                _log.LogDebug($"Job has seen progress within the timeout threshold and will not be considered to be timed out until {LastTimeSeenStateUpdate.Value.Add(_settings.JobTrackerTimeoutThreshold)}.");
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
