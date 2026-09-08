using EasyExile.Radar.Rendering;

namespace EasyExile.Radar.Features;

/// <summary>
/// One visual module.
/// </summary>
/// <remarks>
/// A feature receives a <see cref="RenderFrame"/> and an
/// <see cref="IOverlayCanvas"/>, which between them contain no way to reach the
/// client: everything it can draw comes from a snapshot somebody else captured.
///
/// Drawing settings is deliberately not part of this interface. The settings
/// panel is ImGui and lives in UI/; keeping it out here is what lets a feature
/// be tested against a recording canvas with no device, no window and no
/// immediate-mode context.
/// </remarks>
public interface IRadarFeature
{
    /// <summary>Category name, matching the settings category it reads.</summary>
    string Name { get; }

    bool Enabled { get; }

    void Draw(RenderFrame frame, IOverlayCanvas canvas);
}
