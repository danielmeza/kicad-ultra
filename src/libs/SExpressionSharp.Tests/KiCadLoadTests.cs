using System.Text.Json.Nodes;

namespace SExpressionSharp.Tests;

/// <summary>
/// The other half of acceptance 1: a board, a symbol library and a design-rule file that KiCad
/// still loads and still reads the same way after a full canonical round-trip. Schematics are
/// covered by the netlist comparison in <see cref="FidelityTests"/>.
/// </summary>
public class KiCadLoadTests
{
    public static TheoryData<string> Boards()
    {
        var d = new TheoryData<string>();
        foreach (var f in Corpus.Boards())
        {
            d.Add(f);
        }

        if (d.Count == 0)
        {
            d.Add("<no corpus>");
        }

        return d;
    }

    public static TheoryData<string> DesignRules()
    {
        var d = new TheoryData<string>();
        foreach (var f in Corpus.DesignRules())
        {
            d.Add(f);
        }

        if (d.Count == 0)
        {
            d.Add("<no corpus>");
        }

        return d;
    }

    [Theory]
    [MemberData(nameof(Boards))]
    public void Board_CanonicalRoundTrip_LoadsAndReportsTheSameStatistics(string relative)
    {
        if (relative == "<no corpus>" || Corpus.KiCadCli is null)
        {
            return;
        }

        var src = Corpus.Read(relative);
        var written = Canonical(src);

        var baseline = Corpus.Stage(relative, src);
        var candidate = Corpus.Stage(relative, written);
        try
        {
            Assert.Equal(BoardStats(baseline.File), BoardStats(candidate.File));
        }
        finally
        {
            Directory.Delete(baseline.Dir, recursive: true);
            Directory.Delete(candidate.Dir, recursive: true);
        }
    }

    [Fact]
    public void SymbolLibrary_CanonicalRoundTrip_UpgradesToTheSameBytes()
    {
        if (Corpus.Missing || Corpus.KiCadCli is null)
        {
            return;
        }

        const string Relative = "libs/orbion.kicad_sym";
        var src = Corpus.Read(Relative);

        var baseline = Corpus.Stage(Relative, src);
        var candidate = Corpus.Stage(Relative, Canonical(src));
        try
        {
            Assert.Equal(Upgraded(baseline.File), Upgraded(candidate.File));
        }
        finally
        {
            Directory.Delete(baseline.Dir, recursive: true);
            Directory.Delete(candidate.Dir, recursive: true);
        }
    }

    /// <summary>
    /// Writes each design-rule file into a probe project as its own rules and runs DRC. This is the
    /// only check that proves KiCad still *evaluates* the rules, comments and all.
    /// </summary>
    [Theory]
    [MemberData(nameof(DesignRules))]
    public void DesignRules_RoundTrip_ProduceTheSameDrcResult(string relative)
    {
        if (relative == "<no corpus>" || Corpus.KiCadCli is null)
        {
            return;
        }

        var probe = Path.Combine("tests", "design-rules", "probe-4layer", "probe-4layer.kicad_pcb");
        if (!File.Exists(Path.Combine(Corpus.RequireRoot(), probe)))
        {
            return;
        }

        var src = Corpus.Read(relative);
        foreach (var candidateText in new[] { SDocument.Parse(src).ToText(), Canonical(src) })
        {
            var baseline = Corpus.Stage(probe, Corpus.Read(probe));
            var candidate = Corpus.Stage(probe, Corpus.Read(probe));
            try
            {
                File.WriteAllText(Path.Combine(baseline.Dir, "probe-4layer.kicad_dru"), src);
                File.WriteAllText(Path.Combine(candidate.Dir, "probe-4layer.kicad_dru"), candidateText);

                var expected = Drc(baseline.File, baseline.Dir);
                var actual = Drc(candidate.File, candidate.Dir);
                Assert.True(expected.Count > 0, "the probe board should report violations, otherwise this proves nothing");
                Assert.Equal(expected, actual);
            }
            finally
            {
                Directory.Delete(baseline.Dir, recursive: true);
                Directory.Delete(candidate.Dir, recursive: true);
            }
        }
    }

    private static string Canonical(string src) =>
        SDocument.Parse(src).ToText(new SExpressionWriterOptions { Format = SExpressionFormat.Canonical });

    private static string BoardStats(string board)
    {
        var outFile = Path.Combine(Path.GetDirectoryName(board)!, "stats.txt");
        var r = Corpus.RunCli("pcb", "export", "stats", "-o", outFile, board);
        Assert.True(File.Exists(outFile), $"kicad-cli did not load the board (exit {r.ExitCode}): {r.All}");
        return string.Join('\n', File.ReadAllLines(outFile).Where(l => !l.StartsWith("- Date:", StringComparison.Ordinal)));
    }

    private static string Upgraded(string library)
    {
        var outFile = Path.Combine(Path.GetDirectoryName(library)!, "upgraded.kicad_sym");
        var r = Corpus.RunCli("sym", "upgrade", "--force", "-o", outFile, library);
        Assert.True(File.Exists(outFile), $"kicad-cli did not load the symbol library (exit {r.ExitCode}): {r.All}");
        return File.ReadAllText(outFile);
    }

    /// <summary>
    /// Runs DRC and returns the sorted list of violation descriptions and severities.
    /// </summary>
    /// <remarks>
    /// MEASURED, KiCad 10.0.6: the <c>items</c> array is NOT deterministic -- two DRC runs over the
    /// same board and the same rules pair one creepage violation against different tracks. The
    /// description carries the rule name and the measured value, which is what the design rules
    /// actually decide, and that part is stable.
    /// </remarks>
    private static List<string> Drc(string board, string stagingDir)
    {
        var outFile = Path.Combine(Path.GetDirectoryName(board)!, "drc.json");
        var r = Corpus.RunCli("pcb", "drc", "--format", "json", "--severity-all", "-o", outFile, board);
        Assert.True(File.Exists(outFile), $"kicad-cli did not run DRC (exit {r.ExitCode}): {r.All}");

        var root = JsonNode.Parse(File.ReadAllText(outFile))!.AsObject();
        var lines = new List<string>();
        foreach (var key in new[] { "violations", "unconnected_items", "schematic_parity" })
        {
            if (root[key] is JsonArray array)
            {
                foreach (var entry in array)
                {
                    var o = entry!.AsObject();
                    var description = o["description"]?.GetValue<string>() ?? entry.ToJsonString();
                    var severity = o["severity"]?.GetValue<string>() ?? string.Empty;
                    lines.Add($"{key}|{severity}|{description}".Replace(stagingDir, "<staged>", StringComparison.Ordinal));
                }
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }
}
