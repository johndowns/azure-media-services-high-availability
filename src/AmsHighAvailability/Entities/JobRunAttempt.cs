using AmsHighAvailability.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace AmsHighAvailability.Entities
{
    public interface IJobRunAttempt
    {
        void Start((string inputData, string stampId) arguments);

        void StatusUpdate((string newStatus, DateTimeOffset statusTime) arguments);

        void StatusTimeout();
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
        public JobRunAttemptStatus Status { get; set; }

        [JsonProperty("lastStatusUpdateReceivedTime")]
        public DateTimeOffset? LastStatusUpdateReceivedTime { get; set; }

        [JsonIgnore]
        private readonly Configuration.Options _settings;

        public JobRunAttempt(IOptions<Configuration.Options> options)
        {
            this._settings = options.Value;
        }

        [FunctionName(nameof(JobRunAttempt))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx)
            => ctx.DispatchAsync<JobRunAttempt>();

        public void Start((string inputData, string stampId) arguments)
        {
            // Set up the internal metadata.
            JobRunAttemptId = Entity.Current.EntityKey;
            JobId = JobRunAttemptId.Split('|')[0];
            StampId = arguments.stampId;
            SubmittedTime = DateTime.UtcNow;
            CompletedTime = null;
            Status = JobRunAttemptStatus.Received;
            LastStatusUpdateReceivedTime = null;

            // Submit the job for processing.
            // TODO submit job to AMS
            Status = JobRunAttemptStatus.Processing;

            // Make a note to ourselves to check for a status update timeout.
            ScheduleStatusTimeout();
        }

        public void StatusUpdate((string newStatus, DateTimeOffset statusTime) arguments)
        {
            LastStatusUpdateReceivedTime = arguments.statusTime;

            switch (arguments.newStatus)
            {
                case "InProgress":
                    UpdateJobStatus(JobRunAttemptStatus.Processing);
                    ScheduleStatusTimeout();
                    break;
                case "Done":
                    UpdateJobStatus(JobRunAttemptStatus.Succeeded);
                    break;
                case "Failed":
                    UpdateJobStatus(JobRunAttemptStatus.Failed);
                    break;
            }
        }

        public void StatusTimeout()
        {
            if (Status != JobRunAttemptStatus.Processing)
            {
                // We don't need to time out if the job isn't actively processing.
                return;
            }

            if (LastStatusUpdateReceivedTime == null || LastStatusUpdateReceivedTime < DateTime.UtcNow.AddMinutes(-60))
            {
                // The last time we received a status update was more than 60 minutes ago.
                // This means we consider the job to have timed out.
                UpdateJobStatus(JobRunAttemptStatus.TimedOut);
            }
        }

        private void UpdateJobStatus(JobRunAttemptStatus newStatus)
        {
            Status = newStatus;

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

        private void ScheduleStatusTimeout()
        {
            var statusTimeoutTimeUtc = DateTime.UtcNow.AddMinutes(10); // TODO
            Entity.Current.SignalEntity<IJobRunAttempt>(Entity.Current.EntityId, statusTimeoutTimeUtc, proxy => proxy.StatusTimeout());
        }
    }
}
