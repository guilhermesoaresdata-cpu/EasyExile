using System.Security.Cryptography;
using System.Text;
using EasyExile.Core.Contract;

namespace EasyExile.Core.Runtime;

public sealed record ClientBuild(
    string ExecutableName, string Fingerprint, uint PeTimestamp, uint ImageSize, string TextSha256);

public sealed record BuildCheck(bool Matches, ClientBuild Client, string Contract, string Detail);

/// <summary>
/// Fingerprints the installed client and refuses to run against a build the
/// contract was not proven on. Continuing with a warning would read one build's
/// structures at another build's offsets, which yields confident nonsense rather
/// than an error.
/// </summary>
public static class BuildGate
{
    public static BuildCheck Check(string executablePath)
    {
        var client = Fingerprint(executablePath);
        var contract = GameLayout.Build.Fingerprint;

        var matches = string.Equals(client.Fingerprint, contract, StringComparison.Ordinal);

        var detail = matches
            ? CoreText.BuildMatches
            : CoreText.BuildMismatch(contract, client.Fingerprint);

        return new BuildCheck(matches, client, contract, detail);
    }

    /// <summary>
    /// PE header identity plus a hash of the code section. The header alone is
    /// not enough — the code is what the offsets actually describe.
    /// </summary>
    public static ClientBuild Fingerprint(string executablePath)
    {
        using var stream = File.OpenRead(executablePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (stream.Length < 0x100)
            throw new InvalidDataException(CoreText.PeTooSmall);

        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset + 0x100 > stream.Length)
            throw new InvalidDataException(CoreText.PeHeaderOffsetInvalid);

        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException(CoreText.PeSignatureMissing);

        _ = reader.ReadUInt16();
        var sectionCount = reader.ReadUInt16();
        var timestamp = reader.ReadUInt32();

        stream.Position = peOffset + 4 + 16;
        var optionalHeaderSize = reader.ReadUInt16();

        var optionalHeaderStart = peOffset + 4 + 20;
        stream.Position = optionalHeaderStart + 56;
        var imageSize = reader.ReadUInt32();

        var sectionTable = optionalHeaderStart + optionalHeaderSize;
        string? textHash = null;

        for (var i = 0; i < sectionCount; i++)
        {
            stream.Position = sectionTable + (i * 40L);

            var name = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0');

            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();

            if (!string.Equals(name, ".text", StringComparison.Ordinal)) continue;
            if ((long)rawPointer + rawSize > stream.Length)
                throw new InvalidDataException(CoreText.PeTextSectionOverruns);

            stream.Position = rawPointer;
            textHash = Convert.ToHexString(SHA256.HashData(reader.ReadBytes(checked((int)rawSize))));
            break;
        }

        if (textHash is null) throw new InvalidDataException(CoreText.PeTextSectionMissing);

        var executableName = Path.GetFileName(executablePath);

        return new ClientBuild(
            executableName,
            StableId(executableName, timestamp, imageSize, textHash),
            timestamp, imageSize, textHash);
    }

    /// <summary>
    /// Must match the identity the contract was stamped with, byte for byte.
    /// A different formula here would make every client look like a mismatch.
    /// </summary>
    public static string StableId(string executableName, uint timestamp, uint imageSize, string textSha256)
    {
        var raw = $"{executableName}|{timestamp:X8}|{imageSize:X8}|{textSha256}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24];
    }
}
