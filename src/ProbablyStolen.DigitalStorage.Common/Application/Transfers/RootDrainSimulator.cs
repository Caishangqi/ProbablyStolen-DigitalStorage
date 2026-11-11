using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>One simulated placement.</summary>
public readonly struct ForecastPlacement
{
    internal ForecastPlacement(RootSnapshot root, int page, int slot)
    {
        Root = root;
        Page = page;
        Slot = slot;
    }

    /// <summary>Gets the root as the source offered it.</summary>
    public RootSnapshot Root { get; }

    /// <summary>Gets the target page.</summary>
    public int Page { get; }

    /// <summary>Gets the target strip slot.</summary>
    public int Slot { get; }
}

/// <summary>What draining a cell's own pages into its strip pages would do.</summary>
public sealed class DrainForecast
{
    internal DrainForecast(IReadOnlyList<ForecastPlacement> placements, int appendedPages, CellView after,
        RootSnapshot? rejectedRoot, StoreRejection? rejection)
    {
        Placements = placements;
        AppendedPages = appendedPages;
        After = after;
        RejectedRoot = rejectedRoot;
        Rejection = rejection;
    }

    /// <summary>Gets every planned placement, in source order.</summary>
    public IReadOnlyList<ForecastPlacement> Placements { get; }

    /// <summary>Gets how many strip pages would be appended.</summary>
    public int AppendedPages { get; }

    /// <summary>Gets the simulated view after the drain (or up to the first rejection).</summary>
    public CellView After { get; }

    /// <summary>Gets whether every root fits.</summary>
    public bool Accepted => Rejection == null;

    /// <summary>Gets the first root that would be refused, if any.</summary>
    public RootSnapshot? RejectedRoot { get; }

    /// <summary>Gets why the first refused root would be refused, if any.</summary>
    public StoreRejection? Rejection { get; }

    /// <summary>
    /// Gets whether the cell is over its byte capacity or type limit. This never blocks migration: relocating does
    /// not change usage, and the cell keeps refusing new items until it is under its limits again.
    /// </summary>
    public bool OverCapacity => After.UsedBytes() > After.Rules.Tier.GetBytes() || After.TypeCount > After.TypeLimit;
}

/// <summary>
/// Simulates a whole same-cell drain before any item moves. It plans every root in source order
/// with <see cref="PlanScope.Relocate"/> on a copy of the view, exactly as the real sink does.
/// </summary>
public static class RootDrainSimulator
{
    /// <summary>Forecasts moving <paramref name="roots"/>, given in source order, inside <paramref name="cell"/>.</summary>
    /// <exception cref="ArgumentException">A root is not on the page and slot its snapshot claims.</exception>
    public static DrainForecast Forecast(CellView cell, IEnumerable<RootSnapshot> roots)
    {
        if (cell == null) throw new ArgumentNullException(nameof(cell));
        if (roots == null) throw new ArgumentNullException(nameof(roots));

        var view = cell.Clone();
        var placements = new List<ForecastPlacement>();
        var appended = 0;
        foreach (var root in roots)
        {
            var plan = CellStorePlanner.Plan(view, root.ToCandidate(), PlanScope.Relocate);
            if (!plan.IsAccepted) return new DrainForecast(placements, appended, view, root, plan.Rejection);
            if (plan.NewPage is { } geometry)
            {
                view.AppendPage(geometry);
                appended++;
            }

            view.RecordRelocated(root.Page, view.SlotAt(root.Page, root.X, root.Y), plan.Page, plan.Slot);
            placements.Add(new ForecastPlacement(root, plan.Page, plan.Slot));
        }

        return new DrainForecast(placements, appended, view, null, null);
    }
}
