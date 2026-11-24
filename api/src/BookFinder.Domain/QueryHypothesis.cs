namespace BookFinder.Domain;

/// <summary>
/// One reading of the user's text: what title, author and year they might mean.
/// <see cref="FreeText"/> is set only by the rule-based fallback, which can't tell titles from authors.
/// <see cref="IsSeries"/> means <see cref="Title"/> names a series ("harry potter"), so there's no single answer.
/// </summary>
public sealed record QueryHypothesis(
    string? Title,
    string? Author,
    IReadOnlyList<string> Keywords,
    int? Year,
    string? FreeText = null,
    bool IsSeries = false)
{
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    public bool IsFreeText => !string.IsNullOrWhiteSpace(FreeText);
    public bool IsAuthorOnly => HasAuthor && !HasTitle && !IsFreeText;

    /// <summary>Describes a book ("whale", "lighthouse") without naming a title or author.</summary>
    public bool IsKeywordOnly => !HasTitle && !HasAuthor && !IsFreeText && Keywords.Count > 0;

    public bool IsSearchable => HasTitle || HasAuthor || IsFreeText || IsKeywordOnly;
}
