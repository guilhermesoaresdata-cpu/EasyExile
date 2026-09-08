namespace EasyExile.Core.Snapshots;

/// <summary>
/// What the UI sweep remembers between calls: when it last ran, what it found,
/// and the per-type caches its entity reads use.
/// </summary>
/// <remarks>
/// Per session, never static, for the same reason the capture caches are: these
/// key on addresses, so two readers sharing them read each other's answers.
/// </remarks>
internal sealed class UiSweepState
{
    public DateTime SweptAt { get; set; } = DateTime.MinValue;

    public UiSnapshot? Last { get; set; }

    /// <summary>
    /// Whether the sweep also collects the captions the client is drawing.
    /// </summary>
    /// <remarks>
    /// Set by whoever owns the session, because only they know if anything
    /// wants them. It costs two crossings per visible node, which is a fair
    /// price for a feature that is switched on and a waste for one that is not.
    /// </remarks>
    public bool ReadLabels { get; set; }

    public CaptureCaches Caches { get; } = new();

    /// <summary>
    /// The tags and the elements they came from, published for the render lane.
    /// </summary>
    /// <remarks>
    /// Written by the sweep thread and read by the render thread, so it is
    /// swapped whole rather than mutated: a reader either sees the previous
    /// sweep's tags or this one's, never half of each with rectangles that
    /// belong to neither.
    /// </remarks>
    public LabelTargets Targets
    {
        get => System.Threading.Volatile.Read(ref _targets);
        set => System.Threading.Volatile.Write(ref _targets, value);
    }

    private LabelTargets _targets = LabelTargets.Empty;
}
