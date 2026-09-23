using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using SExpressions;

namespace KiCadUltra.Services;

/// <summary>
/// The two library tables the importer registers libraries in.
/// </summary>
public enum LibraryTableKind
{
    /// <summary><c>sym-lib-table</c>, listing <c>.kicad_sym</c> files.</summary>
    Symbol,

    /// <summary><c>fp-lib-table</c>, listing <c>.pretty</c> directories.</summary>
    Footprint,
}

/// <summary>
/// What <see cref="KiCadLibraryTable.Register"/> did.
/// </summary>
public enum LibraryTableUpdateStatus
{
    /// <summary>A row was appended, after creating the table if that was allowed and it did not exist.</summary>
    Added,

    /// <summary>
    /// The table already has a row for this library, under this nickname or another one. Nothing was
    /// written.
    /// </summary>
    AlreadyRegistered,

    /// <summary>
    /// A row with this nickname points at a different library. It was left as it is and nothing was
    /// written.
    /// </summary>
    NameConflict,

    /// <summary>The table could not be read, parsed or written. The file on disk was not changed.</summary>
    Failed,
}

/// <summary>
/// One row to register.
/// </summary>
/// <param name="Nickname">The library nickname KiCad shows, the <c>name</c> field.</param>
/// <param name="Uri">What goes in the <c>uri</c> field, as KiCad should read it.</param>
/// <param name="LibraryPath">
/// The absolute path of the library on disk, used to recognise a row that already points at it.
/// </param>
/// <param name="Description">The <c>descr</c> field.</param>
public sealed record LibraryTableEntry(string Nickname, string Uri, string LibraryPath, string Description);

/// <summary>
/// The outcome of <see cref="KiCadLibraryTable.Register"/>.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="TablePath">The table that was read and, for <see cref="LibraryTableUpdateStatus.Added"/>, written.</param>
/// <param name="Message">A sentence for the user, naming the table.</param>
/// <param name="Error">The exception behind a <see cref="LibraryTableUpdateStatus.Failed"/>, if there was one.</param>
public sealed record LibraryTableUpdate(LibraryTableUpdateStatus Status, string TablePath, string Message, Exception? Error = null);

/// <summary>
/// Adds a library to a KiCad <c>sym-lib-table</c> or <c>fp-lib-table</c> without disturbing anything
/// already in it.
/// </summary>
/// <remarks>
/// <para>
/// KiCad 10's IPC API has no library-table commands (<c>GetLibraryTable</c>,
/// <c>AddLibraryTableEntry</c> and <c>ReloadLibrary</c> are marked "Since: 11.0" in KiCad's
/// <c>api/proto/common/commands/library_commands.proto</c>), so the table file is edited directly.
/// </para>
/// <para>
/// The existing text is never re-rendered. The file is parsed with <see cref="SDocument"/> to validate
/// it, find the rows and locate the last one, and the new row is inserted into the original text
/// right after it. Everything that was there stays byte for byte, including its indentation, line
/// endings and a UTF-8 BOM.
/// </para>
/// <para>
/// The splice was written for SExpressions 0.1.1, which laid the gaps of a form whose item list had
/// changed out again, so a table indented with two spaces came back indented with tabs. SExpressions
/// 0.2.0 no longer does: a node added beside rows that stand on one line is written on one line too,
/// with the sibling's separators and indentation, and it breaks its lines the way the file does, so a
/// CRLF table stays CRLF (sexpressions#35, #36). Measured on KiCad 10.0.6's template
/// <c>sym-lib-table</c>, a CRLF copy, a copy with a BOM, a KiCad 9-style table and a table holding
/// nothing but a <c>(version 7)</c>: appending the row <see cref="RenderRow"/> parses through
/// <see cref="SExpression.AddChild"/> writes the same bytes as the splice in all five. A row built in
/// memory rather than parsed does not, in the last two: it copies the KiCad 9 rows' missing spaces,
/// and with no row to copy it is laid out one child per line where KiCad writes one row per line. So
/// the splice stays: it is the path #118 measured under both SExpressions versions, and it writes
/// KiCad's own row whatever the table holds.
/// </para>
/// <para>
/// The table must also be one KiCad 10.0.6 can read. Its parser is a strict grammar
/// (<c>include/libraries/library_table_grammar.h</c>), not a general s-expression reader: an optional
/// <c>(version ...)</c> and then only <c>lib</c> rows, each made of <c>name</c>, <c>type</c>,
/// <c>uri</c>, <c>options</c> and <c>descr</c> with one value, or a bare <c>(hidden)</c> or
/// <c>(disabled)</c>; no comments; and a quoted value ends at the next <c>"</c>, with no escapes at
/// all. A table outside that grammar is one KiCad already refuses to load, so it is reported instead
/// of written to, and a value containing <c>"</c> or <c>\</c> is refused rather than escaped into
/// something KiCad would read differently.
/// </para>
/// <para>
/// Anything that fails is left untouched and reported as <see cref="LibraryTableUpdateStatus.Failed"/>.
/// The result is re-parsed and re-validated before it is written, and written through a temporary
/// file in the same directory so a failure part-way cannot leave KiCad a truncated table.
/// </para>
/// </remarks>
public static partial class KiCadLibraryTable
{
    /// <summary>KiCad's variable for the directory holding the open project's <c>.kicad_pro</c>.</summary>
    private const string ProjectDirectoryVariable = "KIPRJMOD";

    /// <summary>
    /// A row as KiCad 10.0.6 writes one (<c>LIBRARY_TABLE::Format</c>, one row per line). The values are
    /// filled in through <see cref="SExpression.SetChildValue"/>, so only the edited atoms are
    /// re-rendered, always quoted. KiCad 9 writes the fields without the spaces between them; both
    /// parsers accept either.
    /// </summary>
    private const string RowTemplate = """(lib (name "") (type "KiCad") (uri "") (options "") (descr ""))""";

    /// <summary>The <c>version</c> KiCad 10.0.6 writes into every table.</summary>
    private const string TableVersion = "7";

    private static readonly string[] RowProperties = ["name", "type", "uri", "options", "descr"];

    private static readonly string[] RowMarkers = ["hidden", "disabled"];

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The file name KiCad gives this kind of table.</summary>
    public static string FileName(LibraryTableKind kind) => kind switch
    {
        LibraryTableKind.Symbol => "sym-lib-table",
        LibraryTableKind.Footprint => "fp-lib-table",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string RootToken(LibraryTableKind kind) => kind switch
    {
        LibraryTableKind.Symbol => "sym_lib_table",
        LibraryTableKind.Footprint => "fp_lib_table",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The <c>uri</c> for a library: <c>${KIPRJMOD}/...</c> when it lies inside
    /// <paramref name="projectDirectory"/>, so the project keeps working when it is moved or cloned,
    /// and the absolute path otherwise. Separators are <c>/</c> on every platform, as KiCad writes them.
    /// </summary>
    public static string ToUri(string libraryPath, string? projectDirectory)
    {
        var fullPath = Path.GetFullPath(libraryPath);
        if (!string.IsNullOrEmpty(projectDirectory))
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(projectDirectory), fullPath);
            var outside = Path.IsPathRooted(relative)
                || relative == ".."
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);

            if (!outside)
            {
                return $"${{{ProjectDirectoryVariable}}}/{relative.Replace('\\', '/')}";
            }
        }

        return fullPath.Replace('\\', '/');
    }

    /// <summary>
    /// Registers <paramref name="entry"/> in the table at <paramref name="tablePath"/>.
    /// </summary>
    /// <param name="tablePath">The table file.</param>
    /// <param name="kind">Which kind of table it must be.</param>
    /// <param name="entry">The row to add.</param>
    /// <param name="createIfMissing">
    /// Create the table when it does not exist. Right for a project table. Wrong for the global table:
    /// KiCad 10's start-up wizard offers to fill that one with the default libraries only while it is
    /// missing or invalid, and a table holding nothing but this row would pre-empt that.
    /// </param>
    /// <param name="projectDirectory">
    /// What <c>${KIPRJMOD}</c> stands for when comparing existing rows, or <see langword="null"/> to take
    /// it from the environment like any other variable.
    /// </param>
    /// <remarks>
    /// Re-registering is idempotent. A row with the same nickname that resolves to the same library, or
    /// a row under any nickname that resolves to it, means there is nothing to do and the file is not
    /// written. A row with the same nickname that points elsewhere, or whose <c>uri</c> uses a variable
    /// that cannot be resolved here, is someone else's library: it is left alone and reported as
    /// <see cref="LibraryTableUpdateStatus.NameConflict"/>.
    /// </remarks>
    public static LibraryTableUpdate Register(
        string tablePath,
        LibraryTableKind kind,
        LibraryTableEntry entry,
        bool createIfMissing,
        string? projectDirectory)
    {
        var rootToken = RootToken(kind);
        var exists = File.Exists(tablePath);

        if (entry.Nickname.Length == 0 || entry.Uri.Length == 0 || !CanBeWritten(entry.Nickname) || !CanBeWritten(entry.Uri) || !CanBeWritten(entry.Description))
        {
            return new(LibraryTableUpdateStatus.Failed, tablePath,
                $"'{entry.Nickname}' ({entry.Uri}) cannot go into a KiCad library table: a name or path there cannot be empty or contain \", \\ or control characters.");
        }

        if (!exists && !createIfMissing)
        {
            return new(LibraryTableUpdateStatus.Failed, tablePath, $"{tablePath} does not exist.");
        }

        string original;
        var hasBom = false;
        if (exists)
        {
            try
            {
                var bytes = File.ReadAllBytes(tablePath);
                hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
                original = StrictUtf8.GetString(bytes, hasBom ? Utf8Bom.Length : 0, bytes.Length - (hasBom ? Utf8Bom.Length : 0));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                return new(LibraryTableUpdateStatus.Failed, tablePath, $"{tablePath} could not be read: {ex.Message}", ex);
            }
        }
        else
        {
            original = EmptyTable(rootToken);
        }

        SDocument document;
        try
        {
            document = SDocument.Parse(original);
        }
        catch (SExpressionFormatException ex)
        {
            return new(LibraryTableUpdateStatus.Failed, tablePath, $"{tablePath} is not a valid library table and was left unchanged: {ex.Message}", ex);
        }

        var violation = FindGrammarViolation(document, rootToken);
        if (violation is not null || document.Root is not { } root)
        {
            return new(LibraryTableUpdateStatus.Failed, tablePath, $"{tablePath} is not a table KiCad 10 can read ({violation}); it was left unchanged.");
        }

        var libraryPath = NormalizePath(entry.LibraryPath);
        var rows = root.GetChildren("lib").ToList();

        SExpression? sameName = rows.FirstOrDefault(row => row.GetChildValue("name") == entry.Nickname);
        if (sameName is not null)
        {
            var uri = sameName.GetChildValue("uri");
            return PointsTo(uri, libraryPath, projectDirectory)
                ? new(LibraryTableUpdateStatus.AlreadyRegistered, tablePath, $"'{entry.Nickname}' is already in {tablePath}.")
                : new(LibraryTableUpdateStatus.NameConflict, tablePath, $"{tablePath} already has a library named '{entry.Nickname}' pointing to {uri ?? "nothing"}; it was left unchanged.");
        }

        SExpression? sameTarget = rows.FirstOrDefault(row => PointsTo(row.GetChildValue("uri"), libraryPath, projectDirectory));
        if (sameTarget is not null)
        {
            return new(LibraryTableUpdateStatus.AlreadyRegistered, tablePath, $"{entry.LibraryPath} is already in {tablePath} as '{sameTarget.GetChildValue("name")}'.");
        }

        var rowText = RenderRow(entry);
        var updated = InsertRow(original, root, rowText);
        if (updated is null || !HasNewRow(updated, rootToken, entry, rows.Count + 1))
        {
            return new(LibraryTableUpdateStatus.Failed, tablePath, $"Could not work out where to add a row to {tablePath}; it was left unchanged.");
        }

        try
        {
            WriteThroughTemporaryFile(tablePath, updated, hasBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(LibraryTableUpdateStatus.Failed, tablePath, $"{tablePath} could not be written: {ex.Message}", ex);
        }

        return new(LibraryTableUpdateStatus.Added, tablePath, $"'{entry.Nickname}' was added to {tablePath}.");
    }

    /// <summary>An empty table, as KiCad writes one: <c>(sym_lib_table\n\t(version 7)\n)\n</c>.</summary>
    private static string EmptyTable(string rootToken)
    {
        var table = new SExpression(rootToken);
        _ = table.CreateChild("version", TableVersion);
        return table.ToText();
    }

    private static string RenderRow(LibraryTableEntry entry)
    {
        var row = SExpression.Parse(RowTemplate);
        (string Token, string Value)[] fields = [("name", entry.Nickname), ("uri", entry.Uri), ("descr", entry.Description)];
        foreach ((string Token, string Value) field in fields)
        {
            _ = row.SetChildValue(field.Token, field.Value);
        }

        return row.ToText();
    }

    /// <summary>
    /// Inserts <paramref name="rowText"/> into <paramref name="text"/> after the last form inside
    /// <paramref name="root"/>, on its own line with that form's indentation, or on the same line when
    /// that form does not start a line. Returns <see langword="null"/> if the parser's source spans do not
    /// point back into <paramref name="text"/>.
    /// </summary>
    private static string? InsertRow(string text, SExpression root, string rowText)
    {
        ReadOnlySpan<char> source = text;
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        SChildCollection children = root.Children;

        if (children.Count == 0)
        {
            // Not even a (version ...): put the row on its own line straight after the root token.
            if (!source.Overlaps(root.SourceSpan, out var rootStart))
            {
                return null;
            }

            var tokenStart = text.IndexOf(root.Token, rootStart, StringComparison.Ordinal);
            return tokenStart < 0 ? null : text.Insert(tokenStart + root.Token.Length, $"{newLine}\t{rowText}");
        }

        SExpression last = children[^1];
        if (!source.Overlaps(last.SourceSpan, out var lastStart))
        {
            return null;
        }

        var lineStart = text.LastIndexOf('\n', lastStart - 1) + 1;
        var indent = text[lineStart..lastStart];
        var separator = lineStart > 0 && string.IsNullOrWhiteSpace(indent) ? newLine + indent : " ";

        return text.Insert(lastStart + last.SourceSpan.Length, separator + rowText);
    }

    /// <summary>
    /// Re-parses the text about to be written and checks that it is still a table KiCad can read, with
    /// exactly one more row, and that the new row reads back with the values that went in.
    /// </summary>
    private static bool HasNewRow(string text, string rootToken, LibraryTableEntry entry, int expectedRows)
    {
        try
        {
            var document = SDocument.Parse(text);
            if (FindGrammarViolation(document, rootToken) is not null || document.Root is not { } root)
            {
                return false;
            }

            var rows = root.GetChildren("lib").ToList();
            return rows.Count == expectedRows
                && rows.Any(row => row.GetChildValue("name") == entry.Nickname
                    && row.GetChildValue("uri") == entry.Uri
                    && row.GetChildValue("type") == "KiCad");
        }
        catch (SExpressionFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks a parsed table against KiCad 10.0.6's library-table grammar and says what breaks it, or
    /// returns <see langword="null"/> when KiCad can read it. Whitespace is the one thing the parse tree
    /// cannot show, and the grammar allows any amount of it between tokens.
    /// </summary>
    private static string? FindGrammarViolation(SDocument document, string rootToken)
    {
        if (document.Items.Count != 1 || document.Root is not { } root)
        {
            return "the file must hold one top-level form and nothing else";
        }

        if (root.Token != rootToken)
        {
            return $"it is a ({root.Token} ...) table, not a ({rootToken} ...) one";
        }

        SChildCollection children = root.Children;
        if (root.Items.Count != children.Count)
        {
            return "a comment or bare value sits between the rows";
        }

        for (var i = 0; i < children.Count; i++)
        {
            SExpression child = children[i];
            if (i == 0 && child.Token == "version")
            {
                if (!HasOneValue(child))
                {
                    return "(version ...) must hold exactly one value";
                }

                continue;
            }

            if (child.Token != "lib")
            {
                return $"({child.Token} ...) is not a library row";
            }

            if (child.Children.Count == 0 || child.Items.Count != child.Children.Count)
            {
                return "a (lib ...) row may hold only (name ...), (type ...), (uri ...), (options ...), (descr ...), (hidden) and (disabled)";
            }

            foreach (SExpression member in child.Children)
            {
                var valid = RowProperties.Contains(member.Token)
                    ? HasOneValue(member) && !member.Values[0].Contains('"')
                    : RowMarkers.Contains(member.Token) && member.Items.Count == 0;

                if (!valid)
                {
                    return $"a (lib ...) row holds an unexpected ({member.Token} ...)";
                }
            }
        }

        return null;
    }

    private static bool HasOneValue(SExpression form) => form.Items.Count == 1 && form.Values.Count == 1;

    /// <summary>
    /// True when <paramref name="value"/> survives a round trip through KiCad 10's table grammar as a
    /// quoted string: the grammar ends it at the next <c>"</c> and keeps a backslash as it is, while
    /// the writer would escape both.
    /// </summary>
    private static bool CanBeWritten(string value) => !value.Any(c => c is '"' or '\\' || char.IsControl(c));

    private static void WriteThroughTemporaryFile(string tablePath, string text, bool bom)
    {
        // Write through a symlink rather than replacing it: a table kept in a dotfiles repository
        // and linked into place must stay linked.
        var target = File.Exists(tablePath)
            ? File.ResolveLinkTarget(tablePath, returnFinalTarget: true)?.FullName ?? tablePath
            : tablePath;

        var directory = Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".";
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom));
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            // A no-op once the move has happened.
            File.Delete(temporary);
        }
    }

    /// <summary>
    /// True when <paramref name="uri"/> resolves to <paramref name="libraryPath"/>. A <c>uri</c> using a
    /// variable that is not set here resolves to nothing, so it never matches.
    /// </summary>
    private static bool PointsTo(string? uri, string libraryPath, string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        var unresolved = false;
        var expanded = VariableReference().Replace(uri, match =>
        {
            var name = match.Groups[1].Value;
            var value = name == ProjectDirectoryVariable && !string.IsNullOrEmpty(projectDirectory)
                ? projectDirectory
                : Environment.GetEnvironmentVariable(name);

            unresolved |= string.IsNullOrEmpty(value);
            return value ?? match.Value;
        });

        return !unresolved
            && Path.IsPathFullyQualified(expanded)
            && string.Equals(NormalizePath(expanded), libraryPath, PathComparison);
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    [GeneratedRegex(@"\$\{([^}]*)\}")]
    private static partial Regex VariableReference();
}
