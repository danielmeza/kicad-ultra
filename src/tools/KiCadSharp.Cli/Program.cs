using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

using SExpressionSharp;

namespace KiCadSharp.Cli
{
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitProblemFound = 1;
        private const int ExitCannotRead = 2;

        internal static int Main(string[] args)
        {
            var root = new RootCommand(
                "Work with KiCad's s-expression files: summarise their structure, reformat them through a " +
                "parse/write round trip, read a value by path, and check that they parse.");

            root.Add(BuildParseCommand());
            root.Add(BuildFmtCommand());
            root.Add(BuildQueryCommand());
            root.Add(BuildValidateCommand());

            return root.Parse(args).Invoke();
        }

        private static Argument<FileInfo> FileArgument(string description) =>
            new("file") { Description = description };

        // ---------------------------------------------------------------- parse

        private static Command BuildParseCommand()
        {
            var file = FileArgument("The s-expression file to summarise (.kicad_sch, .kicad_pcb, .kicad_dru, .kicad_sym, ...).");
            var json = new Option<bool>("--json") { Description = "Emit the summary as JSON instead of a table." };

            var command = new Command("parse", "Parse a file and print a structural summary: form count, top-level heads, node and value counts, depth.");
            command.Add(file);
            command.Add(json);

            command.SetAction(parse => RunParse(parse.GetRequiredValue(file), parse.GetValue(json)));
            return command;
        }

        private static int RunParse(FileInfo file, bool asJson)
        {
            if (!TryReadText(file, out var text))
            {
                return ExitCannotRead;
            }

            var scan = SourceScanner.Scan(text);

            SExpressionDocument document;
            try
            {
                document = SExpressionSource.ReadText(text);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"{file.FullName}: cannot parse: {ex.Message}");
                return ExitProblemFound;
            }

            var perForm = new List<FormStructure>(document.Forms.Count);
            var nodes = 0;
            var values = 0;
            var depth = 0;
            foreach (var form in document.Forms)
            {
                var structure = Structure.Describe(form);
                perForm.Add(structure);
                nodes += structure.Nodes;
                values += structure.Values;
                depth = Math.Max(depth, structure.Depth);
            }

            var heads = Structure.ChildTokenCounts(document.Forms);

            if (asJson)
            {
                var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["file"] = file.FullName,
                    ["bytes"] = text.Length,
                    ["formsParsed"] = document.Forms.Count,
                    ["formsInFile"] = scan.TopLevelForms,
                    ["commentLines"] = scan.CommentLines,
                    ["nodes"] = nodes,
                    ["values"] = values,
                    ["depth"] = depth,
                    ["multiFormApiAvailable"] = SExpressionSource.SupportsMultipleForms,
                    ["topLevelHeads"] = ToHeadList(heads),
                };

                Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"file          {file.FullName}");
                Console.WriteLine($"bytes         {text.Length.ToString(CultureInfo.InvariantCulture)}");
                Console.WriteLine($"top-level     {document.Forms.Count} form(s) parsed, {scan.TopLevelForms} present in the file");
                Console.WriteLine($"comments      {scan.CommentLines} line(s)");
                Console.WriteLine($"nodes         {nodes}");
                Console.WriteLine($"values        {values}");
                Console.WriteLine($"max depth     {depth}");
                Console.WriteLine();
                Console.WriteLine("top-level heads");
                foreach (var head in heads)
                {
                    Console.WriteLine($"  {head.Key,-24} {head.Value}");
                }
            }

            WarnIfTruncated(document, scan);
            return ExitOk;
        }

        private static List<Dictionary<string, object?>> ToHeadList(IReadOnlyList<KeyValuePair<string, int>> heads)
        {
            var list = new List<Dictionary<string, object?>>(heads.Count);
            foreach (var head in heads)
            {
                list.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { ["token"] = head.Key, ["count"] = head.Value });
            }

            return list;
        }

        // ------------------------------------------------------------------ fmt

        private static Command BuildFmtCommand()
        {
            var file = FileArgument("The s-expression file to reformat.");
            var inPlace = new Option<bool>("--in-place", "-i") { Description = "Rewrite the file instead of printing to stdout." };

            var command = new Command("fmt", "Round-trip a file through the parser and the writer. The result is verified by re-parsing it and comparing the trees, so this is also the proof that the round trip is lossless.");
            command.Add(file);
            command.Add(inPlace);

            command.SetAction(parse => RunFmt(parse.GetRequiredValue(file), parse.GetValue(inPlace)));
            return command;
        }

        private static int RunFmt(FileInfo file, bool inPlace)
        {
            if (!TryReadText(file, out var text))
            {
                return ExitCannotRead;
            }

            var scan = SourceScanner.Scan(text);

            SExpressionDocument document;
            try
            {
                document = SExpressionSource.ReadText(text);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"{file.FullName}: cannot parse: {ex.Message}");
                return ExitProblemFound;
            }

            var writer = new SExpressionWriter();
            var builder = new StringBuilder();
            foreach (var form in document.Forms)
            {
                builder.Append(writer.Write(form));
            }

            var formatted = builder.ToString();

            // Re-parse the output and compare the trees. A pretty printer is allowed to move whitespace;
            // it is not allowed to change the tree.
            SExpressionDocument reparsed;
            try
            {
                reparsed = SExpressionSource.ReadText(formatted);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"{file.FullName}: the writer produced output the parser rejects: {ex.Message}");
                return ExitProblemFound;
            }

            if (reparsed.Forms.Count != document.Forms.Count)
            {
                Console.Error.WriteLine($"{file.FullName}: round trip changed the form count: {document.Forms.Count} -> {reparsed.Forms.Count}");
                return ExitProblemFound;
            }

            for (var i = 0; i < document.Forms.Count; i++)
            {
                if (!Structure.SameTree(document.Forms[i], reparsed.Forms[i], out var difference))
                {
                    Console.Error.WriteLine($"{file.FullName}: round trip is not lossless: {difference}");
                    return ExitProblemFound;
                }
            }

            if (!inPlace)
            {
                Console.Out.Write(formatted);
                return ExitOk;
            }

            // Refuse to write back anything the round trip would silently drop.
            if (!document.AllFormsRead && scan.TopLevelForms > document.Forms.Count)
            {
                Console.Error.WriteLine(
                    $"{file.FullName}: refusing --in-place. The file has {scan.TopLevelForms} top-level forms but the " +
                    $"installed SExpressionSharp only returns the first, so writing back would delete the rest.");
                return ExitProblemFound;
            }

            if (scan.CommentLines > 0)
            {
                Console.Error.WriteLine(
                    $"{file.FullName}: refusing --in-place. The file has {scan.CommentLines} comment line(s) and the " +
                    $"parser does not carry comments through the round trip, so writing back would delete them.");
                return ExitProblemFound;
            }

            File.WriteAllText(file.FullName, formatted);
            Console.Error.WriteLine($"{file.FullName}: rewritten ({text.Length} -> {formatted.Length} bytes, tree unchanged).");
            return ExitOk;
        }

        // ---------------------------------------------------------------- query

        private static Command BuildQueryCommand()
        {
            var file = FileArgument("The s-expression file to read.");
            var path = new Argument<string>("path")
            {
                Description = "Dotted or slashed path, e.g. 'kicad_sch.version' or 'kicad_sch/symbol[2]/property'. " +
                              "A segment names a child token; '[n]' picks the zero-based nth match.",
            };
            var all = new Option<bool>("--all", "-a") { Description = "Print every match instead of only the first." };

            var command = new Command("query", "Read a value out of a file by path.");
            command.Add(file);
            command.Add(path);
            command.Add(all);

            command.SetAction(parse => RunQuery(parse.GetRequiredValue(file), parse.GetRequiredValue(path), parse.GetValue(all)));
            return command;
        }

        private static int RunQuery(FileInfo file, string path, bool all)
        {
            if (!TryReadText(file, out var text))
            {
                return ExitCannotRead;
            }

            var scan = SourceScanner.Scan(text);

            SExpressionDocument document;
            try
            {
                document = SExpressionSource.ReadText(text);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"{file.FullName}: cannot parse: {ex.Message}");
                return ExitProblemFound;
            }

            IReadOnlyList<SExpression> matches;
            try
            {
                matches = SExpressionPath.Resolve(document, path);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"bad path '{path}': {ex.Message}");
                return ExitProblemFound;
            }

            if (matches.Count == 0)
            {
                Console.Error.WriteLine($"{file.FullName}: no node matches '{path}'.");
                WarnIfTruncated(document, scan);
                return ExitProblemFound;
            }

            var limit = all ? matches.Count : 1;
            for (var i = 0; i < limit; i++)
            {
                var node = matches[i];
                Console.WriteLine(node.Values.Count > 0
                    ? string.Join(' ', node.Values)
                    : $"({node.Token} ... {node.Children.Count} child form(s))");
            }

            if (!all && matches.Count > 1)
            {
                Console.Error.WriteLine($"note: {matches.Count} nodes match '{path}'; pass --all to print them all.");
            }

            WarnIfTruncated(document, scan);
            return ExitOk;
        }

        // ------------------------------------------------------------- validate

        private static Command BuildValidateCommand()
        {
            var file = FileArgument("The s-expression file to check.");

            var command = new Command("validate", "Check that a file is structurally sound and that the parser accepts it. Exits non-zero when it is not.");
            command.Add(file);

            command.SetAction(parse => RunValidate(parse.GetRequiredValue(file)));
            return command;
        }

        private static int RunValidate(FileInfo file)
        {
            if (!TryReadText(file, out var text))
            {
                return ExitCannotRead;
            }

            var scan = SourceScanner.Scan(text);
            var failed = false;

            foreach (var problem in scan.Problems)
            {
                Console.Error.WriteLine($"{file.FullName}: {problem}");
                failed = true;
            }

            if (scan.TopLevelForms == 0)
            {
                Console.Error.WriteLine($"{file.FullName}: the file contains no s-expression form.");
                failed = true;
            }

            SExpressionDocument? document = null;
            try
            {
                document = SExpressionSource.ReadText(text);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"{file.FullName}: the parser rejects this file: {ex.Message}");
                failed = true;
            }

            if (failed)
            {
                return ExitProblemFound;
            }

            var parsedForms = document!.Forms.Count;
            Console.WriteLine(
                $"{file.FullName}: ok — {scan.TopLevelForms} top-level form(s), {scan.CommentLines} comment line(s), " +
                $"{parsedForms} form(s) reached the parser.");

            WarnIfTruncated(document, scan);
            return ExitOk;
        }

        // ---------------------------------------------------------------- shared

        private static bool TryReadText(FileInfo file, out string text)
        {
            try
            {
                text = File.ReadAllText(file.FullName);
                return true;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"{file.FullName}: {ex.Message}");
                text = string.Empty;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"{file.FullName}: {ex.Message}");
                text = string.Empty;
                return false;
            }
        }

        private static void WarnIfTruncated(SExpressionDocument document, ScanResult scan)
        {
            if (document.AllFormsRead || scan.TopLevelForms <= document.Forms.Count)
            {
                return;
            }

            Console.Error.WriteLine(
                $"warning: only the first of {scan.TopLevelForms} top-level forms was read. The installed " +
                $"SExpressionSharp has no ParseAll/ParseAllFile, and Parse returns a single form. " +
                $"Everything after the first form was ignored.");
        }
    }
}
