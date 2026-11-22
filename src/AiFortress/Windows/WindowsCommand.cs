using System.Diagnostics;

namespace AiFortress;

internal static class WindowsCommand
{
    internal sealed record Result(int ExitCode, string Message);

    public static async Task<Result> RunAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, executable))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException(executable);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); }
            catch (InvalidOperationException) { }
            throw new TimeoutException(executable);
        }

        var message = error.Result.Trim();
        if (message.Length == 0)
            message = output.Result.Trim();
        return new Result(process.ExitCode, message.Length != 0 ? message : $"{executable} ({process.ExitCode})");
    }
}
