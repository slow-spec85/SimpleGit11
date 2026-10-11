using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Execution.Local;

namespace SimpleGit11.Services.Git.Execution;

/// <summary>Coordinates Git's sequence/message editors in the repository's execution environment.</summary>
public sealed class GitEditorService : IGitEditorService
{
    private readonly IGitCommandRunner _runner;
    private readonly IExecutionContextService? _executionContext;
    private readonly Func<bool, string, Task<string?>> _editText;
    private readonly string _helperPath;

    public GitEditorService(
        IGitCommandRunner runner,
        Func<bool, string, Task<string?>> editText,
        IExecutionContextService? executionContext = null,
        string? helperPath = null)
    {
        _runner = runner;
        _editText = editText;
        _executionContext = executionContext;
        _helperPath = helperPath ?? Path.Combine(AppContext.BaseDirectory, "SimpleGit11.GitEditor.exe");
    }

    public async Task<GitCommandResult> RunAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        Func<bool, string, Task<string?>>? editText = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? preparedMessages = null)
    {
        Services.Execution.ExecutionContext? context = _executionContext?.Current;
        IExecutionRuntime? runtime = context?.Runtime;
        bool local = context?.IsLocal ?? true;
        IGitCommandRunner runner = runtime?.Git ?? _runner;
        IRepositoryFileSystem files = runtime?.Files ?? new LocalRepositoryFileSystem();
        IRepositoryPathService paths = runtime?.Paths ?? new LocalRepositoryPathService();
        if (local && !File.Exists(_helperPath))
        {
            throw new FileNotFoundException("The SimpleGit11 Git editor executable is missing.", _helperPath);
        }

        string gitDirectory = (await runner.RunAsync(repository.Path,
            ["rev-parse", "--path-format=absolute", "--git-dir"], cancellationToken: cancellationToken)).StandardOutput.Trim();
        // Git owns this directory's lifetime, so drafts survive conflict/continue and
        // application restarts, and disappear on rebase completion or abort.
        string messagesPath = paths.Combine(paths.Combine(gitDirectory, "rebase-merge"), "simplegit-messages.json");
        IReadOnlyDictionary<string, string>? messages = preparedMessages;
        if (messages is null && await files.FileExistsAsync(messagesPath, cancellationToken))
        {
            messages = JsonSerializer.Deserialize<Dictionary<string, string>>(await files.ReadAllBytesAsync(messagesPath, cancellationToken));
        }
        async Task<string?> EditRequestAsync(bool sequence, string text)
        {
            if (!sequence && await ReadPreparedMessageAsync(repository, runner, files, paths, gitDirectory, messages, cancellationToken) is string message)
            {
                return message;
            }
            string? answer = await (editText ?? _editText)(sequence, text);
            if (sequence && answer is not null && preparedMessages is { Count: > 0 })
            {
                await files.WriteAllBytesAtomicAsync(messagesPath, JsonSerializer.SerializeToUtf8Bytes(preparedMessages), cancellationToken);
            }
            return answer;
        }
        string prefix = paths.Combine(gitDirectory, "simplegit-editor-" + Guid.NewGuid().ToString("N"));
        if (paths.Style == RepositoryPathStyle.Windows)
        {
            prefix = prefix.Replace('\\', '/');
        }
        string requestPath = prefix + ".request";
        List<string> temporaryFiles = [requestPath, requestPath + ".tmp"];
        string editor;
        if (local)
        {
            editor = Quote(_helperPath.Replace('\\', '/'));
        }
        else
        {
            // A POSIX equivalent is used on SSH hosts; the Windows helper cannot execute there.
            string script = prefix + ".sh";
            temporaryFiles.Add(script);
            await files.WriteAllBytesAtomicAsync(script, Encoding.UTF8.GetBytes(RemoteEditorScript), cancellationToken);
            editor = "sh " + Quote(script);
        }

        Dictionary<string, string> environment = new()
        {
            ["GIT_SEQUENCE_EDITOR"] = editor + " sequence",
            ["GIT_EDITOR"] = editor + " message",
            ["SIMPLEGIT_EDITOR_SESSION"] = prefix,
            ["SIMPLEGIT_EDITOR_HOST"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        HashSet<string> handledRequests = [];
        using CancellationTokenSource commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<GitCommandResult>? command = null;
        Task? heartbeat = null;
        try
        {
            if (!local)
            {
                string lease = prefix + ".alive";
                temporaryFiles.Add(lease);
                await WriteHeartbeatAsync(files, lease, commandCancellation.Token);
                heartbeat = MaintainHeartbeatAsync(files, lease, commandCancellation.Token);
            }

            command = runner.RunAsync(repository.Path, arguments,
                new GitCommandOptions(ThrowOnError: false, EnvironmentVariables: environment), commandCancellation.Token);
            while (!command.IsCompleted)
            {
                if (heartbeat?.IsFaulted == true)
                {
                    await heartbeat;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (await files.FileExistsAsync(requestPath, cancellationToken))
                {
                    string request = Encoding.UTF8.GetString(await files.ReadAllBytesAsync(requestPath, cancellationToken));
                    int first = request.IndexOf('\n');
                    int second = first < 0 ? -1 : request.IndexOf('\n', first + 1);
                    if (first < 1 || second < 0)
                    {
                        throw new InvalidDataException("The Git editor request is malformed.");
                    }

                    string id = request[..first];
                    // IDs are generated by the helper, never interpreted as paths supplied by a todo command.
                    if (id.Length > 64 || !System.Linq.Enumerable.All(id, char.IsAsciiLetterOrDigit))
                    {
                        throw new InvalidDataException("The Git editor request identifier is invalid.");
                    }

                    if (handledRequests.Add(id))
                    {
                        string responsePath = prefix + "." + id + ".response";
                        temporaryFiles.Add(responsePath);
                        bool sequence = request[(first + 1)..second] == "sequence";
                        string text = request[(second + 1)..];
                        Task<string?> editing = EditRequestAsync(sequence, text);
                        if (await Task.WhenAny(editing, command) == command)
                        {
                            break;
                        }

                        string? result = await editing.WaitAsync(cancellationToken);
                        // WinUI text controls can return CR-only lines. Git needs LF boundaries
                        // to separate subject/body and apply its configured message cleanup.
                        result = result?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                        await files.WriteAllBytesAtomicAsync(responsePath,
                            Encoding.UTF8.GetBytes(result is null ? "0\n" : "1\n" + result), cancellationToken);
                    }
                }

                await Task.WhenAny(command, Task.Delay(150, cancellationToken));
            }

            return await command;
        }
        finally
        {
            await commandCancellation.CancelAsync();
            if (command is not null)
            {
                try
                {
                    await command;
                }
                catch (Exception)
                {
                    // The invocation above reports the command/callback failure; cleanup must preserve it.
                }
            }
            if (heartbeat is not null)
            {
                try
                {
                    await heartbeat;
                }
                catch (Exception)
                {
                    // A disconnected SSH host can also fail the heartbeat task.
                }
            }

            foreach (string temporaryFile in temporaryFiles)
            {
                try
                {
                    if (await files.FileExistsAsync(temporaryFile))
                    {
                        await files.DeleteFileAsync(temporaryFile);
                    }
                }
                catch (Exception)
                {
                    // Best-effort cleanup must not turn a completed Git operation into a failure.
                }
            }
        }
    }

    // The original commit is identified by REBASE_HEAD, rather than by editor-call
    // order: conflict recovery may request extra messages between reword steps.
    private static async Task<string?> ReadPreparedMessageAsync(RepositoryInfo repository, IGitCommandRunner runner,
        IRepositoryFileSystem files, IRepositoryPathService paths, string gitDirectory,
        IReadOnlyDictionary<string, string>? messages, CancellationToken cancellationToken)
    {
        if (messages is null || messages.Count == 0) { return null; }
        // Successful reword steps do not always set REBASE_HEAD. Git moves the
        // instruction to 'done' before invoking the message editor.
        string donePath = paths.Combine(paths.Combine(gitDirectory, "rebase-merge"), "done");
        if (await files.FileExistsAsync(donePath, cancellationToken))
        {
            string done = Encoding.UTF8.GetString(await files.ReadAllBytesAsync(donePath, cancellationToken));
            string? last = System.Linq.Enumerable.LastOrDefault(done.Split('\n'), line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));
            string[] parts = last?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries) ?? [];
            string? originalHash = parts.Length >= 2 && parts[0] is "reword" or "r" ? parts[1]
                : parts.Length >= 3 && parts[0] is "merge" or "m" && parts[1] == "-c" ? parts[2]
                : parts.Length >= 3 && parts[0] is "exec" or "x" && parts[^2] == "-c" ? parts[^1] : null;
            if (originalHash is not null)
            {
                foreach (KeyValuePair<string, string> pair in messages)
                {
                    if (pair.Key.StartsWith(originalHash, StringComparison.OrdinalIgnoreCase)) { return pair.Value; }
                }
            }
        }
        try
        {
            GitCommandResult head = await runner.RunAsync(repository.Path, ["rev-parse", "REBASE_HEAD"], cancellationToken: cancellationToken);
            return head.IsSuccess && messages.TryGetValue(head.StandardOutput.Trim(), out string? message) ? message : null;
        }
        catch (GitCommandException) { return null; }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static Task WriteHeartbeatAsync(IRepositoryFileSystem files, string path, CancellationToken cancellationToken)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return files.WriteAllBytesAtomicAsync(path, Encoding.UTF8.GetBytes(timestamp), cancellationToken);
    }

    private static async Task MaintainHeartbeatAsync(IRepositoryFileSystem files, string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            await WriteHeartbeatAsync(files, path, cancellationToken);
        }
    }

    private const string RemoteEditorScript = """
        set -eu
        prefix="$SIMPLEGIT_EDITOR_SESSION"
        id="$$"
        request="$prefix.request"
        response="$prefix.$id.response"
        last_alive=""
        last_seen=$(date +%s)
        { printf '%s\n%s\n' "$id" "$1"; cat "$2"; } > "$request.tmp"
        mv "$request.tmp" "$request"
        while [ ! -f "$response" ]; do
            if alive=$(cat "$prefix.alive" 2>/dev/null); then
                case "$alive" in
                    ''|*[!0-9]*) ;;
                    *) if [ "$alive" != "$last_alive" ]; then
                        last_alive="$alive"
                        last_seen=$(date +%s)
                       fi ;;
                esac
            fi
            now=$(date +%s)
            [ "$((now - last_seen))" -lt 60 ] || exit 1
            sleep 0.2
        done
        [ "$(head -n 1 "$response")" = 1 ] || exit 1
        tail -n +2 "$response" > "$2"
        """;
}
