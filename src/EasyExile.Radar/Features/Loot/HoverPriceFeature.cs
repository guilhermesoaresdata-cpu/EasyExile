using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Loot;

namespace EasyExile.Radar.Features.Loot;

/// <summary>
/// What the item under the cursor is worth, in any item UI.
/// </summary>
/// <remarks>
/// Port of POE2Radar (MIT) <c>HoverPriceSettings</c> and the hover chip in
/// <c>OverlayRenderer</c>. Inventory, stash, vendor, reward grid and the flask
/// bar are all covered because the client hangs an item entity off the same
/// field in every one of them.
///
/// The innermost slot wins. Grids nest a slot inside panel elements, so several
/// rectangles contain the cursor at once and only the smallest is the item.
///
/// The chip prices the STACK, not the unit. Forty Exalted Orbs valued as one is
/// not a rounding error, it is wrong by a factor of forty — and a stack is
/// exactly when a person wants the number.
/// </remarks>
public sealed class HoverPriceFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly PriceBook _prices;
    private readonly Func<Vector2> _cursor;

    public HoverPriceFeature(RadarSettings settings, PriceBook prices, Func<Vector2> cursor)
    {
        _settings = settings;
        _prices = prices;
        _cursor = cursor;
    }

    public string Name => "Preco sob o cursor";

    public bool Enabled => _settings.Loot.Enabled && _settings.Loot.ShowHoverPrice;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        if (!Enabled || !frame.HasWorld || !_prices.IsLoaded) return;

        var snapshot = frame.Snapshot!;

        if (frame.Panels.Slots.Length == 0) return;

        var cursor = _cursor();
        var options = _settings.Loot;

        var hovered = SlotHighlightFeature.Hovered(frame.Panels, cursor);

        if (hovered is null) return;

        if (Price(hovered.Item) is not { } price) return;

        var total = price.Exalted * hovered.Count;

        var described = _prices.Describe(price, total, options.MinQuantity,
            options.NameUniques && hovered.Item.IsUnique);

        var text = hovered.Count > 1 ? $"{hovered.Count}x  {described}" : described;

        var scale = options.TextScale;
        var size = canvas.MeasureText(text, scale);
        var colour = unchecked((uint)(total >= options.HighlightExalted
            ? options.RichColour
            : options.PricedColour));

        // Which corner is free depends on the panel and on the item — the game
        // writes stack counts, sockets and quality marks into these same
        // corners — so there is no answer that is right everywhere.
        var (left, top) = SlotHighlightFeature.Corner(options.HoverCorner, hovered, size);

        var min = new Vector2(left - 5f, top - 3f);
        var max = new Vector2(left + size.X + 5f, top + size.Y + 3f);

        canvas.Rect(min, max, Palette.Shadow);
        canvas.Rect(min, max, colour, filled: false);
        canvas.Text(new Vector2(left, top), colour, text, scale);
    }

    /// <summary>
    /// Art for a unique, name for everything else. No cross-fallback.
    /// </summary>
    /// <remarks>
    /// The reference is strict about this and it is not fussiness. The art index
    /// holds uniques; the name index holds everything. Letting a non-unique fall
    /// back to art is how a rare body armour picks up some unique's price and
    /// reads "3.8 div" on a level-five character — which is exactly what it did.
    ///
    /// The other direction is wrong too, more quietly: one .dds is shared across
    /// a currency's tiers, so pricing an Orb of Augmentation by art returns
    /// whichever tier happened to index that icon.
    /// </remarks>
    private PriceResult? Price(ItemSnapshot item) =>
        item.IsUnique
            ? (item.Art is { Length: > 0 } art ? _prices.TryByArt(art, item.BaseName) : null)
            : (item.BaseName is { Length: > 0 } name ? _prices.TryByName(name) : null);

}
