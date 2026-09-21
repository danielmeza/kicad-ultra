using System;

namespace UltraLibrarianImporter.UI.Services.Providers;

public sealed class UltraLibrarianProvider : BaseArchiveComponentProvider
{
    public override string Id => "ultralibrarian";
    public override string DisplayName => "UltraLibrarian";
    public override string SearchUrl => "https://app.ultralibrarian.com/Account/Login?returnUrl=%252fsearch";
    public override string DefaultPrefix => "UL_";
    public override string DefaultLibraryName => "UltraLibrarian";
    public override string ProviderColor => "#E65100";

    /// <summary>
    /// Ultra Librarian's search for a part number: where the search box on ultralibrarian.com sends it
    /// (<c>GET https://app.ultralibrarian.com/search</c> with <c>queryText</c>). A visitor who is not
    /// signed in is sent to Ultra Librarian's sign-in page first.
    /// </summary>
    public static string PartSearchUrl(string partNumber) =>
        $"https://app.ultralibrarian.com/search?queryText={Uri.EscapeDataString(partNumber)}";
}
