using System;

namespace DvMod.RemoteDispatch.Signals
{
    internal static class LoggingReturn
    {
        private const string Prefix = "[RemoteDispatch.Signals] ";

        private static Action<string> Prefixed(Action<string> inner) =>
            msg => inner(Prefix + msg);

        // Nullable fields allow runtime initialization - the initial null is safe.
        // Initialized in SignalsShim.Initialize() which passes actual logging callbacks.
        internal static Action<string>? Log;
        internal static Action<string>? DebugLog;
        internal static Action<string>? Warning;

        /// <summary>
        /// Initialize logging callbacks with auto-prefixing. Call this instead of directly assigning fields.
        /// </summary>
        /// <param name="log">The main logger callback</param>
        /// <param name="debugLog">The debug logger callback</param>
        /// <param name="warning">The warning logger callback</param>
        internal static void Initialize(Action<string> log, Action<string> debugLog, Action<string> warning)
        {
            Log = Prefixed(log);
            DebugLog = Prefixed(debugLog);
            Warning = Prefixed(warning);
        }
    }
}
