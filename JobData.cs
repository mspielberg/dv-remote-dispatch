using DV.Logic.Job;
using DV.ThingTypes.TransitionHelpers;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System;

namespace DvMod.RemoteDispatch
{
    public static class JobData
    {
        private static readonly Dictionary<TrainCar, string> jobIdForCar = InitializeJobIdForCar();
        private static Dictionary<string, Job> jobForId = new Dictionary<string, Job>();

        private const JobLicenses LicensesToExport =
          JobLicenses.Hazmat1 | JobLicenses.Hazmat2 | JobLicenses.Hazmat3 |
          JobLicenses.Military1 | JobLicenses.Military2 | JobLicenses.Military3 |
          JobLicenses.TrainLength1 | JobLicenses.TrainLength2;

        public static string? JobIdForCar(TrainCar car)
        {
            jobIdForCar.TryGetValue(car, out var jobId);
            return jobId;
        }

        public static Job? JobForCar(TrainCar car)
        {
            var jobId = JobIdForCar(car);
            if (jobId == null)
                return null;
            return JobForId(jobId);
        }

        private static Dictionary<TrainCar, string> InitializeJobIdForCar()
        {
            return SingletonBehaviour<JobsManager>.Instance.jobToJobCars
                .SelectMany(kvp => kvp.Value.Select(car => (car, job: kvp.Key)))
                .ToDictionary(p => p.car, p => p.job.ID);
        }

        public static Job? JobForId(string jobId)
        {
            if (jobForId.TryGetValue(jobId, out var job))
                return job;
            jobForId = SingletonBehaviour<JobsManager>.Instance.jobToJobCars.Keys.ToDictionary(job => job.ID);
            jobForId.TryGetValue(jobId, out job);
            return job;
        }

        public static Dictionary<string, JObject> GetAllJobData()
        {
            static IEnumerable<TaskData> FlattenToTransport(TaskData data)
            {
                if (data.type == TaskType.Transport)
                {
                    yield return data;
                }
                else if (data.nestedTasks != null)
                {
                    foreach (var nested in data.nestedTasks)
                    {
                        foreach (var task in FlattenToTransport(nested.GetTaskData()))
                            yield return task;
                    }
                }
            }
            static IEnumerable<TaskData> FlattenMany(IEnumerable<TaskData> data) => data.SelectMany(FlattenToTransport);
            static JObject TaskToJson(TaskData data) => new JObject(
                new JProperty("startTrack", data.startTrack?.ID?.FullDisplayID),
                new JProperty("destinationTrack", data.destinationTrack?.ID?.FullDisplayID),
                new JProperty("cars", (data.cars ?? new List<Car>()).Select(car => car.ID))
            );
            static JArray RequiredLicenses(Job job) => JArray.FromObject(
                Enum.GetValues(typeof(JobLicenses))
                    .OfType<JobLicenses>()
                    .Where(v => (job.requiredLicenses & LicensesToExport & v) != JobLicenses.Basic)
                    .Select(v => Enum.GetName(typeof(JobLicenses), v))
            );
            static float TotalLength(TaskData task) => task.cars.Sum(car => car.length);
            static float TotalMass(TaskData task) => task.cars.Sum(car => car.carType.parentType.mass)
                + ((task.cargoTypePerCar == null)
                ? 0f
                : task.cars.Zip(task.cargoTypePerCar, (car, cargoType) => car.capacity * cargoType.ToV2().massPerUnit).Sum());

            static JObject JobToJson(Job job)
            {
                var flattenedTasks = FlattenMany(job.tasks.Select(task => task.GetTaskData())).ToArray();
                var mainTask = job.jobType == JobType.ShuntingLoad ? flattenedTasks.Last() : flattenedTasks.First();
                var length = TotalLength(mainTask);
                return new JObject(
                    new JProperty("originYardId", job.chainData.chainOriginYardId),
                    new JProperty("destinationYardId", job.chainData.chainDestinationYardId),
                    new JProperty("tasks", flattenedTasks.Select(TaskToJson)),
                    new JProperty("requiredLicenses", RequiredLicenses(job)),
                    new JProperty("length", TotalLength(mainTask)),
                    new JProperty("mass", TotalMass(mainTask) / 1000),
                    new JProperty("basePayment", job.GetBasePaymentForTheJob()),
                    new JProperty("isActive", job.State == JobState.InProgress));
            }

            // ensure cache is updated
            JobForId("");
            return jobForId.ToDictionary(
                kvp => kvp.Key,
                kvp => JobToJson(kvp.Value));
        }

        public static string GetAllJobDataJson()
        {
            return JsonConvert.SerializeObject(GetAllJobData());
        }

        public static class JobPatches
        {
            [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.UpdateTrainCarPlatesOfCarsOnJob))]
            public static class UpdateTrainCarPlatesOfCarsOnJobPatch
            {
                public static void Postfix(JobChainController __instance, string jobId)
                {
                    foreach (TrainCar car in __instance.trainCarsForJobChain)
                    {
                        if (jobId.Length == 0)
                            jobIdForCar.Remove(car);
                        else
                            jobIdForCar[car] = jobId;
                        Sessions.AddTag("jobs");
                    }
                }
            }
            [HarmonyPatch(typeof(Job))]
            public static class UpdateJobStatePatches
            {
                [HarmonyPostfix]
                [HarmonyPatch(nameof(Job.TakeJob))]
                public static void TakeJobPostfix(Job __instance, bool takenViaLoadGame)
                {
                    if (!takenViaLoadGame)
                    {
                        Sessions.AddTag("jobs");
                    }
                }
            }
        }
    }
}