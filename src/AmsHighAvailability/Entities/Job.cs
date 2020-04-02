using AmsHighAvailability.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AmsHighAvailability.Entities
{
    public interface IJob
    {
        void Start(string inputData);

        void MarkAttemptAsSucceeded(string jobRunAttemptId);

        void MarkAttemptAsCanceled(string jobRunAttemptId);

        void MarkAttemptAsFailed(string jobRunAttemptId);

        void MarkAttemptAsTimedOut(string jobRunAttemptId);
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Job : IJob
    {
        [JsonProperty("jobId")]
        public string JobId { get; set; }

        [JsonProperty("inputData")]
        public string InputData { get; set; } // URL or whatever is needed

        [JsonProperty("submittedTime")]
        public DateTimeOffset SubmittedTime { get; set; }

        [JsonProperty("completedTime")]
        public DateTimeOffset? CompletedTime { get; set; }

        [JsonProperty("status")]
        public JobStatus Status { get; set; }

        [JsonProperty("attempts")]
        public HashSet<(string stampId, string attemptId)> Attempts { get; set; } = new HashSet<(string stampId, string attemptId)>();

        [FunctionName(nameof(Job))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx)
            => ctx.DispatchAsync<Job>();

        public void Start(string inputData)
        {
            JobId = Entity.Current.EntityKey;
            InputData = inputData;

            // Start a new attempt.
            StartAttempt();
        }

        public void MarkAttemptAsSucceeded(string jobRunAttemptId)
        {
        }

        public void MarkAttemptAsCanceled(string jobRunAttemptId)
        {
        }

        public void MarkAttemptAsFailed(string jobRunAttemptId)
        {
        }

        public void MarkAttemptAsTimedOut(string jobRunAttemptId)
        {
            // TODO start a new attempt on a different stamp.
            //StartAttempt();
        }

        private void StartAttempt()
        {
            // Implement logic for round-robin and for regional affinity (TODO).
            var stampId = "TODO-stamp";

            // Start the attempt.
            var attemptId = $"{JobId}|{Guid.NewGuid()}";
            var attemptEntityId = new EntityId(nameof(JobRunAttempt), attemptId);
            Entity.Current.SignalEntity<IJobRunAttempt>(attemptEntityId, proxy => proxy.Start((InputData, stampId)));

            Attempts.Add((stampId, attemptId));
        }
    }
}
