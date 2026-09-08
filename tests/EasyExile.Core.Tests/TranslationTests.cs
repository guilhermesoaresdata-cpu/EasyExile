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
        // A few are legitimately identical - HUD, Debug, Mana - and the rest
        // being identical would mean somebody pasted the Portuguese across.
        var identical = Text.English
            .Where(e => string.Equals(e.Key, e.Value, StringComparison.Ordinal))
            .ToList();

        Assert.True(identical.Count < 25, $"{identical.Count} entradas iguais nos dois idiomas");
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

    private static IEnumerable<string> Phrases()
    {
        // Every file in the overlay, not just the panel: the levelling guide
        // draws its own text over the game - "Matar: ", "Ir para " - and the
        // first version of this looked only at SettingsWindow, so those went
        // untranslated with the tests all green.
        return ContractIsolationTests
            .SourceFiles(Path.Combine("src", "EasyExile.Radar"))
            .SelectMany(file => Spoken.Matches(File.ReadAllText(file)))
            .Select(m => m.Groups[1].Value);
    }
}
