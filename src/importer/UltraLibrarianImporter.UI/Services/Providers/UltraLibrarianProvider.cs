namespace UltraLibrarianImporter.UI.Services.Providers
{
    public sealed class UltraLibrarianProvider : BaseArchiveComponentProvider
    {
        public override string Id => "ultralibrarian";
        public override string DisplayName => "UltraLibrarian";
        public override string SearchUrl => "https://app.ultralibrarian.com/Account/Login?returnUrl=%252fsearch";
        public override string DefaultPrefix => "UL_";
        public override string DefaultLibraryName => "UltraLibrarian";
        public override string ProviderColor => "#E65100";
    }
}
