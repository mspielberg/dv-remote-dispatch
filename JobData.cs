using DV.Logic.Job;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public static class JobData
    {
        private static readonly Dictionary<TrainCar, string> jobIdForCar = InitializeJobIdForCar();
        private static Dictionary<string, Job> jobForId = new Dictionary<string, Job>();

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
            return JobChainController.jobToJobCars
                .SelectMany(kvp => kvp.Value.Select(car => (car, job: kvp.Key)))
                .ToDictionary(p => p.car, p => p.job.ID);
        }

        public static Job JobForId(string jobId)
        {
            if (jobForId.TryGetValue(jobId, out var job))
                return job;
            jobForId = JobChainController.jobToJobCars.Keys.ToDictionary(job => job.ID);
            jobForId.TryGetValue(jobId, out job);
            return job;
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
                        CarUpdater.MarkCarAsDirty(car);
                    }
                }
            }
        }
    }
}