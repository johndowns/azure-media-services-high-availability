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
        void Start(string inputMediaFileUrl);

        void MarkAttemptAsSucceeded(string jobRunAttemptId);

        void MarkAttemptAsCanceled(string jobRunAttemptId);

        void MarkAttemptAsFailed(string jobRunAttemptId);

        void MarkAttemptAsTimedOut(string jobRunAttemptId);
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Job : IJob
    {
        [JsonProperty("jobId")]
        public string JobId => Entity.Current.EntityKey;

        [JsonProperty("inputMediaFileUrl")]
        public string InputMediaFileUrl { get; set; }

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
            // Due to https://github.com/Azure/azure-functions-durable-extension/issues/1238, we have to account for the fact that
            // this constructor could be called with null arguments when the status is read by the ReadEntityStateAsync() method.
            this._settings = options?.Value;
            this._random = random;
            this._log = log;
        }

        [FunctionName(nameof(Job))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
            => ctx.DispatchAsync<Job>(log);

        public void Start(string inputMediaFileUrl)
        {
            // Initialise the job.
            InputMediaFileUrl = inputMediaFileUrl;
            _log.LogInformation("Started job. JobId={JobId}, InputMediaFileUrl={InputMediaFileUrl}", JobId, InputMediaFileUrl);
            Status = JobStatus.Received;

            // Start a new attempt.
            var attemptStarted = StartAttempt();
            if (! attemptStarted)
            {
                UpdateJobStatus(JobStatus.Failed);
            }
            else
            {
                Status = JobStatus.Processing;
            }
        }

        private void UpdateJobStatus(JobStatus newJobStatus) // TODO handle the fact that an attempt might have come back with a success
        {
            Status = newJobStatus;
            _log.LogInformation("Job has completed. JobId={JobId}, JobStatus={JobStatus}", JobId, Status);
        }

        public void MarkAttemptAsSucceeded(string jobRunAttemptId)
        {
            _log.LogInformation("Job run attempt has succeeded. JobId={JobId}, JobRunAttemptId={JobRunAttemptId}", JobId, jobRunAttemptId);
            UpdateJobStatus(JobStatus.Succeeded);
        }

        public void MarkAttemptAsCanceled(string jobRunAttemptId)
        {
            _log.LogInformation("Job run attempt has been canceled. JobId={JobId}, JobRunAttemptId={JobRunAttemptId}", JobId, jobRunAttemptId);

            var newAttemptStarted = StartAttempt();
            if (!newAttemptStarted)
            {
                UpdateJobStatus(JobStatus.Failed);
            }
        }

        public void MarkAttemptAsFailed(string jobRunAttemptId)
        {
            _log.LogInformation("Job run attempt has failed. JobId={JobId}, JobRunAttemptId={JobRunAttemptId}", JobId, jobRunAttemptId);

            var newAttemptStarted = StartAttempt();
            if (!newAttemptStarted)
            {
                UpdateJobStatus(JobStatus.Failed);
            }
        }

        public void MarkAttemptAsTimedOut(string jobRunAttemptId)
        {
            _log.LogInformation("Job run attempt has timed out. JobId={JobId}, JobRunAttemptId={JobRunAttemptId}", JobId, jobRunAttemptId);

            var newAttemptStarted = StartAttempt();
            if (!newAttemptStarted)
            {
                UpdateJobStatus(JobStatus.Failed);
            }
        }

        private bool StartAttempt()
        {
            var stampId = SelectStampIdForAttempt();
            if (stampId == null)
            {
                _log.LogWarning("Cannot start any further job attempts since no stamps are available. JobId={JobId}", JobId);
                return false;
            }

            // Start the attempt on the selected stamp.
            var attemptId = $"{JobId}|{Guid.NewGuid()}";
            var attemptEntityId = new EntityId(nameof(JobRunAttempt), attemptId);
            Attempts.Add((stampId, attemptId));
            Entity.Current.SignalEntity<IJobRunAttempt>(attemptEntityId, proxy => proxy.Start((InputMediaFileUrl, stampId)));
            _log.LogInformation("Requested job attempt to start. JobId={JobId}, JobAttemptId={JobAttemptId}, StampId={StampId}", JobId, attemptId, stampId);
            return true;
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
