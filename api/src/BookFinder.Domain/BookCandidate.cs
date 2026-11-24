namespace BookFinder.Domain;

public sealed record AuthorRef(string? Key, string Name);

/// <summary>
/// A work from Open Library. <see cref="PrimaryAuthors"/> come from the canonical work record
/// (<c>works.authors</c>), in listed order; <see cref="Contributors"/> are other names Open Library
/// attaches to the work in search results (illustrators, adaptors, editors).
/// </summary>
public sealed record BookCandidate(
    string WorkKey,
    string Title,
    string? Subtitle,
    IReadOnlyList<AuthorRef> PrimaryAuthors,
    IReadOnlyList<AuthorRef> Contributors,
    int? FirstPublishYear,
    int? CoverId,
    int EditionCount,
    int ReadingLogCount)
{
    /// <summary>"OL27482W" from "/works/OL27482W".</summary>
    public string OpenLibraryId => WorkKey[(WorkKey.LastIndexOf('/') + 1)..];

    public AuthorRef? LeadAuthor => PrimaryAuthors.Count > 0 ? PrimaryAuthors[0] : null;
}
