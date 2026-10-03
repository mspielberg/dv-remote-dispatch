using HarmonyLib;
using Signals.Common.Aspects;
using Signals.Game.Aspects;
using System;
using System.Linq;
using System.Reflection;

namespace DvMod.RemoteDispatch
{
    internal static class SignalNativeAspects
    {
        private static SignalLampColour Colour(Signals.Common.SignalLightDefinition lamp) =>
            new SignalLampColour(lamp.Colour.r, lamp.Colour.g, lamp.Colour.b);

        public static bool IsSubstitute(AspectBaseDefinition definition) =>
            definition.Id.Equals("Sz", StringComparison.OrdinalIgnoreCase) ||
            definition.OnLights.Any(l => Colour(l).Red) && definition.BlinkingLights.Any(l => Colour(l).White);

        private static bool ShuntingCondition(AspectBaseDefinition definition) =>
            definition is ShuntingAllowedAspectDefinition shunting && !shunting.Invert ||
            definition is CombinationAspectDefinition combination && combination.Conditions.Any(ShuntingCondition);

        public static bool IsShunting(AspectBaseDefinition definition) => !IsSubstitute(definition) &&
            (ShuntingCondition(definition) || definition.Id.Equals("Ms2", StringComparison.OrdinalIgnoreCase) ||
            definition.OnLights.Length > 0 && definition.BlinkingLights.Length == 0 && definition.OnLights.All(l => Colour(l).White));

        public static SignalAspectInfo Describe(IAspect aspect)
        {
            var definition = aspect.GetDefinition();
            bool warningStop = aspect.Id.Equals("Sp1", StringComparison.OrdinalIgnoreCase) ||
                aspect.Id.Equals("Os1", StringComparison.OrdinalIgnoreCase);
            string fallback = warningStop ? "yellow" : aspect.DisallowPassing ? "red" :
                IsShunting(definition) ? "white" : definition.UsePassingSpeed ? "yellow" : "green";
            return new SignalAspectInfo {
                Id = aspect.Id, DisallowPassing = aspect.DisallowPassing,
                ShuntingPermission = IsShunting(definition), SubstitutePermission = IsSubstitute(definition),
                ShuntingDenied = definition is ShuntingAllowedAspectDefinition s && s.Invert,
                StopLamp = definition.OnLights.Any(l => Colour(l).Red || Colour(l).Blue),
                Colour = SignalAspectRules.SelectColour(definition.OnLights.Concat(definition.BlinkingLights).Select(Colour).ToArray(), fallback)
            };
        }

        public static int Evaluate(IAspect[] aspects, SignalAspectInfo[] info, int stop)
        {
            for (int i = 0; i < aspects.Length; i++)
                if (!info[i].SubstitutePermission && aspects[i].MeetsConditions()) return i;
            return stop;
        }

        public static void NormalizeRestrictions(IAspect[] aspects, SignalAspectInfo[] info, int stop,
            Action<AspectBaseDefinition, bool> rememberOriginal)
        {
            for (int i = 0; i < aspects.Length; i++)
            {
                bool? restrictive = i == stop ? true : info[i].ShuntingPermission || info[i].SubstitutePermission ? false : (bool?)null;
                if (!restrictive.HasValue) continue;
                var definition = aspects[i].GetDefinition();
                if (definition.DisallowPassing != restrictive.Value)
                {
                    rememberOriginal(definition, definition.DisallowPassing);
                    definition.DisallowPassing = restrictive.Value;
                }
                info[i].DisallowPassing = restrictive.Value;
            }
        }

        public static MethodInfo Condition(Type aspectType) =>
            AccessTools.GetDeclaredMember(AccessTools.Method(aspectType, "MeetsConditions"));
    }
}
