using AmsHighAvailability.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AmsHighAvailability.Entities
{
    public interface IJobRunAttemptOutput
    {
        void Start();
        void StatusUpdate((AmsStatus newStatus, int progress, DateTimeOffset statusTime) arguments);
    }

    public class JobRunAttemptOutput
    {
        [JsonProperty("jobId")]
        public string JobId { get; set; }

        [JsonProperty("jobRunAttemptId")]
        public string JobRunAttemptId { get; set; }

        [JsonProperty("jobRunAttemptOutputId")]
        public string JobRunAttemptOutputId { get; set; }

        [JsonProperty("submittedTime")]
        public DateTimeOffset SubmittedTime { get; set; }

        [JsonProperty("completedTime")]
        public DateTimeOffset? CompletedTime { get; set; }

        [JsonProperty("status")]
        public AmsStatus CurrentStatus { get; set; }

        [JsonProperty("currentProgress")]
        public int CurrentProgress { get; set; }

        public HashSet<JobRunAttemptOutputStatusHistory> StatusHistory { get; set; } = new HashSet<JobRunAttemptOutputStatusHistory>();

        [JsonProperty("lastStatusUpdateReceivedTime")]
        public DateTimeOffset? LastStatusUpdateReceivedTime { get; set; }

        [JsonIgnore]
        private readonly ILogger _log;

        public JobRunAttemptOutput(ILogger log)
        {
            this._log = log;
        }

        [FunctionName(nameof(JobRunAttemptOutput))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
            => ctx.DispatchAsync<JobRunAttemptOutput>(log);

        public void Start()
        {
            // Set up the internal metadata.
            JobRunAttemptOutputId = Entity.Current.EntityKey;
            JobRunAttemptId = JobRunAttemptId.Split('|')[1];
            JobId = JobRunAttemptId.Split('|')[0];
            SubmittedTime = DateTime.UtcNow;
            CompletedTime = null;
            CurrentStatus = AmsStatus.Received;
            CurrentProgress = 0;
            LastStatusUpdateReceivedTime = null;
        }

        public void StatusUpdate((AmsStatus newStatus, int progress, DateTimeOffset statusTime) arguments)
        {
            _log.LogInformation("Received status update for job run attempt output. JobRunAttemptOutputId={JobRunAttemptOutputId}, JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, Time={StatusTime}, JobRunAttemptOutputStatus={JobRunAttemptOutputStatus}", JobRunAttemptOutputId, JobRunAttemptId, JobId, arguments.statusTime, arguments.newStatus);
            StatusHistory.Add(new JobRunAttemptOutputStatusHistory { StatusTime = arguments.statusTime, Progress = arguments.progress, TimeReceived = DateTimeOffset.Now });

            // TODO
            // If seeing forward progress, update JRA
        }
    }
}
