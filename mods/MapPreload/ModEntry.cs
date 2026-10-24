using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using xTile;

namespace MapPreload;

/// <summary>
/// Creating a new game builds every location in Data/Locations, and each one loads its map through
/// <c>Game1.game1.xTileContent</c> on the main thread. With Stardew Valley Expanded and friends that is
/// ~350 maps and ~11 seconds without handling input, which is past Android's 10-second ANR limit.
/// This loads the same maps into the same content manager a few at a time while the title screen is
/// open, so the new game finds them already cached.
/// </summary>
public sealed class ModEntry : Mod
{
    /// <summary>Main-thread time spent loading maps per tick. One SVE map alone can take longer.</summary>
    private const double BudgetMs = 12;

    /// <summary>Let Content Patcher finish its title-screen context update before loading anything it patches.</summary>
    private const int StartDelayTicks = 180;

    private Queue<string>? pending;
    private LocalizedContentManager? warmedManager;
    private int titleTicks;
    private int loaded;
    private int failed;
    private readonly Stopwatch spent = new();

    public override void Entry(IModHelper helper)
    {
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (Context.IsWorldReady || Game1.gameMode != Game1.titleScreenGameMode)
        {
            titleTicks = 0;
            if (pending is { Count: > 0 })
            {
                Monitor.Log($"Left the title screen with {pending.Count} maps not preloaded.");
                pending = null;
            }
            return;
        }

        // Returning to the title disposes xTileContent and creates a new one, which starts out empty.
        if (!ReferenceEquals(warmedManager, Game1.game1.xTileContent))
        {
            warmedManager = null;
            pending = null;
        }

        if (++titleTicks < StartDelayTicks || warmedManager != null && pending == null)
            return;

        if (pending == null)
        {
            pending = new Queue<string>(GetMapPaths());
            warmedManager = Game1.game1.xTileContent;
            loaded = failed = 0;
            spent.Reset();
            Monitor.Log($"Preloading {pending.Count} location maps.");
        }

        LocalizedContentManager content = warmedManager!;
        var tick = Stopwatch.StartNew();
        spent.Start();
        while (pending.Count > 0 && tick.Elapsed.TotalMilliseconds < BudgetMs)
        {
            string path = pending.Dequeue();
            try
            {
                content.Load<Map>(path);
                loaded++;
            }
            catch (Exception ex)
            {
                // The game will hit the same error when it creates the location; that is not ours to report.
                failed++;
                Monitor.Log($"Couldn't preload '{path}': {ex.Message}");
            }
        }
        spent.Stop();

        if (pending.Count == 0)
        {
            Monitor.Log($"Preloaded {loaded} maps ({failed} failed) in {spent.ElapsedMilliseconds} ms of main-thread time.", LogLevel.Debug);
            pending = null;
        }
    }

    /// <summary>The maps <c>Game1.AddLocations</c> will load, in the same order.</summary>
    private static IEnumerable<string> GetMapPaths()
    {
        return DataLoader.Locations(Game1.content).Values
            .Select(location => location.CreateOnLoad?.MapPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)!;
    }
}
