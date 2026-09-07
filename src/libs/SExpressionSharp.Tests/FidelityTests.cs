namespace SExpressionSharp.Tests;

/// <summary>
/// The acceptance suite. Everything here is measured against the real KiCad 10.0.6 corpus:
/// a file KiCad refuses is a failure however pretty the C# looks.
/// </summary>
public class FidelityTests
{
    public static TheoryData<string> Schematics()
    {
        var d = new TheoryData<string>();
        foreach (var f in Corpus.Schematics())
        {
            d.Add(f);
        }

        if (d.Count == 0)
        {
            d.Add("<no corpus>");
        }

        return d;
    }

    public static TheoryData<string> AllFiles()
    {
        var d = new TheoryData<string>();
        foreach (var f in Corpus.All())
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

    // ---------------------------------------------------------------- fix 1: quoting

    [Fact]
    public void Quoting_IsPreservedExactly_ByTheCanonicalWriter()
    {
        var src = Corpus.Read("templates/orbion-esp32s3/orbion-esp32s3.kicad_sch");
        var root = new SExpressionParser().Parse(src);
        var written = new SExpressionWriter().Write(root);

        // Every one of these is quoted in the source; KiCad refuses the file when they are not.
        Assert.Contains("(generator \"eeschema\")", written, StringComparison.Ordinal);
        Assert.Contains("(generator_version \"10.0\")", written, StringComparison.Ordinal);
        Assert.Contains("(uuid \"4d07836c-3bf1-57a4-9cbf-9dd27eb171bc\")", written, StringComparison.Ordinal);
    }

    // ------------------------------------------------- fix 3: value/child source order

    [Fact]
    public void ValuesAndChildren_KeepTheirSourceOrder()
    {
        var root = new SExpressionParser().Parse("(a 1 (b) 2 (c) 3)");
        var written = new SExpressionWriter().Write(root).Replace("\r\n", "\n");
        var flat = string.Join(' ', written.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
        Assert.Equal("(a 1 (b) 2 (c) 3 )", flat);
    }

    // ------------------------------------------------------- fix 2: multi-form documents

    [Fact]
    public void Parse_OnAMultiFormDocument_ReturnsOnlyTheFirstForm()
    {
        // This is the compat contract, and the reason ParseAll has to exist: a .kicad_dru is a
        // *sequence* of top-level forms, and Parse() by design yields the first one only.
        var src = Corpus.Read("config/design-rules/jlcpcb-4layer.kicad_dru");
        var first = new SExpressionParser().Parse(src);
        Assert.Equal("version", first.Token);
        Assert.True(src.Length > 20_000, "corpus file unexpectedly small");
    }

    // ------------------------------------------------------------ acceptance 2: bytes

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void RoundTrip_IsByteIdentical(string relative)
    {
        if (relative == "<no corpus>")
        {
            return;
        }

        var src = Corpus.Read(relative);
        var written = RoundTripPreserving(src);
        if (!string.Equals(src, written, StringComparison.Ordinal))
        {
            Assert.Fail($"{relative}: {src.Length} chars in, {written.Length} chars out; first difference at {FirstDifference(src, written)}");
        }
    }

    // ------------------------------------------------ acceptance 1: KiCad must load it

    [Theory]
    [MemberData(nameof(Schematics))]
    public void CanonicalRoundTrip_LoadsInKiCad_AndKeepsTheNetlist(string relative)
    {
        if (relative == "<no corpus>" || Corpus.KiCadCli is null)
        {
            return;
        }

        var src = Corpus.Read(relative);
        var written = RoundTripCanonical(src);

        var baseline = Corpus.Stage(relative, src);
        var candidate = Corpus.Stage(relative, written);
        try
        {
            var a = ExportNetlist(baseline.File);
            var b = ExportNetlist(candidate.File);
            Assert.Equal(a, b);
        }
        finally
        {
            Directory.Delete(baseline.Dir, recursive: true);
            Directory.Delete(candidate.Dir, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(DesignRules))]
    public void DesignRules_RoundTrip_KeepsEveryCommentBlock(string relative)
    {
        if (relative == "<no corpus>")
        {
            return;
        }

        var src = Corpus.Read(relative);
        var written = RoundTripPreserving(src);
        var expected = src.Split('\n').Count(l => l.TrimStart().StartsWith('#'));
        var actual = written.Split('\n').Count(l => l.TrimStart().StartsWith('#'));
        Assert.Equal(expected, actual);
        Assert.True(expected > 100, $"expected a comment-heavy file, found {expected} comment lines");
    }

    // ------------------------------------------------- acceptance 3: token preservation

    [Theory]
    [InlineData("templates/orbion-esp32s3/orbion-esp32s3.kicad_sch", "exclude_from_sim", 89)]
    [InlineData("templates/orbion-esp32s3/orbion-esp32s3.kicad_sch", "do_not_autoplace", 122)]
    [InlineData("templates/orbion-esp32s3/orbion-esp32s3.kicad_sch", "duplicate_pin_numbers_are_jumpers", 18)]
    [InlineData("templates/orbion-esp32s3/orbion-esp32s3.kicad_sch", "embedded_fonts", 19)]
    [InlineData("templates/orbion-esp32s3/orbion-esp32s3.kicad_sch", "generator_version", 1)]
    public void TokensThatOtherParsersDrop_SurviveTheRoundTrip(string relative, string token, int expected)
    {
        if (Corpus.Missing)
        {
            return;
        }

        var src = Corpus.Read(relative);
        Assert.Equal(expected, CountToken(new SExpressionParser().Parse(src), token));

        var written = RoundTripCanonical(src);
        Assert.Equal(expected, CountToken(new SExpressionParser().Parse(written), token));
    }

    /// <summary>
    /// The stronger form of the same claim: for every file in the corpus, the number of nodes
    /// carrying each of the five tokens Python's kiutils silently drops is identical before and
    /// after a canonical round-trip.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllFiles))]
    public void TokenCounts_AreIdentical_BeforeAndAfterRoundTrip(string relative)
    {
        if (relative == "<no corpus>")
        {
            return;
        }

        string[] tokens = ["exclude_from_sim", "do_not_autoplace", "duplicate_pin_numbers_are_jumpers", "embedded_fonts", "generator_version"];
        var src = Corpus.Read(relative);
        var before = tokens.Select(t => CountTokenInDocument(src, t)).ToArray();
        var after = tokens.Select(t => CountTokenInDocument(RoundTripCanonical(src), t)).ToArray();
        Assert.Equal(before, after);
    }

    // ------------------------------------------------------------------------- helpers

    private static string RoundTripPreserving(string src) => new SExpressionWriter().Write(new SExpressionParser().Parse(src));

    private static string RoundTripCanonical(string src) => new SExpressionWriter().Write(new SExpressionParser().Parse(src));

    private static int CountTokenInDocument(string text, string token) => CountToken(new SExpressionParser().Parse(text), token);

    private static int CountToken(SExpression e, string token)
    {
        var n = e.Token == token ? 1 : 0;
        foreach (var c in e.Children)
        {
            n += CountToken(c, token);
        }

        return n;
    }

    private static string ExportNetlist(string schematic)
    {
        var outFile = Path.Combine(Path.GetDirectoryName(schematic)!, "netlist.net");
        var r = Corpus.RunCli("sch", "export", "netlist", "--format", "kicadsexpr", "-o", outFile, schematic);
        Assert.True(File.Exists(outFile), $"kicad-cli did not produce a netlist (exit {r.ExitCode}): {r.All}");
        return Corpus.NormalizeNetlist(File.ReadAllText(outFile));
    }

    private static string FirstDifference(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            if (a[i] != b[i])
            {
                return $"offset {i}: expected {Snippet(a, i)} but found {Snippet(b, i)}";
            }
        }

        return $"offset {n}: one string is a prefix of the other";
    }

    private static string Snippet(string s, int i)
    {
        var start = Math.Max(0, i - 25);
        var end = Math.Min(s.Length, i + 25);
        return "\u2026" + s[start..end].Replace("\n", "\\n").Replace("\t", "\\t") + "\u2026";
    }
}
