using System;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// Which of KiCad's library tables an imported library is registered in, as chosen in Settings (#71).
/// </summary>
/// <remarks>
/// KiCad keeps two of each table: the global <c>sym-lib-table</c> and <c>fp-lib-table</c> in its settings
/// directory, which every project sees, and a project's own pair next to its <c>.kicad_pro</c>. A library
/// written into a project belongs in that project's tables, by a <c>${KIPRJMOD}</c>-relative path: it then
/// moves with the project, and the global table does not gain a row per project, by an absolute path that
/// breaks when the project moves. <see cref="ConfigService"/> saves the value by name, so do not rename one.
/// </remarks>
public enum LibraryRegistrationScope
{
    /// <summary>
    /// The project's tables when the import goes into a KiCad project, KiCad's global tables when it does
    /// not. The default.
    /// </summary>
    Automatic,

    /// <summary>
    /// Always the project's tables. Without a KiCad project to import into, the import fails before any
    /// library is written.
    /// </summary>
    Project,

    /// <summary>Always KiCad's global tables, by absolute path, even for a library written into a project.</summary>
    Global,
}

/// <summary>
/// The library table one import registers in: what a <see cref="LibraryRegistrationScope"/> comes to for it.
/// </summary>
public enum LibraryTableScope
{
    /// <summary>The project's own table, created when it is missing, with <c>${KIPRJMOD}</c>-relative paths.</summary>
    Project,

    /// <summary>KiCad's global table in its settings directory, with absolute paths. It is never created.</summary>
    Global,
}

public static class LibraryRegistrationScopeExtensions
{
    /// <summary>
    /// The table <paramref name="scope"/> selects for one import, or <see langword="null"/> when it asks for
    /// the project's table and there is no project.
    /// </summary>
    /// <param name="scope">The scope chosen in Settings.</param>
    /// <param name="hasProject">The import goes into the directory of a KiCad project.</param>
    public static LibraryTableScope? SelectTable(this LibraryRegistrationScope scope, bool hasProject) => scope switch
    {
        LibraryRegistrationScope.Automatic => hasProject ? LibraryTableScope.Project : LibraryTableScope.Global,
        LibraryRegistrationScope.Project => hasProject ? LibraryTableScope.Project : null,
        LibraryRegistrationScope.Global => LibraryTableScope.Global,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
