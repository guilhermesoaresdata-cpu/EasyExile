using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;

namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// Smooths a monster's own motion between world captures.
/// </summary>
/// <remarks>
/// The map, the player, the pan and the zoom are never smoothed — those come
/// from the fast frame and are already current on every rendered frame. The only
/// thing arriving at 30 Hz is where a monster has walked to, and that is the one
/// thing this interpolates.
///
/// A monster standing still while the player walks past it therefore still
/// slides across the map perfectly smoothly: its world position is not moving,
/// and the map underneath it is recomputed at render rate. Only a monster that
/// is itself moving is interpolated at all.
///
/// Rendering trails one world tick behind the newest sample. That is the price
/// of having two real samples to interpolate between instead of extrapolating
/// past the data — about 33 ms, against a marker that visibly jumps thirty times
/// a second.
/// </remarks>
public sealed class EntityMotion
{
    /// <summary>Two consecutive samples, and when each of them arrived.</summary>
    private struct Track
    {
        public Vector3 Previous;
        public Vector3 Current;
        public DateTimeOffset PreviousAt;
        public DateTimeOffset CurrentAt;
        public DateTimeOffset SeenAt;
    }

    private readonly Dictionary<EntityId, Track> _tracks = new();

    private long _epoch = -1;

    /// <summary>
    /// World distance a monster cannot cover in one tick. Beyond it the movement
    /// was a teleport, a leap, or an address the client recycled onto something
    /// else; sliding a marker across that gap would draw a path nothing walked.
    /// </summary>
    private static readonly float TeleportWorld = 40f * NativeMapProjection.GridToWorld;

    /// <summary>
    /// Below this the two samples are one position with read noise on top.
    /// Interpolating it would add jitter to something standing still.
    /// </summary>
    private const float StillWorld = 0.5f;

    /// <summary>
    /// Longest tick worth smoothing.
    /// </summary>
    /// <remarks>
    /// Reported live: with the world walk at 6-7 Hz the smoothing made monsters
    /// look SLOWER, not smoother. A 150 ms tick turns into a quick slide
    /// followed by a 100 ms pause, and the eye reads the slide as the marker
    /// taking longer to arrive. A hard step at least arrives at once.
    ///
    /// So smoothing only applies when captures are close enough together that
    /// the interpolated segment is short — around 30-60 Hz. Slower than that and
    /// the raw position is the better answer.
    /// </remarks>
    private static readonly TimeSpan MaxGap = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// The most the render is allowed to trail the newest sample.
    /// </summary>
    /// <remarks>
    /// At the intended 30 Hz this is never reached: a tick is 33 ms and the
    /// marker glides across the whole of it. It matters when captures come in
    /// slower — measured live, a busy area walks 512 entities at about 6 Hz —
    /// where trailing a whole 166 ms tick would trade one visible problem for
    /// another. Capped, the marker glides for the first 50 ms and then holds at
    /// the newest known position, which is late by 50 ms rather than by a
    /// sixth of a second.
    /// </remarks>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>Tracks that stop appearing are dropped after this.</summary>
    private static readonly TimeSpan ForgetAfter = TimeSpan.FromSeconds(1);

    /// <summary>Markers whose position this frame came from interpolation.</summary>
    public int Interpolated { get; private set; }

    /// <summary>Markers placed on the raw captured position, with no smoothing.</summary>
    public int Snapped { get; private set; }

    public int Tracked => _tracks.Count;

    /// <summary>Opens a frame. The counters describe the frame just opened.</summary>
    public void BeginFrame()
    {
        Interpolated = 0;
        Snapped = 0;
    }

    /// <summary>
    /// Where an entity should be drawn at this instant.
    /// </summary>
    /// <param name="epoch">Area identity. A change drops every track.</param>
    /// <param name="capturedAt">When the world snapshot holding this position was taken.</param>
    /// <param name="now">Render-frame time.</param>
    /// <param name="smooth">False renders the raw captured position.</param>
    public Vector3 Position(
        long epoch, EntityId id, Vector3 world,
        DateTimeOffset capturedAt, DateTimeOffset now, bool smooth)
    {
        // Anything keyed on an EntityId dies with the area: after a transition
        // that id is a different creature at a reused address, and interpolating
        // between the two would fly a marker across the new map.
        if (epoch != _epoch)
        {
            _epoch = epoch;
            _tracks.Clear();
        }

        if (!smooth)
        {
            Snapped++;
            return world;
        }

        if (!_tracks.TryGetValue(id, out var track))
        {
            // First sighting. There is no earlier sample to come from, and
            // interpolating from a default would fly the marker in from the
            // origin of the map.
            _tracks[id] = new Track
            {
                Previous = world,
                Current = world,
                PreviousAt = capturedAt,
                CurrentAt = capturedAt,
                SeenAt = now,
            };

            Snapped++;
            return world;
        }

        track.SeenAt = now;

        if (capturedAt > track.CurrentAt)
        {
            // A new capture arrived: what was current becomes the past.
            track.Previous = track.Current;
            track.PreviousAt = track.CurrentAt;
            track.Current = world;
            track.CurrentAt = capturedAt;
        }
        else
        {
            // Same capture, another frame. Nothing new to learn from it.
            track.Current = world;
        }

        _tracks[id] = track;

        var span = track.CurrentAt - track.PreviousAt;

        if (span <= TimeSpan.Zero || span > MaxGap)
        {
            Snapped++;
            return track.Current;
        }

        var dx = track.Current.X - track.Previous.X;
        var dy = track.Current.Y - track.Previous.Y;

        var moved = MathF.Sqrt((dx * dx) + (dy * dy));

        if (moved < StillWorld || moved > TeleportWorld)
        {
            Snapped++;
            return track.Current;
        }

        // Render behind the newest sample, so the marker always sits between two
        // positions the monster actually occupied rather than somewhere it has
        // been extrapolated to.
        var delay = span < MaxDelay ? span : MaxDelay;

        var t = (float)((now - delay - track.PreviousAt) / span);

        if (t >= 1f)
        {
            // The next capture is late. Hold at the last real position rather
            // than guessing where the monster went on from there.
            Snapped++;
            return track.Current;
        }

        Interpolated++;

        if (t <= 0f) return track.Previous;

        return new Vector3(
            track.Previous.X + (dx * t),
            track.Previous.Y + (dy * t),
            track.Previous.Z + ((track.Current.Z - track.Previous.Z) * t));
    }

    /// <summary>
    /// Drops tracks for entities that stopped appearing. Without it a monster
    /// killed off screen would hold a track until the area changed.
    /// </summary>
    public void Forget(DateTimeOffset now)
    {
        if (_tracks.Count == 0) return;

        // This runs every frame: no LINQ, and no allocation unless something
        // actually expired.
        List<EntityId>? stale = null;

        foreach (var (id, track) in _tracks)
        {
            if (now - track.SeenAt <= ForgetAfter) continue;

            stale ??= new List<EntityId>();
            stale.Add(id);
        }

        if (stale is null) return;

        foreach (var id in stale) _tracks.Remove(id);
    }
}
