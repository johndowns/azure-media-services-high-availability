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
    public interface IJobRunAttempt
    {
        Task Start((string inputMediaFileUrl, string stampId) arguments);

        void StatusUpdate((AmsStatus newStatus, DateTimeOffset statusTime) arguments);

        void CheckForStatusTimeout();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class JobRunAttempt : IJobRunAttempt
    {
        [JsonProperty("jobId")]
        public string JobId => JobRunAttemptId.Split('|')[0];

        [JsonProperty("jobRunAttemptId")]
        public string JobRunAttemptId => Entity.Current.EntityKey;

        [JsonProperty("stampId")]
        public string StampId { get; set; }

        [JsonProperty("submittedTime")]
        public DateTimeOffset SubmittedTime { get; set; }

        [JsonProperty("completedTime")]
        public DateTimeOffset? CompletedTime { get; set; } = null;

        private AmsStatus currentStatus = AmsStatus.Received;
        [JsonProperty("status")]
        public AmsStatus CurrentStatus {
            get
            {
                return currentStatus;
            }
            set
            {
                currentStatus = value;

                if (CurrentStatus == AmsStatus.Received || CurrentStatus == AmsStatus.Processing)
                {
                    return;
                }

                // Signal the job that we have an update.
                _log.LogInformation("Job run attempt is updating job status. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, Status={Status}", JobRunAttemptId, JobId, CurrentStatus);
                switch (CurrentStatus)
                {
                    case AmsStatus.TimedOut:
                        Entity.Current.SignalEntity<IJob>(new EntityId(nameof(Job), JobId), proxy => proxy.MarkAttemptAsTimedOut(JobRunAttemptId));
                        break;
                    case AmsStatus.Succeeded:
                        Entity.Current.SignalEntity<IJob>(new EntityId(nameof(Job), JobId), proxy => proxy.MarkAttemptAsSucceeded(JobRunAttemptId));
                        break;
                    case AmsStatus.Failed:
                        Entity.Current.SignalEntity<IJob>(new EntityId(nameof(Job), JobId), proxy => proxy.MarkAttemptAsFailed(JobRunAttemptId));
                        break;
                }
            }
        }

        public HashSet<JobRunAttemptStatusHistory> StatusHistory { get; set; } = new HashSet<JobRunAttemptStatusHistory>();

        [JsonProperty("lastStatusUpdateReceivedTime")]
        public DateTimeOffset? LastStatusUpdateReceivedTime { get; set; } = null;

        [JsonIgnore]
        private readonly IMediaServicesJobService _mediaServicesJobService;

        [JsonIgnore]
        private readonly Configuration.Options _settings;

        [JsonIgnore]
        private readonly ILogger _log;

        public JobRunAttempt(ILogger log, IMediaServicesJobService mediaServicesJobService, IOptions<Configuration.Options> options)
        {
            this._log = log;
            this._mediaServicesJobService = mediaServicesJobService;
            this._settings = options.Value;
        }

        [FunctionName(nameof(JobRunAttempt))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
            => ctx.DispatchAsync<JobRunAttempt>(log);

        public async Task Start((string inputMediaFileUrl, string stampId) arguments)
        {
            // Set up the internal metadata.
            StampId = arguments.stampId;
            SubmittedTime = DateTime.UtcNow;

            // Find the details of the stamp that will be used for this job run attempt.
            var stampSettings = _settings.GetStampConfiguration(StampId);

            // Submit the job for processing.
            var isSubmittedSuccessfully = await _mediaServicesJobService.SubmitJobToMediaServicesEndpointAsync(
                stampSettings.MediaServicesSubscriptionId, stampSettings.MediaServicesResourceGroupName, stampSettings.MediaServicesInstanceName,
                arguments.inputMediaFileUrl,
                JobRunAttemptId);

            // Update this entity's status.
            if (isSubmittedSuccessfully)
            {
                _log.LogInformation("Successfully submitted job run attempt to Azure Media Services. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, StampId={StampId}", JobRunAttemptId, JobId, StampId);
                CurrentStatus = AmsStatus.Processing;

                // Make a note to ourselves to check for a status update timeout.
                ScheduleNextStatusTimeoutCheck();
            }
            else
            {
                _log.LogInformation("Failed to start job run attempt. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, StampId={StampId}", JobRunAttemptId, JobId, StampId);
                CurrentStatus = AmsStatus.Failed;
            }
        }

        public void StatusUpdate((AmsStatus newStatus, DateTimeOffset statusTime) arguments)
        {
            _log.LogInformation("Received status update for job run attempt. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, Time={StatusTime}, JobRunAttemptStatus={JobRunAttemptStatus}", JobRunAttemptId, JobId, arguments.statusTime, arguments.newStatus);
            StatusHistory.Add(new JobRunAttemptStatusHistory { Status = arguments.newStatus, StatusTime = arguments.statusTime, TimeReceived = DateTimeOffset.Now });

            // If this status update shows forward progress, we will mark it as the current status and update the job accordingly.
            if (LastStatusUpdateReceivedTime == null || arguments.statusTime > LastStatusUpdateReceivedTime)
            {
                LastStatusUpdateReceivedTime = arguments.statusTime;
                CurrentStatus = arguments.newStatus;
            }

            // If the attempt is still processing, make sure we schedule a timeout check.
            if (CurrentStatus == AmsStatus.Processing)
            {
                ScheduleNextStatusTimeoutCheck();
            }
        }

        public void CheckForStatusTimeout()
        {
            _log.LogInformation("Checking for status timeout on job run attempt. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, LastStatusUpdateReceivedTime={lastStatusUpdateReceivedTime}", JobRunAttemptId, JobId, LastStatusUpdateReceivedTime);

            if (CurrentStatus != AmsStatus.Processing)
            {
                // We don't need to time out if the job isn't actively processing.
                _log.LogInformation("Attempt is no longer processing so no further status updates are needed. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, LastStatusUpdateReceivedTime={lastStatusUpdateReceivedTime}", JobRunAttemptId, JobId, LastStatusUpdateReceivedTime);
                return;
            }

            if (LastStatusUpdateReceivedTime == null || LastStatusUpdateReceivedTime < DateTime.UtcNow.Subtract(_settings.JobRunAttemptTimeoutThreshold))
            {
                // The last time we received a status update was more than 60 minutes ago.
                // This means we consider the job to have timed out.
                CurrentStatus = AmsStatus.TimedOut;
            }
        }

        private void ScheduleNextStatusTimeoutCheck()
        {
            if (CurrentStatus == AmsStatus.Succeeded || CurrentStatus== AmsStatus.Failed || CurrentStatus == AmsStatus.TimedOut)
            {
                _log.LogInformation("Skipped scheduling a new status check since attempt status is terminal. JobId={JobId}, JobRunAttemptId={JobRunAttemptId}, CurrentStatus={CurrentStatus}", JobId, JobRunAttemptId, CurrentStatus);
                return;
            }

            var statusTimeoutTimeUtc = DateTime.UtcNow.Add(_settings.JobRunAttemptStatusTimeoutCheckInterval);
            Entity.Current.SignalEntity<IJobRunAttempt>(Entity.Current.EntityId, statusTimeoutTimeUtc, proxy => proxy.CheckForStatusTimeout());

            _log.LogInformation("Scheduled job run attempt for a status timeout check. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, CheckTime={CheckTime}", JobRunAttemptId, JobId, statusTimeoutTimeUtc);
        }
    }
}
