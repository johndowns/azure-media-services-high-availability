﻿using AmsHighAvailability.Models;
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
    public interface IJobCoordinatorEntity
    {
        void Start(string inputMediaFileUrl);

        void MarkTrackerAsSucceeded(string jobTrackerEntityId);
                 
        void MarkTrackerAsCanceled(string jobTrackerEntityId);
                 
        void MarkTrackerAsFailed(string jobTrackerEntityId);
                 
        void MarkTrackerAsTimedOut(string jobTrackerEntityId);
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class JobCoordinatorEntity : IJobCoordinatorEntity
    {
        [JsonProperty("jobCoordinatorEntityId")]
        public string JobCoordinatorEntityId => Entity.Current.EntityKey;

        [JsonProperty("inputMediaFileUrl")]
        public string InputMediaFileUrl { get; set; }

        [JsonProperty("submittedTime")]
        public DateTimeOffset SubmittedTime { get; set; }

        [JsonProperty("completedTime")]
        public DateTimeOffset? CompletedTime { get; set; }

        [JsonProperty("status")]
        public JobStatus Status { get; set; }

        [JsonProperty("trackers")]
        public HashSet<(string amsInstanceId, string trackerId)> Trackers { get; set; } = new HashSet<(string amsInstanceId, string trackerId)>();

        [JsonIgnore]
        private readonly Configuration.Options _settings;

        [JsonIgnore]
        private readonly Random _random;

        [JsonIgnore]
        private readonly ILogger _log;

        public JobCoordinatorEntity(ILogger log, IOptions<Configuration.Options> options, Random random)
        {
            // Due to https://github.com/Azure/azure-functions-durable-extension/issues/1238, we have to account for the fact that
            // this constructor could be called with null arguments when the status is read by the ReadEntityStateAsync() method.
            this._settings = options?.Value;
            this._random = random;
            this._log = log;
        }

        [FunctionName(nameof(JobCoordinatorEntity))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx, ILogger log)
            => ctx.DispatchAsync<JobCoordinatorEntity>(log);

        public void Start(string inputMediaFileUrl)
        {
            // Initialise the coordinator.
            InputMediaFileUrl = inputMediaFileUrl;
            _log.LogInformation("Started job coordinator. JobCoordinatorEntityId={JobCoordinatorEntityId}, InputMediaFileUrl={InputMediaFileUrl}",
                JobCoordinatorEntityId, InputMediaFileUrl);
            Status = JobStatus.Received;

            // Start a new tracker.
            var trackerStarted = StartTracker();
            if (!trackerStarted)
            {
                UpdateStatus(JobStatus.Failed);
            }
            else
            {
                Status = JobStatus.Processing;
            }
        }

        private void UpdateStatus(JobStatus newJobStatus)
        {
            Status = newJobStatus;
            _log.LogInformation("Job coordinator has completed. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobStatus={JobStatus}",
                JobCoordinatorEntityId, Status);
        }

        public void MarkTrackerAsSucceeded(string jobTrackerEntityId)
        {
            _log.LogInformation("Job tracker has succeeded. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={jobTrackerEntityId}",
                JobCoordinatorEntityId, jobTrackerEntityId);
            UpdateStatus(JobStatus.Succeeded);
        }

        public void MarkTrackerAsCanceled(string jobTrackerEntityId)
        {
            _log.LogInformation("Job tracker has been canceled. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={jobTrackerEntityId}",
                JobCoordinatorEntityId, jobTrackerEntityId);

            var newTrackerStarted = StartTracker();
            if (!newTrackerStarted)
            {
                UpdateStatus(JobStatus.Failed);
            }
        }

        public void MarkTrackerAsFailed(string jobTrackerEntityId)
        {
            _log.LogInformation("Job tracker has failed. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={jobTrackerEntityId}",
                JobCoordinatorEntityId, jobTrackerEntityId);

            var newTrackerStarted = StartTracker();
            if (!newTrackerStarted)
            {
                UpdateStatus(JobStatus.Failed);
            }
        }

        public void MarkTrackerAsTimedOut(string jobTrackerEntityId)
        {
            _log.LogInformation("Job tracker has timed out. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={jobTrackerEntityId}",
                JobCoordinatorEntityId, jobTrackerEntityId);

            var newTrackerStarted = StartTracker();
            if (!newTrackerStarted)
            {
                UpdateStatus(JobStatus.Failed);
            }
        }

        private bool StartTracker()
        {
            var amsInstanceId = SelectAmsInstanceId();
            if (amsInstanceId == null)
            {
                _log.LogWarning("Cannot start any further trackers since no AMS instances are left to use. JobCoordinatorEntityId={JobCoordinatorEntityId}",
                    JobCoordinatorEntityId);
                return false;
            }

            // Start the tracker on the selected AMS instance.
            var trackerId = $"{JobCoordinatorEntityId}|{Guid.NewGuid()}";
            var trackerEntityId = new EntityId(nameof(JobTrackerEntity), trackerId);
            Trackers.Add((amsInstanceId, trackerId));
            Entity.Current.SignalEntity<IJobTracker>(trackerEntityId, proxy => proxy.Start((InputMediaFileUrl, amsInstanceId)));
            _log.LogInformation("Requested tracked job to start. JobCoordinatorEntityId={JobCoordinatorEntityId}, JobTrackerEntityId={JobTrackerEntityId}, AmsInstanceId={AmsInstanceId}",
                JobCoordinatorEntityId, trackerId, amsInstanceId);
            return true;
        }

        private string SelectAmsInstanceId()
        {
            // If we are using regional affinity then prefer using the primary region if possible.
            if (_settings.AmsInstanceRoutingMethod == AmsInstanceRoutingMethod.RegionalAffinity)
            {
                if (!Trackers.Any(a => a.amsInstanceId == _settings.PrimaryAmsInstanceId))
                {
                    return _settings.PrimaryAmsInstanceId;
                }
            }

            // Otherwise, select a random AMS instance that has not already been used.
            var allRemainingAmsInstances = _settings.AlllAmsInstanceIdsArray.Except(Trackers.Select(a => a.amsInstanceId));
            if (allRemainingAmsInstances.Any())
            {
                return allRemainingAmsInstances
                    .Skip(_random.Next(allRemainingAmsInstances.Count()))
                    .Take(1).Single();
            }

            // There are no more AMS instances available, so we can't start a new tracker.
            return null;
        }
    }
}