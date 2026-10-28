using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimMod.Summoner
{
    // Temporary, thread-local test instrumentation. Never installed during normal mod startup.
    internal sealed class ScribeDiagnostics : IDisposable
    {
        [ThreadStatic] private static ScribeDiagnostics current;
        private readonly ScribeDiagnostics parent;
        private readonly bool isolated;
        private readonly Harmony harmony;
        private readonly MethodInfo logError;
        private readonly List<string> failures = new List<string>();
        private bool disposed;

        public int CriticalCount => failures.Count;
        public int EnvironmentCount { get; private set; }

        public ScribeDiagnostics(bool isolated = false)
        {
            this.isolated = isolated;
            parent = current;
            if (parent == null)
            {
                logError = typeof(Log).GetMethod("Error", new[] { typeof(string) });
                if (logError == null)
                    throw new MissingMethodException("Verse.Log.Error(string)");
                harmony = new Harmony("rimmod.summoner.diagnostics." + Guid.NewGuid().ToString("N"));
                // Observe before a host's logging prefix can suppress the original method.
                harmony.Patch(logError, prefix: new HarmonyMethod(typeof(ScribeDiagnostics), nameof(Observe)) { priority = Priority.First });
            }
            current = this;
        }

        private static void Observe(string __0)
        {
            if (string.IsNullOrEmpty(__0))
                return;
            bool environment = Current.Game == null &&
                (__0.StartsWith("Exception getting types in assembly ", StringComparison.Ordinal) || IsSameTypeCatalogFallback(__0));
            for (ScribeDiagnostics scope = current; scope != null; scope = scope.parent)
            {
                if (environment)
                    scope.EnvironmentCount++;
                else
                    scope.failures.Add(__0);
                if (scope.isolated)
                    break;
            }
        }

        private static bool IsSameTypeCatalogFallback(string message)
        {
            const string prefix = "Could not find class ";
            const string fallback = "Trying to use ";
            if (!message.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            int wantedEnd = message.IndexOf(" while resolving node ", prefix.Length, StringComparison.Ordinal);
            int fallbackStart = message.IndexOf(fallback, StringComparison.Ordinal);
            if (wantedEnd < 0 || fallbackStart < 0)
                return false;
            fallbackStart += fallback.Length;
            int fallbackEnd = message.IndexOf(" instead.", fallbackStart, StringComparison.Ordinal);
            return fallbackEnd >= 0 && string.Equals(message.Substring(prefix.Length, wantedEnd - prefix.Length),
                message.Substring(fallbackStart, fallbackEnd - fallbackStart), StringComparison.Ordinal);
        }

        public void RequireClean(string operation)
        {
            if (failures.Count == 0)
                return;
            string first = failures[0];
            int newline = first.IndexOfAny(new[] { '\r', '\n' });
            if (newline >= 0)
                first = first.Substring(0, newline);
            throw new InvalidOperationException(operation + " recorded " + failures.Count + " critical diagnostic(s): " + first);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            current = parent;
            if (harmony != null)
                harmony.Unpatch(logError, HarmonyPatchType.Prefix, harmony.Id);
        }
    }
}
