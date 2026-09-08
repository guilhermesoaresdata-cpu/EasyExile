namespace EasyExile.Core.Spatial;

/// <summary>A position in client world units.</summary>
public readonly record struct Vector3(float X, float Y, float Z);

/// <summary>A point on the grid, or on the screen, depending on the caller.</summary>
public readonly record struct Vector2(float X, float Y);
