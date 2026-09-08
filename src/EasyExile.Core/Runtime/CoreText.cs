namespace EasyExile.Core.Runtime;

/// <summary>
/// Every string the Core can put in front of a person, in one place.
/// </summary>
/// <remarks>
/// This is not a localization framework and is deliberately not one yet. It is
/// the structure a framework would need: no user-facing literal is scattered
/// through the code, so swapping this type for resource lookups later is a
/// contained change rather than a sweep. Intended default is pt-BR; code,
/// namespaces and technical comments stay in English.
/// </remarks>
internal static class CoreText
{
    public const string BuildMatches = "cliente e contrato descrevem a mesma build";

    public static string BuildMismatch(string contract, string client) =>
        $"o contrato foi provado em {contract}, este cliente e {client}";

    public const string ClientModuleUnreadable = "nao foi possivel ler o modulo do cliente";

    public static string ProcessOpenFailed(int processId) =>
        $"nao foi possivel abrir o processo {processId}; rode como administrador";

    public const string PeTooSmall = "executavel pequeno demais para ser uma imagem PE";
    public const string PeHeaderOffsetInvalid = "offset do cabecalho PE invalido";
    public const string PeSignatureMissing = "assinatura PE nao encontrada";
    public const string PeTextSectionOverruns = "secao .text excede o tamanho do arquivo";
    public const string PeTextSectionMissing = "secao .text nao encontrada";

    public static string BuildMismatchRefusal(string detail) => $"OFFSETS BUILD MISMATCH: {detail}";
}
