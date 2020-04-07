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
        void StatusUpdate((AmsStatus newStatus, int progress, DateTimeOffset statusTime) arguments);
    }

    public class JobRunAttemptOutput
    {
        [JsonProperty("jobId")]
        public string JobId => Entity.Current.EntityKey.Split('|')[0];

        [JsonProperty("jobRunAttemptId")]
        public string JobRunAttemptId => Entity.Current.EntityKey.Split('|')[1];

        [JsonProperty("jobRunAttemptOutputId")]
        public string JobRunAttemptOutputId => Entity.Current.EntityKey;

        [JsonProperty("status")]
        public AmsStatus CurrentStatus { get; set; } = AmsStatus.Received;

        [JsonProperty("currentProgress")]
        public int CurrentProgress { get; set; } = 0;

        public HashSet<JobRunAttemptOutputStatusHistory> StatusHistory { get; set; } = new HashSet<JobRunAttemptOutputStatusHistory>();

        [JsonProperty("lastStatusUpdateReceivedTime")]
        public DateTimeOffset? LastStatusUpdateReceivedTime { get; set; } = null;

        [JsonIgnore]
        private readonly ILogger _log;

        public JobRunAttemptOutput(ILogger log)
        {
            this._log = log;
        }

        [FunctionName(nameof(JobRunAttemptOutput))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
            => ctx.DispatchAsync<JobRunAttemptOutput>(log);

        public void StatusUpdate((AmsStatus newStatus, int progress, DateTimeOffset statusTime) arguments)
        {
            _log.LogInformation("Received status update for job run attempt output. JobRunAttemptOutputId={JobRunAttemptOutputId}, JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, Time={StatusTime}, JobRunAttemptOutputStatus={JobRunAttemptOutputStatus}, JobRunAttemptOutputProgress={JobRunAttemptOutputProgress}", JobRunAttemptOutputId, JobRunAttemptId, JobId, arguments.statusTime, arguments.newStatus, arguments.progress);
            StatusHistory.Add(new JobRunAttemptOutputStatusHistory { StatusTime = arguments.statusTime, Progress = arguments.progress, TimeReceived = DateTimeOffset.Now });

            // If we see the status move from 'Received' to something else, or from 'Processing' to something else, or we see the progress increase, then we count this as progress
            if ((CurrentStatus == AmsStatus.Received && arguments.newStatus != AmsStatus.Received) ||
                (CurrentStatus == AmsStatus.Processing && arguments.newStatus != AmsStatus.Processing) ||
                (arguments.progress > CurrentProgress))
            {
                // Signal the JobRunAttempt that we have seen a progress update on this attempt.
                _log.LogInformation("Updating job run attempt status from output status change. JobRunAttemptOutputId={JobRunAttemptOutputId}, JobRunAttemptId={JobRunAttemptId}, JobId={JobId}, Time={StatusTime}, JobRunAttemptOutputStatus={JobRunAttemptOutputStatus}, JobRunAttemptOutputProgress={JobRunAttemptOutputProgress}", JobRunAttemptOutputId, JobRunAttemptId, JobId, arguments.statusTime, arguments.newStatus, arguments.progress);
                var entityId = new EntityId(nameof(JobRunAttempt), JobRunAttemptId);
                Entity.Current.SignalEntity<IJobRunAttempt>(entityId, proxy => proxy.StatusUpdate((AmsStatus.Processing, arguments.statusTime)));
            }
        }
    }
}
