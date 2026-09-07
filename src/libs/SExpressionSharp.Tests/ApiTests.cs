using System.Globalization;

namespace SExpressionSharp.Tests;

/// <summary>
/// The behaviour the library promises, on inputs small enough to read.
/// </summary>
public class ApiTests
{
    // ------------------------------------------------------------------------------- fidelity

    [Fact]
    public void QuotingIsRemembered_NotGuessed()
    {
        const string Src = "(a \"quoted\" bare \"7\" 7)";
        var doc = SDocument.Parse(Src);

        var kinds = doc.Root!.Items.AsSpan().ToArray().Select(i => i.QuoteStyle).ToArray();
        Assert.Equal([SQuoteStyle.Quoted, SQuoteStyle.Bare, SQuoteStyle.Quoted, SQuoteStyle.Bare], kinds);
        Assert.Equal(Src, doc.ToText());
        Assert.Equal("(a \"quoted\" bare \"7\" 7)\n", doc.ToText(Canonical));
    }

    [Fact]
    public void SourceOrderOfValuesAndChildrenIsKept()
    {
        var doc = SDocument.Parse("(a 1 (b) 2 (c) 3)");
        var root = doc.Root!;

        Assert.Equal(["1", "2", "3"], root.Values.ToArray());
        Assert.Equal(["b", "c"], root.Children.Select(c => c.Token));
        Assert.Equal([SItemKind.Atom, SItemKind.Expression, SItemKind.Atom, SItemKind.Expression, SItemKind.Atom], root.Items.AsSpan().ToArray().Select(i => i.Kind));
        Assert.Equal("(a 1 (b) 2 (c) 3)", doc.ToText());
    }

    [Fact]
    public void CommentsSurviveInsideAndBetweenForms()
    {
        const string Src = "# leading\n(rule \"x\"\n\t# why\n\t(constraint clearance))\n\n# trailing\n";
        var doc = SDocument.Parse(Src);

        Assert.Equal([" leading", " trailing"], doc.Comments);
        Assert.Equal([" why"], doc.Root!.Comments);
        Assert.Equal(Src, doc.ToText());
    }

    [Fact]
    public void CommentsCanBeTurnedOff_ForDialectsWhereHashIsAnAtom()
    {
        var parser = new SExpressionParser(new SExpressionParserOptions { CommentPrefix = null });
        var doc = parser.ParseAll("(net #PWR01)");
        Assert.Equal("#PWR01", doc.Root!.GetValue(0));
        Assert.Empty(doc.Root.Comments);
    }

    // ------------------------------------------------------------------------ multi-form files

    [Fact]
    public void ParseAll_ReturnsEveryTopLevelForm_WhileParseReturnsTheFirst()
    {
        const string Src = "(version 1)\n\n# note\n(rule \"a\")\n(rule \"b\")\n";
        Assert.Equal("version", new SExpressionParser().Parse(Src).Token);

        var doc = SDocument.Parse(Src);
        Assert.Equal(3, doc.Count);
        Assert.Equal(["version", "rule", "rule"], doc.Select(f => f.Token));
        Assert.Equal("b", doc[2].GetValue(0));
        Assert.Equal(Src, doc.ToText());
    }

    [Fact]
    public void ParseOnAnEmptyDocumentThrowsWithAPosition()
    {
        var ex = Assert.Throws<SExpressionFormatException>(() => new SExpressionParser().Parse("   \n  "));
        Assert.IsAssignableFrom<FormatException>(ex);
    }

    [Fact]
    public void AnUnbalancedFileReportsLineAndColumn()
    {
        var ex = Assert.Throws<SExpressionFormatException>(() => SDocument.Parse("(a\n  (b\n"));
        Assert.Equal(3, ex.Line);
        Assert.Contains("line 3", ex.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------- minimal edits

    [Fact]
    public void EditingOneValueChangesOnlyThatValuesBytes()
    {
        const string Src = "(kicad_sch\n\t(version 20250114)\n\t(generator \"eeschema\")\n\t(uuid \"abc\")\n)\n";
        var doc = SDocument.Parse(Src);
        doc.Root!["generator"]!.SetValue(0, "kicad-ultra");

        Assert.Equal(Src.Replace("eeschema", "kicad-ultra", StringComparison.Ordinal), doc.ToText());
    }

    [Fact]
    public void EditingDeepInAFileLeavesEverySiblingByteIdentical()
    {
        var text = "(root\n\t(a (x 1) (y 2))\n\t(b\n\t\t(deep   (target  \"old\" )  )\n\t)\n\t(c 3)\n)\n";
        var doc = SDocument.Parse(text);
        doc.Find("root/b/deep/target")!.SetValue(0, "new");

        Assert.Equal(text.Replace("\"old\"", "\"new\"", StringComparison.Ordinal), doc.ToText());
    }

    [Fact]
    public void AddingAChildReformatsOnlyTheEnclosingForm()
    {
        const string Src = "(root\n\t(keep   me   \"as-is\")\n\t(edit 1)\n)\n";
        var doc = SDocument.Parse(Src);
        doc.Root!["edit"]!.CreateChild("added", "9");

        var written = doc.ToText();
        Assert.Contains("(keep   me   \"as-is\")", written, StringComparison.Ordinal);
        Assert.Contains("(added 9)", written, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnmodifiedDocumentIsReturnedWithoutRebuildingIt()
    {
        const string Src = "(a 1)\n";
        var doc = SDocument.Parse(Src);
        Assert.False(doc.IsModified);
        Assert.Same(Src, doc.ToText());
    }

    [Fact]
    public void SettingAValueToWhatItAlreadyIsDoesNotDirtyTheTree()
    {
        var doc = SDocument.Parse("(a \"x\")\n");
        doc.Root!.SetValue(0, "x");
        Assert.False(doc.IsModified);
    }

    // ------------------------------------------------------------------------------ ergonomics

    [Fact]
    public void ADefaultConstructedViewSaysSoInsteadOfThrowingANullReference()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => default(SValueCollection).Add("x"));
        Assert.Contains("not attached", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneLinerLookupsReadNaturally()
    {
        var doc = SDocument.Parse("(kicad_sch (lib_symbols (symbol \"R\" (property \"Reference\" \"R\")) (symbol \"C\")) (uuid \"u1\"))");

        Assert.Equal("u1", doc.Root!["uuid"]!.GetValue(0));
        Assert.Equal("u1", doc.Root.GetChildValue("uuid"));
        Assert.Equal(["R", "C"], doc.FindAll("kicad_sch/lib_symbols/symbol").Select(s => s.GetValue(0)));
        Assert.Equal("Reference", doc.Find("kicad_sch/lib_symbols/symbol/property")!.GetValue(0));
        Assert.Equal(2, doc.Root.Descendants("symbol").Count());
    }

    [Fact]
    public void TypedReadsAndWritesUseTheInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var node = SDocument.Parse("(at 152.4 -3.81 90)").Root!;

            Assert.Equal(152.4, node.GetValueAsDouble(0));
            Assert.True(node.TryGetValue<double>(1, out var y));
            Assert.Equal(-3.81, y);
            Assert.Equal(90, node.GetRequiredValue<int>(2));
            Assert.Equal(0.0, node.GetValue(9, 0.0));

            node.SetValue(0, 1.5);
            Assert.Equal("1.5", node.GetValue(0));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void SetChildValueCreatesTheChildWhenItIsMissing()
    {
        var node = SDocument.Parse("(symbol)").Root!;
        node.SetChildValue("uuid", "1234", SQuoteStyle.Quoted);
        Assert.Equal("(symbol\n\t(uuid \"1234\")\n)\n", node.ToText(Canonical));
    }

    [Fact]
    public void ValuesAndChildrenAreLiveViewsOverTheSameItems()
    {
        var node = new SExpression("at", "1", "2");
        node.CreateChild("unit", "3");

        Assert.Equal(2, node.Values.Count);
        Assert.Single(node.Children);

        node.Values.Add("90");
        Assert.Equal(["1", "2", "90"], node.Values.ToArray());

        // A value added after a child still writes before it, keeping the familiar KiCad shape.
        Assert.Equal("(at 1 2 90\n\t(unit 3)\n)\n", node.ToText(Canonical));

        Assert.True(node.Children.Remove(node.Children[0]));
        Assert.Empty(node.Children);
        Assert.Equal("(at 1 2 90)\n", node.ToText(Canonical));
    }

    [Fact]
    public void ACloneKeepsItsSource_SoAnUntouchedCopyStillWritesByteForByte()
    {
        const string Src = "(a  1   (b \"x\") )";
        var clone = SDocument.Parse(Src).Root!.Clone();
        Assert.Null(clone.Parent);
        Assert.Equal(Src, clone.ToText());

        clone.SetValue(0, "2");
        Assert.Equal(Src.Replace(" 1 ", " 2 ", StringComparison.Ordinal), clone.ToText());
    }

    [Fact]
    public void ATreeBuiltInMemoryIsFormattedFromScratch()
    {
        var root = new SExpression("kicad_sch");
        root.CreateChild("version", "20250114");
        root.CreateChild("generator").AddValue("kicad-ultra", SQuoteStyle.Quoted);
        root.CreateChild("paper").AddValue("A4 landscape");

        Assert.Equal(
            "(kicad_sch\n\t(version 20250114)\n\t(generator \"kicad-ultra\")\n\t(paper \"A4 landscape\")\n)\n",
            root.ToText());
    }

    [Fact]
    public void AnAtomThatWouldNotSurviveBareIsQuotedEvenWhenAskedToBeBare()
    {
        var node = new SExpression("comment");
        node.AddValue("two words", SQuoteStyle.Bare);
        node.AddValue("#hash", SQuoteStyle.Bare);
        node.AddValue(string.Empty, SQuoteStyle.Bare);
        Assert.Equal("(comment \"two words\" \"#hash\" \"\")\n", node.ToText(Canonical));
    }

    [Fact]
    public void EscapedQuotesRoundTripBothWays()
    {
        const string Src = "(property \"Value\" \"a \\\"quoted\\\" word\")";
        var doc = SDocument.Parse(Src);
        Assert.Equal("a \"quoted\" word", doc.Root!.GetValue(1));
        Assert.Equal(Src, doc.ToText());
        Assert.Equal(Src + "\n", doc.ToText(Canonical));
    }

    [Fact]
    public async Task TheAsyncPathReadsAndWritesTheSameBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sexpr-{Guid.NewGuid():N}.kicad_dru");
        const string Src = "(version 1)\n\n# note\n(rule \"a\")\n";
        await File.WriteAllTextAsync(path, Src);
        try
        {
            var doc = await SDocument.LoadAsync(path);
            Assert.Equal(2, doc.Count);

            var copy = path + ".copy";
            await doc.SaveAsync(copy);
            Assert.Equal(Src, await File.ReadAllTextAsync(copy));
            File.Delete(copy);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SExpressionWriterOptions Canonical => new() { Format = SExpressionFormat.Canonical };
}
