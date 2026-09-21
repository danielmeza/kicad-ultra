using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services.EasyEda2KiCad;

/// <summary>
/// What a run of an external program left behind.
/// </summary>
/// <param name="ExitCode">The program's exit code. Meaningless when <paramref name="TimedOut"/> is set.</param>
/// <param name="StandardOutput">Everything it wrote to stdout.</param>
/// <param name="StandardError">Everything it wrote to stderr.</param>
/// <param name="TimedOut">It was still running when the timeout expired, and was stopped.</param>
public sealed record ProcessOutcome(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

/// <summary>
/// Runs another program as a separate process: never in this one, and never through a shell.
/// </summary>
internal static class ExternalProcess
{
    /// <summary>How long to wait for a stopped process, and its output pipes, to go away.</summary>
    private static readonly TimeSpan StopGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Starts <paramref name="fileName"/> with <paramref name="arguments"/>, waits for it to exit, and
    /// returns what it printed. A run that outlives <paramref name="timeout"/> is stopped, with the
    /// processes it started, and reported as <see cref="ProcessOutcome.TimedOut"/>.
    /// </summary>
    /// <param name="fileName">The program to start: a full path, never a shell command line.</param>
    /// <param name="arguments">Its arguments, one per entry.</param>
    /// <param name="workingDirectory">The directory it runs in.</param>
    /// <param name="environment">Variables to set or replace in the inherited environment.</param>
    /// <param name="timeout">How long it may run before it is stopped.</param>
    /// <param name="cancellationToken">Stops it early.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled. The
    /// process, if it had started, has been stopped.</exception>
    /// <exception cref="Win32Exception"><paramref name="fileName"/> could not be started.</exception>
    public static async Task<ProcessOutcome> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // One entry per argument, handed to the program exactly as it is. Never one command line built
        // by concatenation, where a path with a space in it, or a crafted value, could be split into or
        // read as further arguments.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string> variable in environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        _ = process.Start();

        // Nothing is ever typed into it, so a program that asks a question fails at once instead of
        // waiting for the timeout.
        process.StandardInput.Close();

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        var stopped = false;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process);
            stopped = true;
        }

        // Once the process tree is gone its pipes close and both reads finish. The bound is for a
        // descendant that escaped the kill and still holds a copy of them.
        Task bothRead = Task.WhenAll(standardOutput, standardError);
        try
        {
            await bothRead.WaitAsync(StopGracePeriod, CancellationToken.None);
        }
        catch (TimeoutException)
        {
        }

        return stopped && cancellationToken.IsCancellationRequested
            ? throw new OperationCanceledException(cancellationToken)
            : new ProcessOutcome(
                stopped ? -1 : process.ExitCode,
                standardOutput.IsCompletedSuccessfully ? standardOutput.Result : string.Empty,
                standardError.IsCompletedSuccessfully ? standardError.Result : string.Empty,
                stopped);
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // It exited on its own in the meantime, or cannot be signalled; either way there is
            // nothing more to stop.
        }

        using var grace = new CancellationTokenSource(StopGracePeriod);
        try
        {
            await process.WaitForExitAsync(grace.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
