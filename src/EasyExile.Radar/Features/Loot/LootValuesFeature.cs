using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Loot;

namespace EasyExile.Radar.Features.Loot;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// What a drop is worth, on the game's own loot tag.
/// </summary>
/// <remarks>
/// Port of POE2Radar (MIT) <c>RadarApp.BuildItemLabels</c> and
/// <c>OverlayRenderer.DrawItemLabels</c>, including the reference's default of
/// anchoring the chip to the tag.
///
/// Two routes, and which one applies is not a preference:
///
/// The game already names everything it considers worth naming, and it draws
/// that name in a rectangle it computed. Putting the value on that rectangle
/// costs no projection and cannot jitter. It also cannot be misplaced, which the
/// world-projected route very much can: the game lays its tags out in a column
/// so they never overlap, so a chip at the item's own screen position ends up
/// near the item and nowhere near its name.
///
/// The exception is an unidentified unique, whose name the game deliberately
/// hides. There is no tag text to match, so it takes the projected route — and
/// that is exactly the case where knowing costs something.
///
/// Drawn whether the game's map is open or not: loot does not stop mattering
/// because a map is up.
/// </remarks>
public sealed class LootValuesFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly RadarStats _stats;
    private readonly PriceBook _prices;

    /// <summary>Bases already labelled on a tag this frame, so nothing is drawn twice.</summary>
    private readonly HashSet<string> _tagged = new(StringComparer.OrdinalIgnoreCase);

    public LootValuesFeature(RadarSettings settings, RadarStats stats, PriceBook prices)
    {
        _settings = settings;
        _stats = stats;
        _prices = prices;
    }

    public string Name => T("Valores de loot");

    public bool Enabled => _settings.Loot.Enabled;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        _stats.LootPriced = 0;

        if (!Enabled || !frame.HasWorld) return;

        var snapshot = frame.Snapshot!;

        // The league the character is actually in decides which prices apply.
        // Handed over rather than guessed: a hardcore league's economy is not
        // the softcore one's.
        if (snapshot.League is { Length: > 0 } league) _prices.SetDetectedLeague(league);

        _prices.RefreshIfDue();

        if (!_prices.IsLoaded) return;

        var options = _settings.Loot;

        if (options.AnchorValuesToTags) DrawOnTags(frame, canvas, snapshot, options);

        DrawProjected(frame, canvas, snapshot, options);
    }

    /// <summary>
    /// The chip on the game's own tag, matched by the name the game printed.
    /// </summary>
    private void DrawOnTags(
        RenderFrame frame, IOverlayCanvas canvas, WorldSnapshot snapshot, LootSettings options)
    {
        var bounds = frame.ClientBounds;
        var camera = frame.MapFrame?.Camera ?? snapshot.Camera;

        _tagged.Clear();

        var (tags, tagCamera) = frame.GroundTags;

        _stats.LootTags = tags.Length;

        foreach (var label in tags)
        {
            // The ITEM first, the tag's text only as a fallback.
            //
            // Matching on text alone could never name a unique: the game writes
            // the BASE on a unique's ground tag — "Hardwood Spear" — so looking
            // that up finds nothing and the drop got no label at all. The entity
            // beside the tag knows what it really is.
            //
            // Pairing by projected position is sound and measured: a tag sits on
            // its entity's projection to within a pixel horizontally and the
            // nameplate's own lift vertically.
            var owner = OwnerEntity(snapshot, label);
            var item = owner?.Item;

            if (item is not null) _stats.LootTagsMatched++;

            // Normally nothing: the rectangle above was read on THIS frame,
            // against the camera below, so there is no gap to correct. It earns
            // its keep when the fast lane has no reading yet and the tags come
            // from the sweep instead — then the chip sits where the tag WAS, and
            // this is the difference. The item has not moved — the camera has
            // — so re-projecting it through both cameras gives the pixels the
            // view has travelled since, and the chip rides along smoothly.
            // The camera from the SWEEP, not from the tick. Once the sweep is
            // throttled those are different instants, and correcting against
            // the wrong "before" overshoots — worse than not correcting.
            var drift = Drift(owner, tagCamera ?? snapshot.Camera, camera);

            var price = item is not null
                ? Price(item)
                : _prices.TryByName(label.Text);

            var unique = item?.IsUnique ?? false;

            if (price is not { } value)
            {
                _stats.LootTagsUnpriced++;

                // A unique with no price was being dropped silently, which
                // deleted the one item on the ground that always deserves a
                // look. A unique is priced by its ART, because the game writes
                // only the base on the tag - so a unique whose art the price
                // book does not carry looks exactly like a rare that nobody
                // buys, and got the same treatment.
                //
                // "No price" is not "no value" here; it is the tool admitting
                // it does not know. The colour for saying so already existed
                // and was only ever used in the stash.
                if (unique && options.ShowUnpricedUniques)
                    Unpriced(canvas, options, label, drift, bounds);

                continue;
            }

            // The floor hides common currency and rares nobody would stop for.
            // A unique is not that: knowing WHICH one dropped is worth having
            // regardless of what the market happens to say it is worth today -
            // the same reasoning that already made an unpriced unique worth a
            // mark rather than silence. "Wayfarer Jacket" was The Dancing
            // Mirage at 0,92 ex, under the default 5 ex floor, and the floor
            // check below used to delete the whole tag before its name was
            // ever looked at.
            if (!unique && value.Exalted < options.MinimumFor(value.Category, unique))
            {
                _stats.LootTagsBelowFloor++;
                continue;
            }

            if (!options.ShowsCategory(value.Category))
            {
                _stats.LootTagsFiltered++;
                continue;
            }

            if (label.Right + drift.X < bounds.X || label.X + drift.X > bounds.Right ||
                label.CentreY + drift.Y < bounds.Y || label.CentreY + drift.Y > bounds.Bottom) continue;

            var centre = label.CentreX + drift.X;
            var top = label.Y + drift.Y;
            var bottom = label.Bottom + drift.Y;

            // OVER the game's line, not above it. The generic base is painted
            // out and the real name takes its place at its size, in the colour
            // the game would have used for that rarity — so it reads as the tag
            // having said the right thing all along.
            if (options.RevealNames &&
                value.Name is { Length: > 0 } name &&
                !string.Equals(name, label.Text, StringComparison.OrdinalIgnoreCase))
            {
                // Scaled to the tag's own height so the replacement is the same
                // size as what it replaces. Measured at 1 and divided, because
                // the tag's height is in client pixels and the font's is not.
                var unit = canvas.MeasureText(name);
                var scale = unit.Y > 0.1f ? Math.Clamp(label.Height / unit.Y, 0.6f, 3f) : options.TextScale;
                var size = canvas.MeasureText(name, scale);

                var half = Math.Max(label.Width, size.X + 12f) / 2f;

                var colour = RarityColour(item, options);

                var min = new Vector2(centre - half, top - 2f);
                var max = new Vector2(centre + half, bottom + 2f);

                // A plate, not a black bar. Fully opaque because at anything
                // less the game's own line reads straight through and you get
                // both names at once — and framed in the rarity's own colour,
                // dimmed, because that is what the client's tag looks like and
                // the replacement should not announce itself as a sticker.
                canvas.Rect(min, max, Palette.Rgba(10, 10, 12));
                canvas.Rect(min, max, Palette.Dim(colour, 0.55f), filled: false);

                canvas.Text(new Vector2(centre - (size.X / 2f), top), colour, name, scale);
            }

            // The value directly under the tag, centred on it.
            var text = _prices.Describe(value, value.Exalted, options.MinQuantity, nameIt: false);
            var measured = canvas.MeasureText(text, options.TextScale);

            Chip(canvas, options,
                new Vector2(centre - (measured.X / 2f), bottom + 4f),
                text,
                value.Exalted >= options.HighlightExalted);

            // Both names. The tag shows the BASE for a unique and the item's
            // own name otherwise, while the projected route below knows only the
            // base — so recording one of them would let the same drop be drawn
            // twice.
            _tagged.Add(label.Text);

            if (item?.BaseName is { Length: > 0 } baseName) _tagged.Add(baseName);

            _stats.LootPriced++;
        }
    }

    /// <summary>
    /// The colour the game itself would write this name in.
    /// </summary>
    /// <remarks>
    /// Rarity, not a fixed parchment. The replacement is standing in for the
    /// game's own line, and the game colours that line by rarity — an orange
    /// unique name over a tag the client had drawn orange is the difference
    /// between a correction and a sticker.
    /// </remarks>
    private static uint RarityColour(ItemSnapshot? item, LootSettings options) => item?.Rarity switch
    {
        MonsterRarity.Unique => unchecked((uint)options.UniqueNameColour),
        MonsterRarity.Rare => Palette.Rare,
        MonsterRarity.Magic => Palette.Magic,
        MonsterRarity.Normal => Palette.Bone,
        _ => unchecked((uint)options.RevealColour),
    };

    /// <summary>
    /// The dropped item a tag belongs to, matched by the base name it prints.
    /// </summary>
    /// <remarks>
    /// Not by position, which was my mistake. I measured NPC nameplates — those
    /// do sit on their object to within a pixel — and generalised to loot tags,
    /// which do not: the game lays those out in a column so they never overlap,
    /// and a Hardwood Spear's tag was found 250px from the spear.
    ///
    /// The tag's text is the item's BASE NAME, so that is the join. It is also
    /// exactly what makes the unique case work: the game prints the base and the
    /// item knows the real name, which is the whole reason to draw one above the
    /// other. Position only breaks ties between two drops of the same base.
    /// </remarks>
    /// <summary>
    /// How far the view has travelled since the tag's rectangle was measured.
    /// </summary>
    /// <remarks>
    /// The snapshot carries the camera it was taken with, and the frame carries
    /// the camera being drawn with. Projecting the same unmoved item through
    /// both is the difference, in pixels, and adding it puts a stale rectangle
    /// where the game is drawing its tag right now.
    /// </remarks>
    private static Vector2 Drift(EntitySnapshot? owner, CameraSnapshot? then, CameraSnapshot? now)
    {
        if (owner?.WorldPosition is not { } world || then is null || now is null) return default;
        if (ReferenceEquals(then, now)) return default;

        var before = then.Project(world);
        var after = now.Project(world);

        if (before.Status == ScreenStatus.BehindCamera || after.Status == ScreenStatus.BehindCamera)
            return default;

        return new Vector2(after.Screen.X - before.Screen.X, after.Screen.Y - before.Screen.Y);
    }

    private static EntitySnapshot? OwnerEntity(WorldSnapshot snapshot, LootLabelSnapshot label)
    {
        EntitySnapshot? best = null;

        foreach (var entity in snapshot.Entities)
        {
            if (entity.Item is not { HasIdentity: true } item) continue;

            if (!string.Equals(item.BaseName, label.Text, StringComparison.OrdinalIgnoreCase))
                continue;

            best ??= entity;
        }

        return best;
    }

    private static float Distance(CameraSnapshot? camera, EntitySnapshot entity, LootLabelSnapshot label)
    {
        if (camera is null || entity.WorldPosition is not { } world) return float.MaxValue;

        var point = camera.Project(world);

        if (point.Status == ScreenStatus.BehindCamera) return float.MaxValue;

        var dx = label.CentreX - point.Screen.X;
        var dy = label.CentreY - point.Screen.Y;

        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// The projected chip, for the drops the game refuses to name.
    /// </summary>
    private void DrawProjected(
        RenderFrame frame, IOverlayCanvas canvas, WorldSnapshot snapshot, LootSettings options)
    {
        var camera = frame.MapFrame?.Camera ?? snapshot.Camera;

        if (camera is null) return;

        var bounds = frame.ClientBounds;
        var tagged = options.AnchorValuesToTags;

        foreach (var entity in snapshot.Entities)
        {
            if (entity.Item is not { HasIdentity: true } item) continue;

            // Skip only what the tag route ACTUALLY drew — not everything it
            // might have drawn.
            //
            // This is why the floor went silent. The client only draws a ground
            // label for loot within a short radius of the character, so most
            // drops in an area have no tag at all; measured live, four items on
            // the floor and not one tag among them. With anchoring on, the old
            // test stood down for every drop that was not an unidentified
            // unique, and handed them to a tag route that had nothing to draw
            // them on. Between the two of them, nothing was drawn.
            //
            // The set is filled one line after a chip is actually painted, so
            // asking it is asking what happened rather than what was configured.
            if (tagged && item.BaseName is { Length: > 0 } b && _tagged.Contains(b)) continue;

            if (Price(item) is not { } price) continue;

            // Same exception as the tag route above: a unique's identity is
            // worth surfacing on its own, floor or no floor.
            if (!item.IsUnique && price.Exalted < options.MinimumFor(price.Category, item.IsUnique)) continue;
            if (!options.ShowsCategory(price.Category)) continue;

            if (entity.WorldPosition is not { } world) continue;

            var point = camera.Project(world);

            if (point.Status != ScreenStatus.OnScreen) continue;

            var at = new Vector2(point.Screen.X, point.Screen.Y);

            if (at.X < bounds.X || at.X > bounds.Right || at.Y < bounds.Y || at.Y > bounds.Bottom) continue;

            // An unidentified unique gets its NAME revealed, because the game is
            // hiding it. An identified one already shows its name, so the value
            // alone is what is missing.
            // An unidentified unique gets its NAME revealed because the game is
            // hiding it. That is the one case where the name is the point.
            var text = _prices.Describe(
                price, price.Exalted, options.MinQuantity, item.IsUnidentifiedUnique);

            Chip(canvas, options, new Vector2(at.X, at.Y + 16f), text,
                price.Exalted >= options.HighlightExalted, centred: true);

            _stats.LootPriced++;
        }
    }

    /// <summary>
    /// A chip, not loose text.
    /// </summary>
    /// <remarks>
    /// The game draws its own loot tag with a filled backdrop and a border, and
    /// a bare string next to one is simply not seen — the first live sighting of
    /// this feature was a dim "0.12 ex" that read as nothing at all. Same
    /// construction as the game's tag, so it lands with the same weight.
    /// </remarks>
    /// <summary>
    /// Says "unique, and I do not know what it is worth" on the tag itself.
    /// </summary>
    /// <remarks>
    /// Deliberately not a number. Guessing one for a unique the price book has
    /// never heard of would be worse than the silence this replaces - the whole
    /// point is that the answer is unknown and the item is worth picking up
    /// anyway.
    /// </remarks>
    private static void Unpriced(
        IOverlayCanvas canvas, LootSettings options, LootLabelSnapshot label, Vector2 drift, Overlay.ScreenRect bounds)
    {
        var centre = label.CentreX + drift.X;
        var bottom = label.Bottom + drift.Y;

        if (label.Right + drift.X < bounds.X || label.X + drift.X > bounds.Right ||
            label.CentreY + drift.Y < bounds.Y || label.CentreY + drift.Y > bounds.Bottom) return;

        var text = T("unique - sem preco");
        var scale = options.TextScale;
        var size = canvas.MeasureText(text, scale);
        var colour = unchecked((uint)options.UnknownColour);

        var left = centre - (size.X / 2f);
        var top = bottom + 4f;

        var min = new Vector2(left - 5f, top - 3f);
        var max = new Vector2(left + size.X + 5f, top + size.Y + 3f);

        canvas.Rect(min, max, Palette.Shadow);
        canvas.Rect(min, max, colour, filled: false);
        canvas.Text(new Vector2(left, top), colour, text, scale);
    }

    private static void Chip(
        IOverlayCanvas canvas, LootSettings options, Vector2 at, string text, bool rich,
        bool centred = false)
    {
        var scale = options.TextScale;
        var size = canvas.MeasureText(text, scale);
        var colour = unchecked((uint)(rich ? options.RichColour : options.PricedColour));

        var left = centred ? at.X - (size.X / 2f) : at.X;
        var top = at.Y;

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
