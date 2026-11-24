using BookFinder.Application.Matching;

namespace BookFinder.Tests.Unit;

public class AuthorMatcherTests
{
    [Theory]
    [InlineData("Tolkien", "J. R. R. Tolkien")]
    [InlineData("J.R.R. Tolkien", "J. R. R. Tolkien")]
    [InlineData("John Ronald Reuel Tolkien", "J.R.R. Tolkien")]
    [InlineData("Tolkien, J. R. R.", "J.R.R. Tolkien")]
    [InlineData("J.K. Rowling", "J. K. Rowling")]
    [InlineData("Victor Hugo", "Victor Hugo")]
    [InlineData("victor hugo", "Víctor Hugo")]
    public void Matches_name_variants(string query, string candidate) =>
        Assert.True(AuthorMatcher.Matches(query, candidate));

    [Theory]
    [InlineData("Charles Dickens", "Monica Dickens")]
    [InlineData("Mark Twain", "Mark Haddon")]
    [InlineData("Dixon", "J.R.R. Tolkien")]
    [InlineData("", "J.R.R. Tolkien")]
    [InlineData(null, null)]
    public void Rejects_different_people(string? query, string? candidate) =>
        Assert.False(AuthorMatcher.Matches(query, candidate));
}
