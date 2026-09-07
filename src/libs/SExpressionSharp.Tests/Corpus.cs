using System.Diagnostics;

namespace SExpressionSharp.Tests;

/// <summary>
/// Locates the real KiCad 10 corpus and the kicad-cli shim. Every corpus-backed test skips
/// (rather than fails) when the corpus is absent, so the suite still runs on a bare CI agent.
/// </summary>
public static class Corpus
{
    public static readonly string? Root = FindRoot();

    private static string? FindRoot()
    {
        var env = Environment.GetEnvironmentVariable("ORBION_KICAD_ROOT");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
        {
            return env;
        }

        const string Default = "/home/daniel-meza/Documents/repos/spn/orbion/hwr/orbion-kicad";
        return Directory.Exists(Default) ? Default : null;
    }

    /// <summary>True when the corpus is missing; xunit v2 cannot skip dynamically, so corpus-backed tests return early.</summary>
    public static bool Missing => Root is null;

    public static string RequireRoot()
    {
        Assert.True(Root is not null, "KiCad corpus not present (set ORBION_KICAD_ROOT).");
        return Root!;
    }

    public static string? KiCadCli
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("ORBION_KICAD_CLI");
            if (!string.IsNullOrEmpty(env))
            {
                return env;
            }

            if (Root is not null)
            {
                var shim = Path.Combine(Root, "scripts", "kicad-cli");
                if (File.Exists(shim))
                {
                    return shim;
                }
            }

            return null;
        }
    }

    public static string RequireKiCadCli()
    {
        var cli = KiCadCli;
        Assert.True(cli is not null, "kicad-cli not available (set ORBION_KICAD_CLI).");
        return cli!;
    }

    /// <summary>Every schematic in the corpus, relative to the corpus root.</summary>
    public static IEnumerable<string> Schematics()
    {
        if (Root is null)
        {
            yield break;
        }

        foreach (var f in Directory.GetFiles(Path.Combine(Root, "templates"), "*.kicad_sch", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (char.IsLower(Path.GetFileName(f)[0]))
            {
                yield return Path.GetRelativePath(Root, f);
            }
        }

        foreach (var f in Directory.GetFiles(Path.Combine(Root, "blocks"), "*.kicad_sch", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            yield return Path.GetRelativePath(Root, f);
        }
    }

    public static IEnumerable<string> Boards()
    {
        if (Root is null)
        {
            yield break;
        }

        foreach (var dir in new[] { "templates", "blocks", "tests" })
        {
            var full = Path.Combine(Root, dir);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var f in Directory.GetFiles(full, "*.kicad_pcb", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                yield return Path.GetRelativePath(Root, f);
            }
        }
    }

    public static IEnumerable<string> DesignRules()
    {
        if (Root is null)
        {
            yield break;
        }

        foreach (var f in Directory.GetFiles(Path.Combine(Root, "config", "design-rules"), "*.kicad_dru").OrderBy(x => x, StringComparer.Ordinal))
        {
            yield return Path.GetRelativePath(Root, f);
        }
    }

    /// <summary>Everything the acceptance suite round-trips: schematics, boards, the symbol library, the worksheet and the design rules.</summary>
    public static IEnumerable<string> All() =>
        Schematics()
            .Concat(Boards())
            .Concat(DesignRules())
            .Concat(new[] { Path.Combine("libs", "orbion.kicad_sym"), Path.Combine("config", "worksheets", "orbion-a3.kicad_wks") });

    public static string Read(string relative) => File.ReadAllText(Path.Combine(RequireRoot(), relative));

    public sealed record CliResult(int ExitCode, string StdOut, string StdErr)
    {
        public string All => StdOut + StdErr;
    }

    public static CliResult RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo(RequireKiCadCli())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RequireRoot(),
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(300_000);
        return new CliResult(p.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Copies the project directory that holds <paramref name="relative"/> to a scratch directory so
    /// relative references (lib tables, project file) still resolve, then overwrites the one file.
    /// </summary>
    public static (string Dir, string File) Stage(string relative, string newContent)
    {
        var root = RequireRoot();
        var srcDir = Path.GetDirectoryName(Path.Combine(root, relative))!;
        // MEASURED: kicad-cli here is a flatpak, and `--filesystem=host` does NOT expose /tmp --
        // the sandbox has its own. Staging under $HOME (or ORBION_SEXPR_SCRATCH) is visible to both.
        var scratch = Environment.GetEnvironmentVariable("ORBION_SEXPR_SCRATCH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "sexpr-roundtrip");
        var dstDir = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dstDir);
        foreach (var f in Directory.GetFiles(srcDir))
        {
            File.Copy(f, Path.Combine(dstDir, Path.GetFileName(f)));
        }

        var target = Path.Combine(dstDir, Path.GetFileName(relative));
        File.WriteAllText(target, newContent);
        return (dstDir, target);
    }

    /// <summary>Strips the two lines a netlist export stamps with the source path and the run date.</summary>
    public static string NormalizeNetlist(string netlist)
    {
        var lines = netlist.Replace("\r\n", "\n").Split('\n');
        return string.Join('\n', lines.Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("(source ", StringComparison.Ordinal)
                && !t.StartsWith("(date ", StringComparison.Ordinal)
                && !t.StartsWith("(tool ", StringComparison.Ordinal);
        }));
    }
}
