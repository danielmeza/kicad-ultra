using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace UltraLibrarianImporter.UI.Services.EasyEda2KiCad;

/// <summary>Where a working easyeda2kicad was found.</summary>
public enum EasyEda2KiCadSource
{
    /// <summary>The path the user set in Settings.</summary>
    SettingsPath,

    /// <summary>An <c>easyeda2kicad</c> executable on <c>PATH</c>.</summary>
    SearchPath,

    /// <summary><c>&lt;python&gt; -m easyeda2kicad</c> with KiCad's Python interpreter.</summary>
    KiCadPython,
}

/// <summary>
/// How to start easyeda2kicad: the program, and the arguments that go before easyeda2kicad's own
/// (<c>-m easyeda2kicad</c> when the program is a Python interpreter).
/// </summary>
public sealed record EasyEda2KiCadCommand(string FileName, IReadOnlyList<string> LeadingArguments, EasyEda2KiCadSource Source)
{
    /// <summary>The command as a person would type it. For display only: it is never executed as a string.</summary>
    public string DisplayCommand => LeadingArguments.Count == 0 ? FileName : $"{FileName} {string.Join(' ', LeadingArguments)}";

    /// <summary>The command and where it was found, for the UI and the import log.</summary>
    public string Description => Source switch
    {
        EasyEda2KiCadSource.SettingsPath => $"{DisplayCommand} (the path set in Settings)",
        EasyEda2KiCadSource.SearchPath => $"{DisplayCommand} (found on PATH)",
        EasyEda2KiCadSource.KiCadPython => $"{DisplayCommand} (KiCad's Python interpreter)",
        _ => DisplayCommand,
    };
}

/// <summary>
/// The result of looking for easyeda2kicad: the command that works, or <see langword="null"/> with
/// every place that was tried and why it did not work.
/// </summary>
public sealed record EasyEda2KiCadDetection(EasyEda2KiCadCommand? Command, IReadOnlyList<string> Attempts)
{
    public bool IsAvailable => Command is not null;
}

/// <summary>How to install easyeda2kicad on the system this importer is running on.</summary>
/// <param name="Command">The command to run in a terminal.</param>
/// <param name="Note">What the command does not say by itself.</param>
public sealed record EasyEda2KiCadInstallHelp(string Command, string Note);

/// <summary>
/// Finds the user-installed easyeda2kicad (#76). It is a third-party tool under AGPL-3.0 and not part
/// of this project: it is only ever started as a separate program, through its documented command-line
/// flags, and never imported, bundled or installed by this application.
/// </summary>
/// <remarks>
/// <para>
/// Looks in the order #76 specifies: the path set in Settings, then <c>easyeda2kicad</c> on
/// <c>PATH</c>, then <c>&lt;python&gt; -m easyeda2kicad</c> where <c>&lt;python&gt;</c> is KiCad's
/// interpreter, <c>api.interpreter_path</c> in <c>kicad_common.json</c>. Each candidate is probed
/// with <c>-h</c>, and accepted only if it exits with 0 and its help lists <c>--lcsc_id</c>, the flag
/// every conversion passes.
/// </para>
/// <para>
/// Every call looks again: nothing is cached, so installing the tool, or changing the path in
/// Settings, takes effect at the next check or import.
/// </para>
/// </remarks>
public sealed class EasyEda2KiCadLocator
{
    /// <summary>The executable and module name, as published on PyPI.</summary>
    public const string ToolName = "easyeda2kicad";

    /// <summary>The Flathub application id of KiCad.</summary>
    private const string KiCadFlatpakId = "org.kicad.KiCad";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<EasyEda2KiCadLocator> _logger;

    public EasyEda2KiCadLocator(ILogger<EasyEda2KiCadLocator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// True when this process runs inside KiCad's Flatpak sandbox, which is where it runs whenever the
    /// Flatpak KiCad starts it.
    /// </summary>
    public static bool IsInsideKiCadFlatpak =>
        string.Equals(Environment.GetEnvironmentVariable("FLATPAK_ID"), KiCadFlatpakId, StringComparison.Ordinal);

    /// <summary>
    /// The install command for this system. Under KiCad's Flatpak the tool has to be installed inside
    /// the sandbox, with the sandbox's own <c>pip3</c>: one installed on the host is invisible there.
    /// </summary>
    public static EasyEda2KiCadInstallHelp InstallHelp => IsInsideKiCadFlatpak
        ? new EasyEda2KiCadInstallHelp(
            "flatpak run --command=pip3 org.kicad.KiCad install --user easyeda2kicad",
            "KiCad runs as a Flatpak, and so does this importer when KiCad starts it. Run the command in a terminal on the host: " +
            "it installs easyeda2kicad inside KiCad's sandbox. A copy installed with pip or pipx outside the sandbox is not visible from inside it.")
        : new EasyEda2KiCadInstallHelp(
            "pipx install easyeda2kicad",
            "It needs Python 3.9 or newer. Or install it with pip into any Python environment, and set the path to the easyeda2kicad " +
            "it installs (or to that environment's Python) in Settings. If KiCad runs as a Flatpak, install it inside KiCad's sandbox " +
            "instead: flatpak run --command=pip3 org.kicad.KiCad install --user easyeda2kicad");

    /// <summary>
    /// Looks for a working easyeda2kicad, in the order described on the class.
    /// </summary>
    /// <param name="configuredPath">The path from Settings: easyeda2kicad itself, or a Python interpreter
    /// that has it installed. Empty to skip that step.</param>
    /// <param name="cancellationToken">Stops the search, and any probe that is running.</param>
    public async Task<EasyEda2KiCadDetection> LocateAsync(string? configuredPath, CancellationToken cancellationToken = default)
    {
        var attempts = new List<string>();

        // 1. The path set in Settings.
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var path = configuredPath.Trim();
            if (!File.Exists(path))
            {
                attempts.Add($"The path set in Settings, {path}, does not exist.");
            }
            else if (IsBatchFile(path))
            {
                // cmd.exe re-parses a batch file's arguments by its own rules, which ArgumentList's
                // quoting does not account for. pip and pipx install an .exe launcher; use that.
                attempts.Add($"The path set in Settings, {path}, is a batch file. Set it to easyeda2kicad.exe, or to a python.exe that has easyeda2kicad installed.");
            }
            else if (await ProbeAsync(ForProgram(path, EasyEda2KiCadSource.SettingsPath), attempts, cancellationToken) is { } fromSettings)
            {
                return Found(fromSettings, attempts);
            }
        }

        // 2. easyeda2kicad on PATH.
        var onPath = FindOnSearchPath();
        if (onPath is null)
        {
            attempts.Add($"{ToolName} is not on PATH.");
        }
        else if (await ProbeAsync(new EasyEda2KiCadCommand(onPath, [], EasyEda2KiCadSource.SearchPath), attempts, cancellationToken) is { } fromPath)
        {
            return Found(fromPath, attempts);
        }

        // 3. <python> -m easyeda2kicad, with the Python interpreter KiCad is configured to use.
        var interpreter = ReadKiCadInterpreter(attempts);
        if (interpreter is not null)
        {
            if (!File.Exists(interpreter))
            {
                attempts.Add($"KiCad's Python interpreter, {interpreter}, does not exist.");
            }
            else if (await ProbeAsync(new EasyEda2KiCadCommand(PreferConsoleInterpreter(interpreter), ["-m", ToolName], EasyEda2KiCadSource.KiCadPython), attempts, cancellationToken) is { } fromKiCad)
            {
                return Found(fromKiCad, attempts);
            }
        }

        _logger.LogInformation("{Tool} was not found: {Attempts}", ToolName, string.Join(" ", attempts));
        return new EasyEda2KiCadDetection(null, attempts);
    }

    /// <summary>
    /// Environment variables for every easyeda2kicad run, on top of the inherited environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PYTHONUTF8=1</c> is Python's own UTF-8 mode (PEP 540): the tool's messages reach the import
    /// log as UTF-8 whatever the locale, including part and manufacturer names that are not ASCII.
    /// </para>
    /// <para>
    /// Inside KiCad's Flatpak, the <c>pip3</c> and <c>python</c> wrappers that the Flathub manifest
    /// installs in <c>/app/bin</c> set <c>PYTHONUSERBASE=$XDG_DATA_HOME/python</c> and put its
    /// <c>bin</c> first on <c>PATH</c>, so that is where the install command in
    /// <see cref="InstallHelp"/> puts easyeda2kicad. KiCad itself sets neither variable, and its
    /// configured interpreter is <c>/usr/bin/python3</c>, not the wrapper, so without the same two
    /// variables here neither step 2 nor step 3 would see the package.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ChildEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PYTHONUTF8"] = "1",
        };

        var userBase = FlatpakPythonUserBase();
        if (userBase is not null)
        {
            environment["PYTHONUSERBASE"] = userBase;
            var path = Environment.GetEnvironmentVariable("PATH");
            var userBin = Path.Combine(userBase, "bin");
            environment["PATH"] = string.IsNullOrEmpty(path) ? userBin : $"{userBin}{Path.PathSeparator}{path}";
        }

        return environment;
    }

    /// <summary>
    /// Inside KiCad's Flatpak: the Python user base its <c>pip3</c> wrapper installs into, unless the
    /// environment already names one. <see langword="null"/> anywhere else.
    /// </summary>
    private static string? FlatpakPythonUserBase()
    {
        if (!IsInsideKiCadFlatpak)
        {
            return null;
        }

        var existing = Environment.GetEnvironmentVariable("PYTHONUSERBASE");
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrEmpty(dataHome) && Path.IsPathFullyQualified(dataHome)
            ? Path.Combine(dataHome, "python")
            : null;
    }

    private EasyEda2KiCadDetection Found(EasyEda2KiCadCommand command, List<string> attempts)
    {
        _logger.LogInformation("Using {Tool}: {Command}", ToolName, command.Description);
        return new EasyEda2KiCadDetection(command, attempts);
    }

    /// <summary>
    /// Runs <c>-h</c>, and returns <paramref name="command"/> if it answered as easyeda2kicad does.
    /// Otherwise adds the reason to <paramref name="attempts"/>.
    /// </summary>
    private async Task<EasyEda2KiCadCommand?> ProbeAsync(EasyEda2KiCadCommand command, List<string> attempts, CancellationToken cancellationToken)
    {
        ProcessOutcome outcome;
        try
        {
            outcome = await ExternalProcess.RunAsync(
                command.FileName,
                [.. command.LeadingArguments, "-h"],
                Path.GetTempPath(),
                ChildEnvironment(),
                ProbeTimeout,
                cancellationToken);
        }
        catch (Win32Exception ex)
        {
            _logger.LogInformation(ex, "{Command} could not be started", command.DisplayCommand);
            attempts.Add($"{command.DisplayCommand} could not be started: {ex.Message}");
            return null;
        }

        if (outcome.TimedOut)
        {
            attempts.Add($"{command.DisplayCommand} -h did not finish within {ProbeTimeout.TotalSeconds:0} seconds.");
            return null;
        }

        // --lcsc_id is the flag every conversion passes. Its presence in the help says this is
        // easyeda2kicad, and not some other program that happens to share the name.
        if (outcome.ExitCode == 0 && outcome.StandardOutput.Contains("--lcsc_id", StringComparison.Ordinal))
        {
            return command;
        }

        var said = FirstLine(outcome.StandardError) ?? FirstLine(outcome.StandardOutput) ?? "no output";
        attempts.Add($"{command.DisplayCommand} -h did not answer as easyeda2kicad (exit code {outcome.ExitCode}): {said}");
        return null;
    }

    /// <summary>
    /// A path from Settings: easyeda2kicad itself, or a Python interpreter to run it as a module.
    /// </summary>
    private static EasyEda2KiCadCommand ForProgram(string path, EasyEda2KiCadSource source)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var isPython = name.StartsWith("python", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "py", StringComparison.OrdinalIgnoreCase);

        return isPython
            ? new EasyEda2KiCadCommand(PreferConsoleInterpreter(path), ["-m", ToolName], source)
            : new EasyEda2KiCadCommand(path, [], source);
    }

    /// <summary>
    /// KiCad on Windows configures <c>pythonw.exe</c>, the interpreter without a console. Its
    /// <c>python.exe</c> sibling is the same installation and reliably writes to redirected output.
    /// </summary>
    private static string PreferConsoleInterpreter(string interpreter)
    {
        if (!OperatingSystem.IsWindows()
            || !string.Equals(Path.GetFileName(interpreter), "pythonw.exe", StringComparison.OrdinalIgnoreCase))
        {
            return interpreter;
        }

        var directory = Path.GetDirectoryName(interpreter);
        var console = directory is null ? null : Path.Combine(directory, "python.exe");
        return console is not null && File.Exists(console) ? console : interpreter;
    }

    private static bool IsBatchFile(string path) =>
        OperatingSystem.IsWindows()
        && (path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The first <c>easyeda2kicad</c> executable on <c>PATH</c> (inside KiCad's Flatpak, the user
    /// base's <c>bin</c> first; see <see cref="ChildEnvironment"/>). On Windows only
    /// <c>easyeda2kicad.exe</c>, the launcher pip and pipx install; never a batch file.
    /// </summary>
    private static string? FindOnSearchPath()
    {
        var fileName = OperatingSystem.IsWindows() ? $"{ToolName}.exe" : ToolName;
        var searchPath = ChildEnvironment().TryGetValue("PATH", out var childPath)
            ? childPath
            : Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(searchPath))
        {
            return null;
        }

        foreach (var directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A relative entry would resolve against the working directory; the shell's rules for
            // that are not worth reproducing.
            if (!Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate) && IsExecutable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & anyExecute) != 0;
    }

    /// <summary>
    /// KiCad's configured Python interpreter, <c>api.interpreter_path</c> in the <c>kicad_common.json</c>
    /// of the newest KiCad version that has one. <see langword="null"/>, with the reason added to
    /// <paramref name="attempts"/>, when there is none.
    /// </summary>
    /// <remarks>
    /// The newest version is a guess when several are installed side by side; see
    /// <see cref="KiCadSettingsDirectory.FindNewestContaining"/>. Under KiCad's Flatpak the importer
    /// inherits the sandbox's <c>XDG_CONFIG_HOME</c>, so this reads the sandboxed KiCad's settings.
    /// </remarks>
    private static string? ReadKiCadInterpreter(List<string> attempts)
    {
        const string settingsFile = "kicad_common.json";
        var directory = KiCadSettingsDirectory.FindNewestContaining(settingsFile);
        if (directory is null)
        {
            attempts.Add($"KiCad's Python interpreter is unknown: no KiCad version under {KiCadSettingsDirectory.GetRoot()} has a {settingsFile}.");
            return null;
        }

        var file = Path.Combine(directory, settingsFile);
        try
        {
            using FileStream stream = File.OpenRead(file);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("api", out JsonElement api)
                && api.ValueKind == JsonValueKind.Object
                && api.TryGetProperty("interpreter_path", out JsonElement interpreter)
                && interpreter.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(interpreter.GetString()))
            {
                return interpreter.GetString()!.Trim();
            }

            attempts.Add($"KiCad's Python interpreter is not set: {file} has no api.interpreter_path.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            attempts.Add($"KiCad's Python interpreter is unknown: {file} could not be read ({ex.Message}).");
        }

        return null;
    }

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
