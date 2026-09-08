using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;

namespace EasyExile.Core.Camera;

/// <summary>
/// Reads the camera out of the running client. The maths lives in
/// <see cref="Projection"/>; this type only supplies it with the matrix and the
/// viewport the contract describes.
/// </summary>
internal sealed class GameCamera
{
    private readonly IMemoryReader _memory;
    private readonly nint _camera;

    private GameCamera(IMemoryReader memory, nint camera)
    {
        _memory = memory;
        _camera = camera;
    }

    public nint Address => _camera;

    public static GameCamera? Resolve(IMemoryReader memory, nint inGameState)
    {
        if (!memory.TryReadPointer(inGameState + GameLayout.Roots.Camera, out var camera)) return null;

        // The span comes from the fields actually read, not from a round number
        // someone picked; see GameLayout.View.RequiredReadableSpan.
        if (!GameEntity.IsPlausibleAddress(camera) ||
            !memory.IsReadable(camera, GameLayout.View.RequiredReadableSpan))
            return null;

        return new GameCamera(memory, camera);
    }

    public (int Width, int Height)? Viewport()
    {
        if (!_memory.TryRead<int>(_camera + GameLayout.View.Viewport, out var width) ||
            !_memory.TryRead<int>(_camera + GameLayout.View.Viewport + 4, out var height))
            return null;

        return width is > 320 and < 16000 && height is > 240 and < 10000 ? (width, height) : null;
    }

    public float[]? ViewProjection()
    {
        if (!_memory.TryReadBytes(_camera + GameLayout.View.ViewProjection,
                GameLayout.View.ViewProjectionSize, out var bytes))
            return null;

        var matrix = new float[16];
        for (int i = 0; i < 16; i++)
        {
            matrix[i] = BitConverter.ToSingle(bytes, i * 4);
            if (!float.IsFinite(matrix[i])) return null;
        }

        return matrix;
    }

    public ScreenPoint WorldToScreen(Vector3 world)
    {
        var matrix = ViewProjection();
        var viewport = Viewport();

        if (matrix is null || viewport is not { } size)
            return new ScreenPoint(ScreenStatus.Invalid, default, default, 0);

        return Projection.Project(matrix, world, size.Width, size.Height);
    }

    /// <summary>
    /// Kept as the entry point the foundation tests exercise. The implementation
    /// is <see cref="Projection.Project"/>, so the live path and the captured
    /// path cannot drift apart.
    /// </summary>
    public static ScreenPoint Project(float[] matrix, Vector3 world, float width, float height) =>
        Projection.Project(matrix, world, width, height);
}
