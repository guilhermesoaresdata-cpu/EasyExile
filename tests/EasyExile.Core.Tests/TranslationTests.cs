using System.Text.RegularExpressions;
using EasyExile.Radar.Settings.General;
using EasyExile.Radar.UI;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// The interface, complete in both languages.
/// </summary>
/// <remarks>
/// "One hundred percent" is a claim, and a claim about three hundred strings is
/// one nobody can check by reading. So this reads the overlay's own source —
/// the panel and everything it draws over the game — pulls out every phrase
/// it can say, and fails on any one the table has not got —
/// which turns the promise into a thing that stays true as the panel grows.
///
/// It also fails the other way, on entries the panel no longer says, because a
/// table that accumulates dead phrases stops being reviewable and the next
/// missing one hides in the noise.
/// </remarks>
public class TranslationTests
{
    /// <summary>Every phrase the panel passes through the translator.</summary>
    private static readonly Regex Spoken = new(@"T\(""([^""]+)""\)", RegexOptions.Compiled);

    [Fact]
    public void Every_phrase_the_panel_says_has_an_english_form()
    {
        var missing = Phrases()
            .Where(phrase => !Text.Knows(phrase))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} sem traducao:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", missing.Take(20)));
    }

    [Fact]
    public void The_table_holds_nothing_the_panel_no_longer_says()
    {
        // A table that only grows stops being reviewable, and the next real gap
        // hides among the dead entries.
        var spoken = Phrases()
            .Select(p => p.Split("##")[0])
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        // The dropdown option lists are the one thing the scan cannot see:
        // they are plain string arrays translated where they are drawn, so
        // no T("...") sits next to them to be found.
        foreach (var elsewhere in new[]
                 {
                     "inferior esquerdo", "inferior direito", "superior esquerdo",
                     "superior direito", "acima", "abaixo",
                     "Ao lado da skill", "Livre (voce escolhe)", "Centro (topo)",
                     "Canto superior esquerdo", "Canto superior direito",
                     "Canto inferior esquerdo", "Canto inferior direito",

                     // Said as T(variable) - a swatch tooltip and a build-tag
                     // checkbox both draw a label out of a table, so no
                     // T("...") sits in the source to be found.
                     "branco", "pergaminho", "azul", "ciano", "verde",
                     "amarelo", "laranja", "vermelho", "rosa", "roxo",
                     "Minion (Mi)", "Projectile (Pj)", "Spell (Sp)", "Attack (At)",
                     "Fire (Fi)", "Cold (Co)", "Lightning (Li)", "Chaos (Ch)",
                     "Life (HP)", "Energy Shield (ES)", "Spirit (Sr)", "Crit (Cr)",
                     "Fogo (F)", "Frio (C)", "Raio (L)", "Caos (X)",
                 })
            spoken.Add(elsewhere);

        var orphans = Text.English.Keys
            .Where(key => !spoken.Contains(key))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            $"{orphans.Count} no dicionario que o painel nao diz mais:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", orphans.Take(20)));
    }

    [Fact]
    public void Nothing_is_translated_into_itself_by_accident()
    {
        // A good few are legitimately identical - HUD, Debug, terrain, dpi, map
        // frame - and the share of them is what matters rather than the count,
        // because the count grows with the table while pasted-across Portuguese
        // would not stay a small fraction of it.
        var identical = Text.English
            .Where(e => string.Equals(e.Key, e.Value, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            identical.Count * 5 < Text.English.Count,
            $"{identical.Count} de {Text.English.Count} entradas iguais nos dois idiomas");
    }

    [Fact]
    public void An_imgui_identity_survives_translation()
    {
        // A label is two things: the words, and the widget's identity after the
        // hashes. Translating the identity too would give the widget a new one
        // in each language, losing its state on every switch.
        Text.Current = Language.English;

        try
        {
            Assert.Equal("Enabled##loot", Text.T("Ativado##loot"));
            Assert.Equal("##devtreefind", Text.T("##devtreefind"));
        }
        finally
        {
            Text.Current = Language.PtBr;
        }
    }

    [Fact]
    public void Portuguese_is_returned_untouched()
    {
        Text.Current = Language.PtBr;

        Assert.Equal("Ativado##loot", Text.T("Ativado##loot"));
        Assert.Equal("Marcas nos itens", Text.T("Marcas nos itens"));
    }

    [Fact]
    public void An_unknown_phrase_still_renders()
    {
        // Visibly wrong to a reader expecting English beats blank or crashed,
        // and the first test above is what keeps it from lasting.
        Text.Current = Language.English;

        try
        {
            Assert.Equal("frase que ninguem traduziu", Text.T("frase que ninguem traduziu"));
        }
        finally
        {
            Text.Current = Language.PtBr;
        }
    }

    [Fact]
    public void The_panel_draws_no_words_that_skipped_the_translator()
    {
        // The other guard reads what is inside T("..."), which by construction
        // cannot see a string nobody wrapped - and those were most of what
        // stayed Portuguese under English: the colour legend, the campaign
        // readouts, every row of the diagnostics table.
        //
        // So this reads the opposite side: every literal in the panel's source
        // that is NOT wrapped. Units and format specifiers survive the filter
        // below; words do not, and a new one has to be wrapped to get in.
        var panel = ContractIsolationTests
            .SourceFiles(Path.Combine("src", "EasyExile.Radar"))
            .Single(f => f.EndsWith("SettingsWindow.cs", StringComparison.OrdinalIgnoreCase));

        var bare = Literals(File.ReadAllText(panel))
            .Where(HasWords)
            // A phrase the table knows is translated somewhere, even when the
            // T() is elsewhere: a swatch tooltip and a dropdown's options are
            // drawn from a table through a variable.
            .Where(text => !Text.Knows(text))
            .Where(text => !Allowed.Contains(text))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            bare.Count == 0,
            $"{bare.Count} literais desenhados sem T():{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", bare.Take(20)));
    }

    /// <summary>Literals the panel does not translate and should not.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        // A folder on disk, not a word on screen.
        "prints",

        // The language picker names each language in its own language, so
        // neither reader has to recognise the other's word for theirs.
        "Portugues (BR)", "English",
    };

    /// <summary>A word is four letters or more; shorter runs are units.</summary>
    /// <remarks>
    /// "fps", "ms", "Hz", "us", "px" and "x" trail interpolated numbers and read
    /// the same in both languages. Four is where the units stop and the
    /// sentences start, and it keeps this from arguing about "de" while missing
    /// "categoria desligada".
    /// </remarks>
    private static bool HasWords(string text) => Words.IsMatch(Holes.Replace(text, " "));

    private static readonly Regex Words = new(@"\p{L}{4,}", RegexOptions.Compiled);

    /// <summary>
    /// The expressions inside an interpolated string, which are code.
    /// </summary>
    /// <remarks>
    /// $"{stats.TerrainWidth} x {stats.TerrainHeight}" draws two numbers and an
    /// x. Read whole it looks full of words - TerrainWidth is a word - and
    /// every stats line in the panel would be reported as untranslated.
    /// </remarks>
    private static readonly Regex Holes = new(@"\{[^}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// Every string literal in a C# file that is not already wrapped in T().
    /// </summary>
    /// <remarks>
    /// Comments are skipped because they hold prose nobody draws, and an
    /// interpolated string is read whole: the words around the braces are the
    /// part that needs translating.
    /// </remarks>
    private static IEnumerable<string> Literals(string source)
    {
        for (var at = 0; at < source.Length; at++)
        {
            var c = source[at];

            if (c == '/' && at + 1 < source.Length && source[at + 1] == '/')
            {
                while (at < source.Length && source[at] != '\u000A') at++;

                continue;
            }

            if (c != '"') continue;

            var verbatim = at > 0 && source[at - 1] == '@';
            var head = source[Math.Max(0, at - 4)..at];
            var wrapped = head.EndsWith("T(", StringComparison.Ordinal)
                || head.EndsWith("T($", StringComparison.Ordinal)
                || head.EndsWith("T(@", StringComparison.Ordinal);

            var start = at + 1;
            var end = start;

            while (end < source.Length && source[end] != '"' && source[end] != '\u000A')
                end += source[end] == '\\' && !verbatim ? 2 : 1;

            var body = source[start..Math.Min(end, source.Length)];

            at = end;

            if (wrapped || body.StartsWith("##", StringComparison.Ordinal)) continue;

            yield return body;
        }
    }

    private static IEnumerable<string> Phrases()
    {
        // Every file in the overlay, not just the panel: the levelling guide
        // draws its own text over the game - "Matar: ", "Ir para " - and the
        // first version of this looked only at SettingsWindow, so those went
        // untranslated with the tests all green.
        return ContractIsolationTests
            .SourceFiles(Path.Combine("src", "EasyExile.Radar"))
            .SelectMany(file => Spoken.Matches(File.ReadAllText(file)))
            // Source text, not runtime text: a combo's options are one string
            // with NUL between them, written in the source as an escape. Read
            // literally it would never match the key the panel actually asks
            // for, and the phrase would look missing in both directions.
            .Select(m => m.Groups[1].Value.Replace("\\0", "\0"));
    }
}
