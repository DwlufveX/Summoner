using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;

internal static class Program
{
    private static string managed;

    private static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: ScribeHost <managed> <mod-dll> <log> <full|regression|live-guard>");
            return 2;
        }
        managed = Path.GetFullPath(args[0]);
        string modPath = Path.GetFullPath(args[1]);
        LogSink.Path = Path.GetFullPath(args[2]);
        string mode = args[3];
        if (mode != "full" && mode != "regression" && mode != "live-guard")
            return 2;
        Directory.CreateDirectory(Path.GetDirectoryName(LogSink.Path));
        File.WriteAllText(LogSink.Path, "");
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Harmony host = new Harmony("summoner.isolated.host");
        try
        {
            Assembly game = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
            Assembly mod = Assembly.LoadFrom(modPath);
            Type log = game.GetType("Verse.Log", true);
            foreach (MethodInfo method in log.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name == "Error" || method.Name == "Warning")
                    host.Patch(method, prefix: new HarmonyMethod(typeof(LogSink).GetMethod(nameof(LogSink.Prefix))));
            }
            // A headless test cannot execute Unity/DefOf attachment notifications.
            // This fixture limitation is reported; no Scribe/initialization errors are suppressed.
            MethodInfo notify = game.GetType("Verse.ThingOwner", true).GetMethod("NotifyAdded", BindingFlags.Instance | BindingFlags.NonPublic);
            host.Patch(notify, prefix: new HarmonyMethod(typeof(LogSink).GetMethod(nameof(LogSink.SkipAddedNotice))));

            Type check = mod.GetType("RimMod.Summoner.DataFoundationCheck", true);
            MethodInfo run = check.GetMethod(mode == "regression" ? "TryRunDiagnosticsOnly" : "TryRun");
            PropertyInfo currentGame = game.GetType("Verse.Current", true).GetProperty("Game");
            object oldGame = currentGame.GetValue(null);
            bool allOk = true;
            try
            {
                if (mode == "live-guard")
                    currentGame.SetValue(null, Uninitialized(game.GetType("Verse.Game", true)));
                for (int pass = 1; pass <= 2; pass++)
                {
                    object[] result = { null };
                    bool ok = (bool)run.Invoke(null, result);
                    string report = (string)result[0];
                    if (mode == "live-guard")
                    {
                        Type db = game.GetType("Verse.DefDatabase`1", true).MakeGenericType(game.GetType("Verse.ThingDef", true));
                        object injected = db.GetMethod("GetNamedSilentFail").Invoke(null, new object[] { "SummonerIsolatedPawn" });
                        allOk &= !ok && report.Contains("ENTITY SKIPPED") && report.Contains("IN_MEMORY PASS") && injected == null;
                    }
                    else
                        allOk &= ok;
                    Console.WriteLine("RUN " + pass + " RESULT " + ok);
                    Console.WriteLine(report);
                    string scribeMode = game.GetType("Verse.Scribe", true).GetField("mode").GetValue(null).ToString();
                    int hooks = 0;
                    Patches info = Harmony.GetPatchInfo(log.GetMethod("Error", new[] { typeof(string) }));
                    if (info != null)
                        foreach (Patch patch in info.Prefixes)
                            if (patch.owner.StartsWith("rimmod.summoner.diagnostics.", StringComparison.Ordinal))
                                hooks++;
                    Console.WriteLine("CLEANUP mode=" + scribeMode + " diagnosticHooks=" + hooks);
                    allOk &= scribeMode == "Inactive" && hooks == 0;
                }
            }
            finally
            {
                currentGame.SetValue(null, oldGame);
            }
            Console.WriteLine("HOST " + mode + " " + (allOk ? "PASS" : "FAIL"));
            Console.WriteLine("RAW_LOG=" + LogSink.Path);
            return allOk ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
        finally
        {
            host.UnpatchAll(host.Id);
        }
    }

    private static object Uninitialized(Type type)
    {
#if RIMWORLD_1_7
        return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
#else
        return FormatterServices.GetUninitializedObject(type);
#endif
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == name)
                return assembly;
        string path = Path.Combine(managed, name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}

public static class LogSink
{
    public static string Path;
    public static bool Prefix(string text)
    {
        File.AppendAllText(Path, text + "\r\n----\r\n");
        return false;
    }
    public static bool SkipAddedNotice() => false;
}
