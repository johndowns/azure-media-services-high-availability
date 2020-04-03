using AmsHighAvailability.Models;
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
        public string InputData { get; set; } // URL or whatever is needed (TODO)

        [JsonProperty("submittedTime")]
        public DateTimeOffset SubmittedTime { get; set; }

        [JsonProperty("completedTime")]
        public DateTimeOffset? CompletedTime { get; set; }

        [JsonProperty("status")]
        public JobStatus Status { get; set; }

        [JsonProperty("attempts")]
        public HashSet<(string stampId, string attemptId)> Attempts { get; set; } = new HashSet<(string stampId, string attemptId)>();

        [JsonIgnore]
        private readonly Configuration.Options _settings;

        [JsonIgnore]
        private readonly Random _random;

        [JsonIgnore]
        private readonly ILogger _log;

        public Job(ILogger log, IOptions<Configuration.Options> options, Random random)
        {
            this._settings = options.Value;
            this._random = random;
            this._log = log;
        }

        [FunctionName(nameof(Job))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
            => ctx.DispatchAsync<Job>(log);

        public void Start(string inputData)
        {
            JobId = Entity.Current.EntityKey;
            InputData = inputData;

            _log.LogInformation("Started job. JobId={JobId}", JobId);

            // Start a new attempt.
            StartAttempt();
        }

        public void MarkAttemptAsSucceeded(string jobRunAttemptId)
        {
            // TODO stop.
        }

        public void MarkAttemptAsCanceled(string jobRunAttemptId)
        {
            // TODO start a new attempt on a different stamp.
        }

        public void MarkAttemptAsFailed(string jobRunAttemptId)
        {
            // TODO start a new attempt on a different stamp.
        }

        public void MarkAttemptAsTimedOut(string jobRunAttemptId)
        {
            // TODO start a new attempt on a different stamp.
        }

        private void StartAttempt()
        {
            var stampId = SelectStampIdForAttempt();
            if (stampId == null)
            {
                _log.LogWarning("Cannot start any further job attempts since no stamps are available. JobId={JobId}", JobId);
                // TODO consider the job to have failed.
            }

            // Start the attempt on the selected stamp.
            var attemptId = $"{JobId}|{Guid.NewGuid()}";
            var attemptEntityId = new EntityId(nameof(JobRunAttempt), attemptId);
            Attempts.Add((stampId, attemptId));
            Entity.Current.SignalEntity<IJobRunAttempt>(attemptEntityId, proxy => proxy.Start((InputData, stampId)));
            _log.LogWarning("Requested job attempt to start. JobId={JobId}, JobAttemptId={JobAttemptId}, StampId={StampId}", JobId, attemptId, stampId);
        }

        private string SelectStampIdForAttempt()
        {
            // If we are using regional affinity then prefer using the home region if possible.
            if (_settings.StampRoutingMethod == StampRoutingMethod.RegionalAffinity)
            {
                if (!Attempts.Any(a => a.stampId == _settings.HomeStampId))
                {
                    return _settings.HomeStampId;
                }
            }

            // Otherwise, select a random stamp that has not already been used.
            var allRemainingStamps = _settings.AlllStampIdsArray.Except(Attempts.Select(a => a.stampId));
            if (allRemainingStamps.Any())
            {
                return allRemainingStamps
                    .Skip(_random.Next(allRemainingStamps.Count()))
                    .Take(1).Single();
            }

            // There are no more stamps available, so we can't start this attempt.
            return null;
        }
    }
}
