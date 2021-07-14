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

        private static Dictionary<TrainCar, string> InitializeJobIdForCar()
        {
            var dict = new Dictionary<TrainCar, string>();
            foreach (var (job, cars) in JobChainController.jobToJobCars)
                foreach (var car in cars)
                    dict[car] = job.ID;
            Main.DebugLog(() => $"Initializing jobIdForCar: {string.Join(",", dict)}");
            return dict;
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
                        if (jobId == string.Empty)
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