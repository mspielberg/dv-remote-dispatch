using DV.Logic.Job;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public static class LicenseData
    {
        // LicensesToJson converts the JobLicenses enum into a json array of the enum's names.
        public static JObject GetLicenseData()
        {
            int license = (int)LicenseManager.GetAcquiredJobLicenses();
            Type enumType = typeof(JobLicenses);
            JObject result = new JObject();
            foreach (int val in Enum.GetValues(enumType))
            {
                if (val > 0)
                {
                    result.Add(Enum.GetName(enumType, val), (license & val) > 0);
                }
            }
            return result;
        }

        public static string GetLicenseDataJson()
        {
            return JsonConvert.SerializeObject(GetLicenseData());
        }

        public static class LicensePatches
        {
            [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.AcquireJobLicense))]
            public static class AcquireJobLicensePatch
            {
                public static void Postfix(JobLicenses newLicense)
                {
                    Sessions.AddTag("licenses");
                }
            }
        }
    }
}