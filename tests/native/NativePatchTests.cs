using DvMod.RemoteDispatch;
using HarmonyLib;
using Signals.Common.Aspects;
using Signals.Common;
using Signals.Game.Aspects;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using UnityEngine;

internal static class NativePatchTests
{
    private static bool? forced;
    private static int assertions;

    private static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Pass the game directory and DVSignals directory.");
        var directories = new[] { Path.Combine(args[0], "DerailValley_Data", "Managed"),
            Path.Combine(args[0], "DerailValley_Data", "Managed", "UnityModManager"), args[1] };
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) => {
            string file = new AssemblyName(request.Name).Name + ".dll";
            string? path = directories.Select(d => Path.Combine(d, file)).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        Run();
        return 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    private static bool Before(IAspect __instance, ref bool __result)
    {
        if (!forced.HasValue) return true;
        __result = forced.Value;
        return false;
    }

    private static IAspect PackAspect(string id, Color[] steady, Color[] blinking, bool restrictive = false)
    {
        var aspect = (IAspect)FormatterServices.GetUninitializedObject(typeof(AlwaysActiveAspect));
        var definition = (AlwaysActiveAspectDefinition)FormatterServices.GetUninitializedObject(typeof(AlwaysActiveAspectDefinition));
        SignalLightDefinition Light(Color colour)
        {
            var light = (SignalLightDefinition)FormatterServices.GetUninitializedObject(typeof(SignalLightDefinition));
            light.Colour = colour;
            return light;
        }
        definition.Id = id;
        definition.DisallowPassing = restrictive;
        definition.OnLights = steady.Select(Light).ToArray();
        definition.BlinkingLights = blinking.Select(Light).ToArray();
        AccessTools.Field(typeof(AspectBase<AlwaysActiveAspectDefinition>), "<Definition>k__BackingField").SetValue(aspect, definition);
        return aspect;
    }

    private static void PolishPackCases()
    {
        // Match PLSignals' actual lamp profiles and ordering: MainSignal places Sz
        // (red plus flashing white) before the plain-red S1. Repeaters carry white
        // identification lamps alongside their steady/blinking warning lamps.
        var red = new Color(.9725491f, .054901965f, .14117648f);
        var yellow = new Color(1, .8235295f, .16078432f);
        var green = new Color(.10980393f, .9921569f, .5254902f);
        var white = new Color(1, 1, 1);
        var none = Array.Empty<Color>();
        var sz = PackAspect("Sz", new[] { red }, new[] { white });
        var s1 = PackAspect("S1", new[] { red }, none, true);
        var main = new[] { sz, s1 };
        var info = main.Select(SignalNativeAspects.Describe).ToArray();
        Check(info[0].SubstitutePermission && !info[0].ShuntingPermission, "Sz is substitute permission, not manual shunting.");
        int stop = SignalAspectRules.SelectStop(info, false);
        Check(stop == 1, "Prefer plain S1 over the earlier red lamp in Sz.");
        Check(SignalNativeAspects.Evaluate(main, info, stop) == 1, "Normal route evaluation must skip Sz even when its native conditions are true.");
        Check(SignalAspectRules.SelectShunting(info, stop) == 1, "Manual shunting must never substitute Sz for a missing shunting aspect.");
        Check(SignalAspectRules.SelectStop(new[] { info[0] }, false) == -1, "Switch off a head whose only red aspect grants substitute permission.");
        info[0].DisallowPassing = true;
        Check(SignalAspectRules.SelectStop(new[] { info[0] }, false) == -1, "Mislabelled restrictive metadata cannot authorize Sz as stop.");
        var unnamed = SignalNativeAspects.Describe(PackAspect("override", new[] { red }, new[] { white }, true));
        Check(unnamed.SubstitutePermission, "Detect red plus flashing white even without a known Sz ID.");
        var plain = SignalNativeAspects.Describe(PackAspect("held", new[] { red }, none, true));
        Check(SignalAspectRules.SelectStop(new[] { unnamed, plain }, false) == 1, "The lamp fallback must exclude substitute permission.");
        Check(info[1].Colour == "#F80E24", "Plain main stop retains its native red lamp colour.");

        var sp1 = SignalNativeAspects.Describe(PackAspect("Sp1", new[] { yellow, white }, none));
        var sp2 = SignalNativeAspects.Describe(PackAspect("Sp2", new[] { green, white }, none));
        var sp3 = SignalNativeAspects.Describe(PackAspect("Sp3", new[] { white }, new[] { green }));
        var sp4 = SignalNativeAspects.Describe(PackAspect("Sp4", new[] { white }, new[] { yellow }));
        Check(sp1.Colour == "#FFD229", "Sp1 must appear yellow rather than permissive green.");
        Check(!sp1.ShuntingPermission && !sp1.SubstitutePermission, "Steady white repeater identification is not shunting permission.");
        Check(SignalAspectRules.SelectStop(new[] { sp2, sp1 }, true) == 1, "Prefer the warning Sp1 even when it is not the first repeater aspect.");
        Check(sp2.Colour == "#1CFD86", "Sp2 combines green with its white repeater lamp.");
        Check(sp3.Colour == sp2.Colour && !sp3.ShuntingPermission, "Sp3 uses blinking green rather than its steady white lamp.");
        Check(sp4.Colour == sp1.Colour && !sp4.ShuntingPermission, "Sp4 uses blinking amber rather than its steady white lamp.");
        var ms2 = SignalNativeAspects.Describe(PackAspect("Ms2", new[] { white }, none));
        Check(ms2.ShuntingPermission && !ms2.SubstitutePermission && ms2.Colour == "#FFFFFF", "Preserve explicitly authorized steady white shunting.");
        Check(SignalAspectRules.SelectShunting(new[] { info[0], info[1], ms2 }, 1) == 2, "Select actual Ms2 while excluding the preceding Sz.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var harmony = new Harmony("RemoteDispatch.NativePatchRegression");
        var prefix = new HarmonyMethod(typeof(NativePatchTests), nameof(Before));
        var inherited = AccessTools.PropertyGetter(typeof(CombinationAspect), nameof(IAspect.DisallowPassing));
        Check(inherited.ReflectedType != inherited.DeclaringType, "Regression must exercise an inherited getter.");
        try
        {
            harmony.Patch(inherited, prefix: prefix);
            throw new Exception("Expected the old 1.4.0 target to be rejected.");
        }
        catch (ArgumentException error)
        {
            Check(error.Message.Contains("declared method"), "Reproduce the user's Harmony failure.");
        }

        PolishPackCases();

        int count = 0;
        try
        {
            foreach (var type in typeof(IAspect).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IAspect).IsAssignableFrom(t)))
            {
                var getter = AccessTools.PropertyGetter(type, nameof(IAspect.DisallowPassing));

                // Avoid Unity constructors/rendering. These are the real native aspect
                // and definition classes; only the getter's managed backing field is needed.
                var aspect = (IAspect)FormatterServices.GetUninitializedObject(type);
                var property = AccessTools.Property(type, "Definition");
                var definition = (AspectBaseDefinition)FormatterServices.GetUninitializedObject(property.PropertyType);
                AccessTools.Field(property.DeclaringType, "<Definition>k__BackingField").SetValue(aspect, definition);

                definition.DisallowPassing = false;
                var info = new[] { new SignalAspectInfo { Id = "Ms1", DisallowPassing = false } };
                bool? original = null;
                SignalNativeAspects.NormalizeRestrictions(new[] { aspect }, info, 0, (_, old) => original = old);
                Check((bool)getter.Invoke(aspect, null), "Native getter must report stop after correction: " + type.Name);
                Check(original == false && info[0].DisallowPassing, "Preserve original stop metadata for restoration.");
                definition.DisallowPassing = true;
                info[0] = new SignalAspectInfo { Id = "Ms2", DisallowPassing = true, ShuntingPermission = true };
                original = null;
                SignalNativeAspects.NormalizeRestrictions(new[] { aspect }, info, -1, (_, old) => original = old);
                Check(!(bool)getter.Invoke(aspect, null), "Native getter must allow white shunting after correction: " + type.Name);
                Check(original == true && !info[0].DisallowPassing, "Preserve original shunting metadata for restoration.");
                info[0] = new SignalAspectInfo { Id = "S2", DisallowPassing = false };
                original = null;
                SignalNativeAspects.NormalizeRestrictions(new[] { aspect }, info, -1, (_, old) => original = old);
                Check(!original.HasValue && !(bool)getter.Invoke(aspect, null), "Other native aspects must remain unchanged.");
                definition.DisallowPassing = true;
                info[0].DisallowPassing = true;
                SignalNativeAspects.NormalizeRestrictions(new[] { aspect }, info, 0, (_, old) => original = old);
                Check(!original.HasValue, "Correct stop metadata must not need mutation.");

                var condition = SignalNativeAspects.Condition(type);
                Check(condition.ReflectedType == condition.DeclaringType, "Condition target must be declared.");
                harmony.Patch(condition, prefix: prefix);
                forced = true;
                Check((bool)condition.Invoke(aspect, null), "White indication prefix must allow " + type.Name);
                forced = false;
                Check(!(bool)condition.Invoke(aspect, null), "White indication prefix must deny " + type.Name);
                count++;
            }
            Console.WriteLine($"PASS: {assertions} native Harmony assertions across {count} DV Signals aspect types (Harmony {typeof(Harmony).Assembly.GetName().Version}).");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
