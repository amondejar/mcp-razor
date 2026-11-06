using System.Collections.Concurrent;

namespace SalesOps;

public enum ChangeSetStatus { Pending, Applied, Discarded }

public sealed record PriceChangeItem(
    string Sku,
    string Name,
    decimal CurrentPrice,
    decimal ProposedPrice,
    decimal Cost,
    int MonthlyUnits,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Rows with warnings start unselected: a person has to opt in.</summary>
    public bool Recommended => Warnings.Count == 0;
}

/// <summary>What propose_price_changes returns as structured content, and what the review app shows.</summary>
public sealed record PriceChangeSet(
    string Id,
    string Category,
    decimal Percent,
    string Reason,
    ChangeSetStatus Status,
    IReadOnlyList<PriceChangeItem> Items,
    DateTimeOffset CreatedAt,
    PriceChangeResult? Result = null);

public sealed record PriceChangeResult(int Applied, int Skipped, decimal MonthlyRevenueImpact, DateTimeOffset At);

/// <summary>
/// Price changes go through a change set: the agent proposes, a person reviews and applies. The rules are enforced
/// here, on the server, whatever the UI sends: a set is applied once, only its own products, never below cost.
/// </summary>
public sealed class PriceChangeService(Catalog catalog, SalesService sales, TimeProvider time)
{
    public const decimal MinimumMargin = 0.15m;
    public const decimal LargeChange = 0.20m;

    private readonly ConcurrentDictionary<string, PriceChangeSet> _sets = new(StringComparer.Ordinal);
    private int _next = 1000;

    public PriceChangeSet Propose(string category, decimal percent, string reason)
    {
        var normalized = Catalog.NormalizeCategory(category)
            ?? throw new ArgumentException($"Unknown category '{category}'. Use one of: {string.Join(", ", Catalog.Categories)}.");
        if (percent is < -50 or > 50 or 0)
        {
            throw new ArgumentException("The percentage must be between -50 and 50, and not 0.");
        }

        var items = catalog.Products
            .Where(product => product.Category == normalized)
            .Select(product =>
            {
                var current = catalog.CurrentPrice(product.Sku);
                var proposed = RoundToNinetyNine(current * (1 + percent / 100));
                return new PriceChangeItem(product.Sku, product.Name, current, proposed, product.Cost,
                    sales.AverageMonthlyUnits(product.Sku), Warnings(current, proposed, product.Cost));
            })
            .ToList();

        var set = new PriceChangeSet($"CS-{Interlocked.Increment(ref _next)}", normalized, percent,
            string.IsNullOrWhiteSpace(reason) ? "No reason given." : reason.Trim(), ChangeSetStatus.Pending, items, time.GetUtcNow());
        _sets[set.Id] = set;
        return set;
    }

    public PriceChangeSet? Get(string id) => _sets.GetValueOrDefault(id);

    /// <summary>Applies the approved rows (SKU → price) and skips the rest. Throws if the request breaks a rule.</summary>
    public PriceChangeSet Apply(string id, IReadOnlyDictionary<string, decimal> approved)
    {
        lock (_sets)
        {
            var set = Pending(id);
            var items = set.Items.ToDictionary(item => item.Sku);

            foreach (var (sku, price) in approved)
            {
                if (!items.TryGetValue(sku, out var item))
                {
                    throw new InvalidOperationException($"{sku} is not part of change set {id}.");
                }

                if (price <= item.Cost)
                {
                    throw new InvalidOperationException($"{sku}: {price} is not above its cost ({item.Cost}). Prices below cost are never applied.");
                }
            }

            foreach (var (sku, price) in approved)
            {
                catalog.SetPrice(sku, price);
            }

            var impact = approved.Sum(pair => (pair.Value - items[pair.Key].CurrentPrice) * items[pair.Key].MonthlyUnits);
            var applied = set with
            {
                Status = ChangeSetStatus.Applied,
                Result = new PriceChangeResult(approved.Count, set.Items.Count - approved.Count, Math.Round(impact, 2), time.GetUtcNow()),
            };
            _sets[id] = applied;
            return applied;
        }
    }

    public PriceChangeSet Discard(string id)
    {
        lock (_sets)
        {
            var discarded = Pending(id) with { Status = ChangeSetStatus.Discarded };
            _sets[id] = discarded;
            return discarded;
        }
    }

    public static IReadOnlyList<string> Warnings(decimal current, decimal proposed, decimal cost)
    {
        var warnings = new List<string>();
        if (proposed <= cost)
        {
            warnings.Add("Below cost");
        }
        else if ((proposed - cost) / proposed < MinimumMargin)
        {
            warnings.Add("Margin under 15 %");
        }

        if (current > 0 && Math.Abs(proposed - current) / current > LargeChange)
        {
            warnings.Add("Change over 20 %");
        }

        return warnings;
    }

    private PriceChangeSet Pending(string id)
    {
        var set = _sets.GetValueOrDefault(id) ?? throw new InvalidOperationException($"Change set {id} does not exist.");
        return set.Status == ChangeSetStatus.Pending
            ? set
            : throw new InvalidOperationException($"Change set {id} was already {set.Status.ToString().ToLowerInvariant()}.");
    }

    // Charm pricing: 97.19 → 97.99.
    private static decimal RoundToNinetyNine(decimal price) => Math.Floor(price) + 0.99m;
}
