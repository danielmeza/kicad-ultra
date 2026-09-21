using System;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// The one definition of "the same query", shared by the response cache and the view model.
/// </summary>
public static class SearchQueryNormalizer
{
    /// <summary>
    /// Trims the query and collapses every run of whitespace to a single space. The result is what
    /// is sent to the providers, so a cached answer is the answer to exactly that text. The cache
    /// additionally compares it case-insensitively; every direct-API provider today (jlcsearch,
    /// Nexar) matches case-insensitively, so "ne555" and "NE555" are the same question.
    /// </summary>
    public static string Normalize(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? string.Empty
            : string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
