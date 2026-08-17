using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Git.Execution;

public sealed class GitCommandRunner : IGitCommandRunner
{
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(workingDirectory);
        }

        options ??= new GitCommandOptions();
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = options.StandardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (options.EnvironmentVariables is not null)
        {
            foreach ((string name, string value) in options.EnvironmentVariables)
            {
                startInfo.Environment[name] = value;
            }
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new GitCommandException("Git process could not be started.", -1);

            try
            {
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

                if (options.StandardInput is not null)
                {
                    await process.StandardInput.WriteAsync(options.StandardInput.AsMemory(), cancellationToken);
                    process.StandardInput.Close();
                }

                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(outputTask, errorTask);

                GitCommandResult result = new(
                    process.ExitCode,
                    await outputTask,
                    await errorTask);
                if (!result.IsSuccess && options.ThrowOnError)
                {
                    string message = string.IsNullOrWhiteSpace(result.StandardError)
                        ? result.StandardOutput.Trim()
                        : result.StandardError.Trim();
                    throw new GitCommandException(message, result.ExitCode);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }

                throw;
            }
        }
        catch (Win32Exception exception)
        {
            throw new FileNotFoundException("Git executable was not found.", "git", exception);
        }
    }

}
