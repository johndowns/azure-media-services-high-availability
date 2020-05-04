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
        void ReceiveStateUpdate((ExtendedJobState state, int progress, DateTimeOffset timestamp) arguments);
    }

    public class JobOutputTrackerEntity : IJobOutputTrackerEntity
    {
        [JsonProperty("jobCoordinatorEntityId")]
        public string JobCoordinatorEntityId => Entity.Current.EntityKey.Split('|')[0];

        [JsonProperty("jobTrackerEntityId")]
        public string JobTrackerEntityId => Entity.Current.EntityKey.Split('|')[1];

        [JsonProperty("jobOutputTrackerEntityId")]
        public string JobOutputTrackerEntityId => Entity.Current.EntityKey;

        [JsonProperty("state")]
        public ExtendedJobState State { get; set; } = ExtendedJobState.Submitted;

        [JsonProperty("currentProgress")]
        public int CurrentProgress { get; set; } = 0;

        public HashSet<JobOutputTrackerStateHistory> StateHistory { get; set; } = new HashSet<JobOutputTrackerStateHistory>();

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

        public void ReceiveStateUpdate((ExtendedJobState state, int progress, DateTimeOffset timestamp) arguments)
        {
            _log.LogInformation("Received state update for job output tracker. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, JobOutputTrackerEntityId={JobOutputTrackerEntityId}, Timestamp={Timestamp}, JobOutputTrackerState={JobOutputTrackerState}, JobOutputTrackerProgress={JobOutputTrackerProgress}",
                JobCoordinatorEntityId, JobTrackerEntityId, JobOutputTrackerEntityId, arguments.timestamp, arguments.state, arguments.progress);
            StateHistory.Add(new JobOutputTrackerStateHistory { Timestamp = arguments.timestamp, Progress = arguments.progress, TimeReceived = DateTimeOffset.Now });

            // If we see the state move from 'Received' to something else, or from 'Processing'
            // to something else, or we see the progress increase, then we count this as progress.
            if ((State == ExtendedJobState.Submitted && arguments.state != ExtendedJobState.Submitted) ||
                (State == ExtendedJobState.Processing && arguments.state != ExtendedJobState.Processing) ||
                (arguments.progress > CurrentProgress))
            {
                if (LastTimeSeenJobOutputProgress == null ||
                    arguments.timestamp > LastTimeSeenJobOutputProgress)
                {
                    // Update the current state information.
                    State = arguments.state;
                    CurrentProgress = arguments.progress;
                    LastTimeSeenJobOutputProgress = arguments.timestamp;

                    // Signal the tracker that we have seen a progress update.
                    _log.LogDebug("Updating job tracker state from output tracker's state change. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, JobOutputTrackerEntityId={JobOutputTrackerEntityId}, Timestamp={Timestamp}, JobOutputTrackerState={JobOutputTrackerState}, JobOutputTrackerProgress={JobOutputTrackerProgress}",
                        JobCoordinatorEntityId, JobTrackerEntityId, JobOutputTrackerEntityId, arguments.timestamp, arguments.state, arguments.progress);
                    var entityId = new EntityId(nameof(JobTrackerEntity), JobTrackerEntityId);
                    Entity.Current.SignalEntity<IJobTrackerEntity>(
                        entityId,
                        proxy => proxy.ReceiveOutputStateUpdate(arguments.timestamp));
                }
            }
        }
    }
}
