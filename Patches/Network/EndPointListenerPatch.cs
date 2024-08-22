using HarmonyLib;
using System.Net;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Reflection;

namespace DvMod.RemoteDispatch.Patches.Network;

// Harmony patch for the EndPointListener class
[HarmonyPatch(typeof(EndPointListener))]
public static class EndPointListenerPatch
{
    // Number of calls to String.op_Inequality() to skip
    const int TARGET_CALL = 1;

    // Method info for the String.op_Inequality()
    static MethodInfo opInequalityMethod = AccessTools.Method(typeof(string), "op_Inequality");

    // Harmony transpiler patch for the "SearchListener" method
    [HarmonyPatch("SearchListener")]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int call_Ctr = 0;
        bool found = false;
        bool complete = false;

        foreach (CodeInstruction instruction in instructions)
        {
            // Uncomment for debugging
            // Debug.Log($"Transpiling: {instruction.ToString()} - {call_Ctr}, {found}, {complete}");

            // Check if the current instruction is a call to String.op_Inequality()
            if (instruction.opcode == OpCodes.Call && instruction.operand as MethodInfo == opInequalityMethod)
            {
                // Have we found target call to patch
                if (!found)
                {
                    call_Ctr++;
                    found = call_Ctr == TARGET_CALL;
                }
                // Have we patched the target
                else if (!complete)
                {
                    complete = true;
                    // Replace the original call with the custom method
                    yield return CodeInstruction.Call(typeof(EndPointListenerPatch), "InverseCompareHost", [typeof(string), typeof(string)]);
                    continue;
                }
            }
            yield return instruction;
        }
    }

    // Custom method to compare hosts
    public static bool InverseCompareHost(string host1, string host2)
    {
        // If hosts are equal, return false
        if (host1 == host2)
            return false;

        // Ckeck if host1 is a valid IP address and is either IPv6Any ("[::]") or Any ("0.0.0.0")
        if (IPAddress.TryParse(host1, out IPAddress ip) && (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Any)))
            return false;

        return true;
    }
}