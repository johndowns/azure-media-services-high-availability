using AmsHighAvailability.Models;
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
        void Start((string inputData, string stampId) arguments);

        void StatusUpdate((JobRunAttemptStatus newStatus, DateTimeOffset statusTime) arguments);

        void CheckForStatusTimeout();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class JobRunAttempt : IJobRunAttempt
    {
        [JsonProperty("jobId")]
        public string JobId { get; set; }

        [JsonProperty("jobRunAttemptId")]
        public string JobRunAttemptId { get; set; }

        [JsonProperty("stampId")]
        public string StampId { get; set; }

        [JsonProperty("submittedTime")]
        public DateTimeOffset SubmittedTime { get; set; }

        [JsonProperty("completedTime")]
        public DateTimeOffset? CompletedTime { get; set; }

        [JsonProperty("status")]
        public JobRunAttemptStatus CurrentStatus { get; set; }

        public HashSet<JobRunAttemptStatusHistory> StatusHistory { get; set; } = new HashSet<JobRunAttemptStatusHistory>();

        [JsonProperty("lastStatusUpdateReceivedTime")]
        public DateTimeOffset? LastStatusUpdateReceivedTime { get; set; }

        [JsonIgnore]
        private readonly Configuration.Options _settings;

        [JsonIgnore]
        private readonly ILogger _log;

        public JobRunAttempt(ILogger log, IOptions<Configuration.Options> options)
        {
            this._settings = options.Value;
            this._log = log;
        }

        [FunctionName(nameof(JobRunAttempt))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
            => ctx.DispatchAsync<JobRunAttempt>(log);

        public void Start((string inputData, string stampId) arguments)
        {
            // Set up the internal metadata.
            JobRunAttemptId = Entity.Current.EntityKey;
            JobId = JobRunAttemptId.Split('|')[0];
            StampId = arguments.stampId;
            SubmittedTime = DateTime.UtcNow;
            CompletedTime = null;
            CurrentStatus = JobRunAttemptStatus.Received;
            LastStatusUpdateReceivedTime = null;

            // Submit the job for processing.
            // TODO submit job to AMS
            CurrentStatus = JobRunAttemptStatus.Processing;
            _log.LogInformation("Started job run attempt. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, StampId={StampId}", JobRunAttemptId, JobId, StampId);

            // Make a note to ourselves to check for a status update timeout.
            ScheduleNextStatusTimeoutCheck();
        }

        public void StatusUpdate((JobRunAttemptStatus newStatus, DateTimeOffset statusTime) arguments)
        {
            _log.LogInformation("Received status update for job run attempt. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, Time={StatusTime}, JobRunAttemptStatus={JobRunAttemptStatus}", JobRunAttemptId, JobId, arguments.statusTime, arguments.newStatus);

            LastStatusUpdateReceivedTime = arguments.statusTime;
            CurrentStatus = arguments.newStatus;
            StatusHistory.Add(new JobRunAttemptStatusHistory { Status = arguments.newStatus, Timestamp = arguments.statusTime });

            if (CurrentStatus == JobRunAttemptStatus.Processing)
            {
                ScheduleNextStatusTimeoutCheck();
            }

            UpdateJobStatus(CurrentStatus);
        }

        public void CheckForStatusTimeout()
        {
            _log.LogInformation("Checking for status timeout on job run attempt. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, LastStatusUpdateReceivedTime={lastStatusUpdateReceivedTime}", JobRunAttemptId, JobId, LastStatusUpdateReceivedTime);

            if (CurrentStatus != JobRunAttemptStatus.Processing)
            {
                // We don't need to time out if the job isn't actively processing.
                return;
            }

            if (LastStatusUpdateReceivedTime == null || LastStatusUpdateReceivedTime < DateTime.UtcNow.Subtract(_settings.JobRunAttemptTimeoutThreshold))
            {
                // The last time we received a status update was more than 60 minutes ago.
                // This means we consider the job to have timed out.
                UpdateJobStatus(JobRunAttemptStatus.TimedOut);
            }
        }

        private void UpdateJobStatus(JobRunAttemptStatus newStatus)
        {
            _log.LogInformation("Job run attempt is updating job status. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, NewStatus={newStatus}", JobRunAttemptId, JobId, newStatus);

            // Signal the job that we have an update.
            switch (newStatus)
            {
                case JobRunAttemptStatus.TimedOut:
                    {
                        Entity.Current.SignalEntity<IJob>(new EntityId(nameof(Job), JobId), proxy => proxy.MarkAttemptAsTimedOut(JobRunAttemptId));
                        break;
                    }

                case JobRunAttemptStatus.Succeeded:
                    {
                        Entity.Current.SignalEntity<IJob>(new EntityId(nameof(Job), JobId), proxy => proxy.MarkAttemptAsSucceeded(JobRunAttemptId));
                        break;
                    }

                case JobRunAttemptStatus.Failed:
                    {
                        Entity.Current.SignalEntity<IJob>(new EntityId(nameof(Job), JobId), proxy => proxy.MarkAttemptAsFailed(JobRunAttemptId));
                        break;
                    }
            }
        }

        private void ScheduleNextStatusTimeoutCheck()
        {
            var statusTimeoutTimeUtc = DateTime.UtcNow.Add(_settings.JobRunAttemptStatusTimeoutCheckInterval);
            Entity.Current.SignalEntity<IJobRunAttempt>(Entity.Current.EntityId, statusTimeoutTimeUtc, proxy => proxy.CheckForStatusTimeout());

            _log.LogInformation("Scheduled job run attempt for a status timeout check. JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, CheckTime={CheckTime}", JobRunAttemptId, JobId, statusTimeoutTimeUtc);
        }
    }
}
