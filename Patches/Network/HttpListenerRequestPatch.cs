using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection.Emit;
using UnityEngine;



namespace DvMod.RemoteDispatch.Patches.Network;

[HarmonyPatch(typeof(HttpListenerRequest))]
public static class HttpListenerRequestPatch
{
    const int TARGET_LDLOC_0 = 5;
    const int TARGET_SKIPS = 9;

    [HarmonyPatch("FinishInitialization")]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int ldloc0_Ctr = 0;
        bool found = false;
        bool complete = false;
        int skipCtr = 0;

        foreach (CodeInstruction instruction in instructions)
        {
            //Debug.Log($"Transpiling: {instruction.ToString()} - {ldloc0_Ctr}, {found}, {complete}, {skipCtr}");
            if (instruction.opcode == OpCodes.Ldloc_0 && !found)
            {
                ldloc0_Ctr++;
                found = ldloc0_Ctr == TARGET_LDLOC_0;
            }
            else if (found && !complete)
            {
                complete = true;
                yield return CodeInstruction.Call(typeof(HttpListenerRequestPatch), "ExtractIpv6HostName", [typeof(string)]);
                continue;
            }
            else if (complete && skipCtr < TARGET_SKIPS)
            {
                skipCtr++;
                yield return new CodeInstruction(OpCodes.Nop);
                continue;
            }
            yield return instruction;
        }
    }

    //Extract IPv6 from between square braces.
    public static string ExtractIpv6HostName(string HostName)
    {
        if (HostName.StartsWith("["))
        {
            int endofaddress = HostName.IndexOf("]");

            if (endofaddress >= 3) // shortest IPv6 address [::]
            {
                HostName = HostName.Substring(0, endofaddress + 1);
                //Debug.Log($"ExtractIpv6HostName: {HostName}");
                return HostName;
            }
        }

        int num = HostName.IndexOf(':');
        if (num >= 0)
        {
            return HostName.Substring(0, num);
        }
        return HostName;
    }
}
