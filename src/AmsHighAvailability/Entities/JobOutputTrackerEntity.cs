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
    public interface IJobOutputTrackerEntity
    {
        void StatusUpdate((AmsStatus newStatus, int progress, DateTimeOffset statusTime) arguments);
    }

    public class JobOutputTrackerEntity : IJobOutputTrackerEntity
    {
        [JsonProperty("jobCoordinatorEntityId")]
        public string JobCoordinatorEntityId => Entity.Current.EntityKey.Split('|')[0];

        [JsonProperty("jobTrackerEntityId")]
        public string JobTrackerEntityId => Entity.Current.EntityKey.Split('|')[1];

        [JsonProperty("jobOutputTrackerEntityId")]
        public string JobOutputTrackerEntityId => Entity.Current.EntityKey;

        [JsonProperty("status")]
        public AmsStatus CurrentStatus { get; set; } = AmsStatus.Submitted;

        [JsonProperty("currentProgress")]
        public int CurrentProgress { get; set; } = 0;

        public HashSet<JobOutputTrackerStatusHistory> StatusHistory { get; set; } = new HashSet<JobOutputTrackerStatusHistory>();

        [JsonProperty("LastTimeSeenJobOutputProgress")]
        public DateTimeOffset? LastTimeSeenJobOutputProgress { get; set; } = null;

        [JsonIgnore]
        private readonly ILogger _log;

        public JobOutputTrackerEntity(ILogger log)
        {
            this._log = log;
        }

        [FunctionName(nameof(JobOutputTrackerEntity))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
            => ctx.DispatchAsync<JobOutputTrackerEntity>(log);

        public void StatusUpdate((AmsStatus newStatus, int progress, DateTimeOffset statusTime) arguments)
        {
            _log.LogInformation("Received status update for job output tracker. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, JobOutputTrackerEntityId={JobOutputTrackerEntityId}, Time={StatusTime}, JobOutputTrackerStatus={JobOutputTrackerStatus}, JobOutputTrackerProgress={JobOutputTrackerProgress}",
                JobCoordinatorEntityId, JobTrackerEntityId, JobOutputTrackerEntityId, arguments.statusTime, arguments.newStatus, arguments.progress);
            StatusHistory.Add(new JobOutputTrackerStatusHistory { StatusTime = arguments.statusTime, Progress = arguments.progress, TimeReceived = DateTimeOffset.Now });

            // If we see the status move from 'Received' to something else, or from 'Processing'
            // to something else, or we see the progress increase, then we count this as progress.
            if ((CurrentStatus == AmsStatus.Submitted && arguments.newStatus != AmsStatus.Submitted) ||
                (CurrentStatus == AmsStatus.Processing && arguments.newStatus != AmsStatus.Processing) ||
                (arguments.progress > CurrentProgress))
            {
                if (LastTimeSeenJobOutputProgress == null ||
                    arguments.statusTime > LastTimeSeenJobOutputProgress)
                {
                    // Update the current status information.
                    CurrentStatus = arguments.newStatus;
                    CurrentProgress = arguments.progress;
                    LastTimeSeenJobOutputProgress = arguments.statusTime;

                    // Signal the tracker that we have seen a progress update.
                    _log.LogInformation("Updating job tracker status from output tracker's status change. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, JobOutputTrackerEntityId={JobOutputTrackerEntityId}, Time={StatusTime}, JobOutputTrackerStatus={JobOutputTrackerStatus}, JobOutputTrackerProgress={JobOutputTrackerProgress}",
                        JobCoordinatorEntityId, JobTrackerEntityId, JobOutputTrackerEntityId, arguments.statusTime, arguments.newStatus, arguments.progress);
                    var entityId = new EntityId(nameof(JobTrackerEntity), JobTrackerEntityId);
                    Entity.Current.SignalEntity<IJobTracker>(entityId, proxy => proxy.ReceivePushOutputStatusUpdate(arguments.statusTime));
                }
            }
        }
    }
}
