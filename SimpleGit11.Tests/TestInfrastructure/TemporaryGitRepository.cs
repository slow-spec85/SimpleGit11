using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Tests.TestInfrastructure;

internal sealed class TemporaryGitRepository : IAsyncDisposable
{
    private readonly TemporaryDirectory _temporaryDirectory;

    private TemporaryGitRepository(TemporaryDirectory temporaryDirectory)
    {
        _temporaryDirectory = temporaryDirectory;
        Repository = new RepositoryInfo(
            temporaryDirectory.Path,
            "test-repository",
            "main",
            mainWorktreePath: temporaryDirectory.Path);
    }

    public RepositoryInfo Repository { get; }

    public static async Task<TemporaryGitRepository> CreateAsync()
    {
        TemporaryDirectory temporaryDirectory = new();
        TemporaryGitRepository repository = new(temporaryDirectory);

        try
        {
            await repository.RunGitAsync("init", "--initial-branch=main");
            await repository.RunGitAsync("config", "user.name", "SimpleGit11 Tests");
            await repository.RunGitAsync("config", "user.email", "simplegit11-tests@example.invalid");
            await repository.RunGitAsync("config", "--local", "remote.pushDefault", "");
            return repository;
        }
        catch
        {
            temporaryDirectory.Dispose();
            throw;
        }
    }

    public string WriteFile(string relativePath, string content)
    {
        return _temporaryDirectory.CreateFile(relativePath, content);
    }

    public string ReadFile(string relativePath)
    {
        return File.ReadAllText(_temporaryDirectory.GetPath(relativePath));
    }

    public bool FileExists(string relativePath)
    {
        return File.Exists(_temporaryDirectory.GetPath(relativePath));
    }

    public async Task CommitAllAsync(string message = "test commit")
    {
        await RunGitAsync("add", "--all");
        await RunGitAsync("commit", "-m", message);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        _temporaryDirectory.Dispose();
    }

    public async Task<string> RunGitAsync(params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = _temporaryDirectory.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("Git process could not be started.");
            }

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git exited with code {process.ExitCode}: {error.Trim()}");
            }

            return output.Trim();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Git is required to run the integration tests.",
                exception);
        }
    }
}
