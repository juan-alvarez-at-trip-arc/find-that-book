using FindThatBook.Application.Text;

namespace FindThatBook.Tests;

public class NormalizerTests
{
    [Theory]
    [InlineData("  The  Hobbit!! ", "the hobbit")]
    [InlineData("Gabriel García Márquez", "gabriel garcia marquez")]
    [InlineData("cien años de soledad", "cien anos de soledad")]
    [InlineData("D'Artagnan", "dartagnan")]
    [InlineData("Ender's Game", "enders game")]
    [InlineData("O’Brien", "obrien")]
    [InlineData("???", "")]
    [InlineData(null, "")]
    public void Normalize_lowercases_and_strips_diacritics_and_punctuation(string? input, string expected)
        => Assert.Equal(expected, Normalizer.Normalize(input));

    [Theory]
    [InlineData("The Hobbit, or There and Back Again", "The Hobbit")]
    [InlineData("Dune: Deluxe Edition", "Dune")]
    [InlineData("Emma - Illustrated", "Emma")]
    [InlineData("Dracula (Annotated)", "Dracula")]
    [InlineData("Emma", "Emma")]
    public void StripSubtitle_removes_subtitles_and_parentheticals(string input, string expected)
        => Assert.Equal(expected, Normalizer.StripSubtitle(input).Trim());

    [Theory]
    [InlineData("The Hobbit, or There and Back Again", "hobbit")]
    [InlineData("A Tale of Two Cities", "tale of two cities")]
    [InlineData("An American Tragedy", "american tragedy")]
    [InlineData("Hobbit", "hobbit")]
    public void TitleKey_ignores_subtitle_and_leading_article(string input, string expected)
        => Assert.Equal(expected, Normalizer.TitleKey(input));

    [Theory]
    [InlineData("J.R.R. Tolkien", "jrr tolkien")]
    [InlineData("jrr tolkien", "jrr tolkien")]
    [InlineData("C. S. Lewis", "cs lewis")]
    [InlineData("Mark Twain", "mark twain")]
    public void NormalizeName_merges_initials(string input, string expected)
        => Assert.Equal(expected, Normalizer.NormalizeName(input));
}
