using EasyExile.Core.Camera;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Runtime;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;

namespace EasyExile.Core.Tests;

/// <summary>
/// Build A of the contract against build B of the client. The required outcome
/// is refusal — never a warning followed by reads, which would produce numbers
/// that look like game state and are not.
/// </summary>
public class BuildMismatchTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "poe2client-build-" + Guid.NewGuid().ToString("N")[..8]);

    public BuildMismatchTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes a minimal but structurally real PE image with one .text section.</summary>
    private string WritePe(string name, uint timestamp, uint imageSize, byte codeFill)
    {
        const int peOffset = 0x80;
        const int optionalHeaderSize = 240;
        const int sectionTable = peOffset + 4 + 20 + optionalHeaderSize;
        const int codeStart = 0x400;
        const int codeSize = 0x200;

        var image = new byte[codeStart + codeSize];

        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BitConverter.GetBytes(peOffset).CopyTo(image, 0x3C);

        BitConverter.GetBytes(0x00004550u).CopyTo(image, peOffset);
        BitConverter.GetBytes((ushort)0x8664).CopyTo(image, peOffset + 4);      // machine
        BitConverter.GetBytes((ushort)1).CopyTo(image, peOffset + 6);           // section count
        BitConverter.GetBytes(timestamp).CopyTo(image, peOffset + 8);
        BitConverter.GetBytes((ushort)optionalHeaderSize).CopyTo(image, peOffset + 4 + 16);

        var optionalHeader = peOffset + 4 + 20;
        BitConverter.GetBytes(imageSize).CopyTo(image, optionalHeader + 56);

        ".text\0\0\0"u8.ToArray().CopyTo(image, sectionTable);
        BitConverter.GetBytes((uint)codeSize).CopyTo(image, sectionTable + 16);  // raw size
        BitConverter.GetBytes((uint)codeStart).CopyTo(image, sectionTable + 20); // raw pointer

        for (int i = 0; i < codeSize; i++) image[codeStart + i] = codeFill;

        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, image);
        return path;
    }

    [Fact]
    public void A_client_from_a_different_build_is_refused_outright()
    {
        var other = WritePe("PathOfExile.exe", 0xDEADBEEF, 0x04C00000, 0x90);

        var refusal = Assert.Throws<BuildMismatchException>(() => GameSession.RequireMatchingBuild(other));

        Assert.False(refusal.Check.Matches);
        Assert.Contains(GameLayout.Build.Fingerprint, refusal.Check.Detail);
        Assert.Contains(refusal.Check.Client.Fingerprint, refusal.Check.Detail);
        Assert.NotEqual(GameLayout.Build.Fingerprint, refusal.Check.Client.Fingerprint);
    }

    [Fact]
    public void There_is_no_way_to_continue_past_a_mismatch()
    {
        // If an override ever appeared, this is where it would show up.
        var members = typeof(GameSession)
            .GetMethods()
            .Select(m => m.Name)
            .Concat(typeof(BuildGate).GetMethods().Select(m => m.Name))
            .ToArray();

        Assert.DoesNotContain(members, n =>
            n.Contains("Force", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Ignore", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Override", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Unsafe", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            typeof(GameSession).GetMethods().Concat(typeof(BuildGate).GetMethods()),
            m => m.GetParameters().Any(p => p.ParameterType == typeof(bool)));
    }

    [Fact]
    public void A_patched_code_section_alone_is_enough_to_refuse()
    {
        // Same header, different code: the offsets describe the code, so this is
        // exactly the case a header-only check would wave through.
        var first = WritePe("A.exe", 0x6A8292EE, 0x04C23000, 0x90);
        var second = WritePe("B.exe", 0x6A8292EE, 0x04C23000, 0xCC);

        var a = BuildGate.Fingerprint(first);
        var b = BuildGate.Fingerprint(second);

        Assert.Equal(a.PeTimestamp, b.PeTimestamp);
        Assert.Equal(a.ImageSize, b.ImageSize);
        Assert.NotEqual(a.TextSha256, b.TextSha256);
        Assert.NotEqual(
            BuildGate.StableId("PathOfExile.exe", a.PeTimestamp, a.ImageSize, a.TextSha256),
            BuildGate.StableId("PathOfExile.exe", b.PeTimestamp, b.ImageSize, b.TextSha256));
    }

    [Fact]
    public void A_client_whose_identity_matches_the_contract_is_accepted()
    {
        // Reconstructing the contract fingerprint from a synthesised image is not
        // possible, so acceptance is checked at the formula: the identity the
        // contract carries must be the identity the gate computes.
        var recomputed = BuildGate.StableId(
            "PathOfExile.exe",
            GameLayout.Build.PeTimestamp,
            GameLayout.Build.ImageSize,
            GameLayout.Build.TextSha256);

        Assert.Equal(GameLayout.Build.Fingerprint, recomputed);
    }

    [Fact]
    public void Something_that_is_not_a_pe_image_is_rejected_rather_than_fingerprinted()
    {
        var garbage = Path.Combine(_directory, "notape.exe");
        File.WriteAllBytes(garbage, new byte[0x400]);

        Assert.Throws<InvalidDataException>(() => BuildGate.Fingerprint(garbage));
    }
}
