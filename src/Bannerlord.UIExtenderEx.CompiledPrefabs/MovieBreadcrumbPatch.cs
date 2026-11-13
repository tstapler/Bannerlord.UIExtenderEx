using Bannerlord.UIExtenderEx.CompiledPrefabs;

using HarmonyLib;
using HarmonyLib.BUTR.Extensions;

using System;
using System.Diagnostics;

using TaleWorlds.GauntletUI.Data;
using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.Patches;

/// <summary>
/// Logs and flushes a line just before the game builds a movie from a compiled prefab, the last thing UIExtenderEx knows before the
/// game runs the generated code. A process that dies without an exception then leaves a last log line near the failure.
/// </summary>
public static class MovieBreadcrumbPatch
{
    /// <summary>Applies a Harmony prefix to <see cref="GauntletMovie.Load"/> that runs after every other prefix.</summary>
    public static void Patch(Harmony harmony)
    {
        try
        {
            if (AccessTools2.DeclaredMethod(typeof(GauntletMovie), nameof(GauntletMovie.Load)) is not { } method)
                return;
            harmony.Patch(method, prefix: new HarmonyMethod(AccessTools2.DeclaredMethod(typeof(MovieBreadcrumbPatch), nameof(LoadPrefix)), Priority.Last));
        }
        catch (Exception e)
        {
            Trace.TraceWarning("UIExtenderEx: GauntletMovie.Load could not be patched, the last compiled prefab to load is not logged: {0}", e.Message);
        }
    }

    // Runs after the runtimes registered what they serve, so the manager knows whether this load is compiled
    private static void LoadPrefix(string movieName, IViewModel? datasource, bool doNotUseGeneratedPrefabs)
    {
        if (doNotUseGeneratedPrefabs || datasource is null)
            return;
        var type = datasource.GetType();
        CompiledPrefabRuntime.Manager.OnMovieAboutToLoad(movieName, type.FullName ?? type.Name);
    }
}
