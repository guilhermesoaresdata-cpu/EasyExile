namespace EasyExile.Core.World;

/// <summary>
/// One block of the Life component. Public because it travels out inside
/// snapshots; it carries values, never an address.
/// </summary>
public sealed record Vital(string Name, int Id, int Current, int Max);
