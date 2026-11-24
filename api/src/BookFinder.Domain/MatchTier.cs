namespace BookFinder.Domain;

/// <summary>The brief's matching hierarchy, strongest first.</summary>
public enum MatchTier
{
    /// <summary>Exact title and a primary author.</summary>
    A = 1,

    /// <summary>Exact title, author listed only as a contributor.</summary>
    B = 2,

    /// <summary>Near title and a matching author.</summary>
    C = 3,

    /// <summary>Author-only query: that author's top works.</summary>
    D = 4,

    /// <summary>Weaker candidate: title or author matches, not both.</summary>
    E = 5,
}

public enum TitleMatch
{
    None,
    Near,
    Exact,
}

public enum AuthorRole
{
    None,
    Contributor,

    /// <summary>On the work record, but not listed first.</summary>
    Primary,

    /// <summary>First author on the work record.</summary>
    Lead,
}
