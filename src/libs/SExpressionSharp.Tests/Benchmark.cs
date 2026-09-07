using System.Diagnostics;
using SExpressionSharp.Tests.Legacy;
using Xunit.Abstractions;

namespace SExpressionSharp.Tests;

/// <summary>
/// Before/after measurement against the implementation this branch replaces, which is vendored
/// verbatim under <c>Legacy/</c>. Both run in the same process on the same input, so the numbers
/// are comparable; the machine is whatever the suite runs on, so treat the ratios as the result.
/// </summary>
public class Benchmark
{
    private const string Subject = "templates/orbion-esp32s3/orbion-esp32s3.kicad_sch";
    private readonly ITestOutputHelper _out;

    public Benchmark(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Parse_And_Write_AreFasterAndLeanerThanTheImplementationTheyReplace()
    {
        if (Corpus.Missing)
        {
            return;
        }

        var text = Corpus.Read(Subject);
        var report = new List<string>
        {
            $"corpus file : {Subject}",
            $"size        : {text.Length:N0} chars",
            string.Empty,
        };
        report.Add($"{"case",-34}{"ms/op",10}{"MB alloc/op",14}");

        var legacyTree = new LegacySExpressionParser().Parse(text);
        var newDoc = new SExpressionParser().ParseAll(text);
        var canonical = new SExpressionWriterOptions { Format = SExpressionFormat.Canonical };

        // Cases are measured round-robin rather than one after another: on a shared machine a burst
        // of contention otherwise lands entirely on whichever case happened to be running.
        var cases = new (string Name, Action Run)[]
        {
            ("tokenizer floor (builds nothing)", () => _ = ScanFloor(text)),
            ("parse (legacy)", () => _ = new LegacySExpressionParser().Parse(text)),
            ("parse (new)", () => _ = new SExpressionParser().ParseAll(text)),
            ("parse (new, no source/pool)", () => _ = new SExpressionParser(new SExpressionParserOptions { TrackSource = false, PoolStrings = false }).ParseAll(text)),
            ("write (legacy)", () => _ = new LegacySExpressionWriter().Write(legacyTree)),
            ("write (new, canonical)", () => _ = new SExpressionWriter(canonical).Write(newDoc)),
            ("write (new, preserving)", () => _ = new SExpressionWriter().Write(newDoc)),
            ("round trip (legacy)", () => _ = new LegacySExpressionWriter().Write(new LegacySExpressionParser().Parse(text))),
            ("round trip (new)", () => _ = new SExpressionParser().ParseAll(text).ToText()),
        };

        var results = MeasureAll(cases);
        foreach (var (name, _) in cases)
        {
            Row(report, name, results[name]);
        }

        var legacyParse = results["parse (legacy)"];
        var newParse = results["parse (new)"];
        var legacyRoundTrip = results["round trip (legacy)"];
        var newRoundTrip = results["round trip (new)"];
        var legacyWrite = results["write (legacy)"];
        var newWrite = results["write (new, canonical)"];

        report.Add(string.Empty);
        report.Add($"parse      : {legacyParse.Milliseconds / newParse.Milliseconds:F2}x faster, {(double)legacyParse.Bytes / newParse.Bytes:F2}x less allocated");
        report.Add($"write      : {legacyWrite.Milliseconds / newWrite.Milliseconds:F2}x faster, {(double)legacyWrite.Bytes / newWrite.Bytes:F2}x less allocated (canonical; preserving is a substring)");
        report.Add($"round trip : {legacyRoundTrip.Milliseconds / newRoundTrip.Milliseconds:F2}x faster, {(double)legacyRoundTrip.Bytes / newRoundTrip.Bytes:F2}x less allocated");

        var text2 = string.Join('\n', report);
        _out.WriteLine(text2);
        var dump = Environment.GetEnvironmentVariable("ORBION_SEXPR_BENCH_OUT");
        if (!string.IsNullOrEmpty(dump))
        {
            File.WriteAllText(dump, text2);
        }

        // Regression guards, not the headline. Parse is held to a tolerance rather than a win: it
        // records source spans and quoting styles the old parser did not, and lands within noise of
        // it while allocating half as much. The end-to-end round trip must be a strict win.
        Assert.True(newParse.Bytes < legacyParse.Bytes, $"parse allocates more: {newParse.Bytes:N0} vs {legacyParse.Bytes:N0}");
        Assert.True(newParse.Milliseconds <= legacyParse.Milliseconds * 1.25, $"parse got slower: {newParse.Milliseconds:F2} ms vs {legacyParse.Milliseconds:F2} ms");
        Assert.True(newRoundTrip.Milliseconds < legacyRoundTrip.Milliseconds, $"round trip got slower: {newRoundTrip.Milliseconds:F2} ms vs {legacyRoundTrip.Milliseconds:F2} ms");
        Assert.True(newRoundTrip.Bytes < legacyRoundTrip.Bytes, $"round trip allocates more: {newRoundTrip.Bytes:N0} vs {legacyRoundTrip.Bytes:N0}");
        Assert.True(newWrite.Milliseconds < legacyWrite.Milliseconds, $"canonical write got slower: {newWrite.Milliseconds:F2} ms vs {legacyWrite.Milliseconds:F2} ms");
    }

    /// <summary>
    /// The same scanning rules with no tree built at all: the floor any parser of this text pays,
    /// so the rest of the parse number is tree construction.
    /// </summary>
    private static int ScanFloor(string text)
    {
        var s = text.AsSpan();
        var items = 0;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c is ' ' or '\t' or '\r' or '\n' or '(' or ')')
            {
                i++;
                continue;
            }

            if (c == '"')
            {
                var rel = s[(i + 1)..].IndexOf('"');
                i = rel < 0 ? s.Length : i + rel + 2;
            }
            else
            {
                while (i < s.Length && s[i] is not (' ' or '\t' or '\r' or '\n' or '(' or ')'))
                {
                    i++;
                }
            }

            items++;
        }

        return items;
    }

    private static void Row(List<string> report, string name, Result r) =>
        report.Add($"{name,-34}{r.Milliseconds,10:F2}{r.Bytes / 1024.0 / 1024.0,14:F2}");

    private static Dictionary<string, Result> MeasureAll((string Name, Action Run)[] cases)
    {
        // 200 warm-up calls each: tiered compilation needs well over a hundred before this code runs
        // at its steady-state speed, and measuring before that silently reports the tier-0 build.
        foreach (var (_, run) in cases)
        {
            for (var i = 0; i < 200; i++)
            {
                run();
            }
        }

        var best = cases.ToDictionary(c => c.Name, _ => new Result(double.MaxValue, 0));
        for (var round = 0; round < 5; round++)
        {
            foreach (var (name, run) in cases)
            {
                var r = MeasureOnce(run);
                if (r.Milliseconds < best[name].Milliseconds)
                {
                    best[name] = r;
                }
            }
        }

        return best;
    }

    private static Result MeasureOnce(Action action)
    {

        const int Iterations = 20;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            action();
        }

        sw.Stop();
        return new Result(sw.Elapsed.TotalMilliseconds / Iterations, (GC.GetAllocatedBytesForCurrentThread() - before) / Iterations);
    }

    private readonly record struct Result(double Milliseconds, long Bytes);
}
