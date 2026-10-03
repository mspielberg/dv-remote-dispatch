using System;
using System.Collections.Generic;

namespace DvMod.RemoteDispatch
{
    public sealed class SignalAspectInfo
    {
        public string Id = "";
        public string Colour = "red";
        public bool DisallowPassing, ShuntingPermission, ShuntingDenied, StopLamp, SubstitutePermission;
    }

    public readonly struct SignalLampColour
    {
        public readonly float R, G, B;
        public SignalLampColour(float r, float g, float b) { R = r; G = g; B = b; }
        public bool White => R > .65f && G > .65f && B > .65f;
        public bool Red => R > .65f && G < .4f && B < .4f;
        public bool Yellow => R > .65f && G >= .4f && B < .6f;
        public bool Green => G > .5f && G > R && G > B;
        public bool Blue => B > .65f && R < .4f && G < .5f;
        private static int Channel(float value) => Math.Max(0, Math.Min(255, (int)Math.Round(value * 255)));
        public string Html => "#" + Channel(R).ToString("X2") + Channel(G).ToString("X2") + Channel(B).ToString("X2");
    }

    public static class SignalAspectRules
    {
        public static int SelectStop(IReadOnlyList<SignalAspectInfo> aspects, bool advanceWarning)
        {
            // Explicit plain stop/warning IDs outrank a permissive aspect that also
            // lights a red lamp (for example Polish Sz, listed before S1).
            for (int i = 0; i < aspects.Count; i++)
            {
                var a = aspects[i];
                string id = a.Id.ToUpperInvariant();
                if (!a.SubstitutePermission && (id == "MS1" || id == "MS-S1" || id == "S1" ||
                    id == "SP1" || id == "OS1" || id == "STOP")) return i;
            }
            for (int i = 0; i < aspects.Count; i++)
                if (!aspects[i].SubstitutePermission && (aspects[i].ShuntingDenied ||
                    aspects[i].StopLamp && !aspects[i].ShuntingPermission)) return i;
            for (int i = 0; i < aspects.Count; i++)
                if (aspects[i].DisallowPassing && !aspects[i].ShuntingPermission && !aspects[i].SubstitutePermission) return i;
            if (advanceWarning)
                for (int i = 0; i < aspects.Count; i++)
                    if (!aspects[i].ShuntingPermission && !aspects[i].SubstitutePermission) return i;
            return -1;
        }
        public static int SelectShunting(IReadOnlyList<SignalAspectInfo> aspects, int stop)
        {
            for (int i = 0; i < aspects.Count; i++)
                if (aspects[i].ShuntingPermission && !aspects[i].SubstitutePermission) return i;
            return stop;
        }

        public static string SelectColour(IReadOnlyList<SignalLampColour> lamps, string fallback)
        {
            // White identifies a repeater, while its coloured lamp carries the warning.
            // Include blinking lamps so Sp3/Sp4 do not become white on the map.
            for (int priority = 0; priority < 4; priority++)
                foreach (var lamp in lamps)
                    if (!lamp.White && (priority == 0 && lamp.Red || priority == 1 && lamp.Yellow ||
                        priority == 2 && lamp.Green || priority == 3 && lamp.Blue)) return lamp.Html;
            foreach (var lamp in lamps) if (!lamp.White) return lamp.Html;
            return lamps.Count > 0 ? lamps[0].Html : fallback;
        }
    }
}
