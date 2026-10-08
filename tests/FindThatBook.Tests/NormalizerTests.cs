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
    [InlineData("Dostoevsky", "Dostoyevsky")]
    [InlineData("Dostoevsky", "Dostoievski")]
    [InlineData("Dostoevsky", "Dostoevskij")]
    [InlineData("Dostoevsky", "Dostoïevski")]
    [InlineData("Fyodor Dostoevsky", "Fiódor Dostoievski")]
    [InlineData("Tolstoy", "Tolstoi")]
    [InlineData("Tolstoy", "Tolstoj")]
    [InlineData("Tchaikovsky", "Chaikovsky")]
    [InlineData("Tchaikovsky", "Tchaikovski")]
    [InlineData("J.R.R. Tolkien", "jrr tolkien")]
    [InlineData("Murakami, Haruki", "Haruki Murakami")]
    public void NameKey_treats_transliteration_variants_of_a_name_as_equal(string a, string b)
        => Assert.Equal(
            string.Join(' ', Normalizer.NameTokens(a).Order()),
            string.Join(' ', Normalizer.NameTokens(b).Order()));

    [Theory]
    [InlineData("Tolkien", "Tolstoy")]
    [InlineData("Dostoevsky", "Doyle")]
    [InlineData("Mark Twain", "Mark Twin")]
    [InlineData("Austen", "Austin")]
    public void NameKey_keeps_different_names_apart(string a, string b)
        => Assert.NotEqual(
            string.Join(' ', Normalizer.NameTokens(a).Order()),
            string.Join(' ', Normalizer.NameTokens(b).Order()));

    [Fact]
    public void NameTokens_is_empty_for_a_blank_name()
    {
        Assert.Empty(Normalizer.NameTokens(null));
        Assert.Empty(Normalizer.NameTokens("   "));
        Assert.Empty(Normalizer.NameTokens("???"));
    }

    [Theory]
    [InlineData("J.R.R. Tolkien", "jrr tolkien")]
    [InlineData("jrr tolkien", "jrr tolkien")]
    [InlineData("C. S. Lewis", "cs lewis")]
    [InlineData("Mark Twain", "mark twain")]
    public void NormalizeName_merges_initials(string input, string expected)
        => Assert.Equal(expected, Normalizer.NormalizeName(input));
}
