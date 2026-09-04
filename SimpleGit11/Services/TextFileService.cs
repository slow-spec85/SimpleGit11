using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Execution;
using AppExecutionContext = SimpleGit11.Services.Execution.ExecutionContext;

namespace SimpleGit11.Services;

public sealed class TextFileService : ITextFileService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);
    private readonly IExecutionContextService? _executionContextService;

    public TextFileService(IExecutionContextService? executionContextService = null)
    {
        _executionContextService = executionContextService;
    }

    public async Task<TextFileDocument> ReadAsync(RepositoryInfo repository, string relativePath)
    {
        AppExecutionContext? context = _executionContextService?.Current;
        string path = await GetSafeFilePathAsync(repository, relativePath, context);
        byte[] bytes = context is null
            ? await File.ReadAllBytesAsync(path)
            : await context.Runtime.Files.ReadAllBytesAsync(path);
        if (context is not null && _executionContextService?.Current.Id != context.Id)
        {
            throw new InvalidOperationException(
                "The execution context changed while the file was being opened.");
        }
        (Encoding encoding, bool emitBom, int preambleLength) = DetectEncoding(bytes);
        if (preambleLength == 0 && LooksBinary(bytes))
        {
            throw new InvalidDataException("The selected file appears to be binary.");
        }

        string text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        return new TextFileDocument(
            path,
            text,
            encoding,
            emitBom,
            DetectNewLine(text),
            context?.Id);
    }

    public async Task WriteAsync(TextFileDocument document, string text)
    {
        AppExecutionContext? context = _executionContextService?.Current;
        if (document.ExecutionContextId is Guid contextId && context?.Id != contextId)
        {
            throw new InvalidOperationException(
                "The execution context changed after the file was opened.");
        }

        string normalizedText = NormalizeNewLines(text, document.NewLine);
        byte[] content = document.Encoding.GetBytes(normalizedText);
        byte[] preamble = document.EmitByteOrderMark
            ? document.Encoding.GetPreamble()
            : [];

        if (preamble.Length == 0)
        {
            await WriteAllBytesAsync(document.Path, content, context);
            return;
        }

        byte[] output = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, output, preamble.Length, content.Length);
        await WriteAllBytesAsync(document.Path, output, context);
    }

    private async Task<string> GetSafeFilePathAsync(
        RepositoryInfo repository,
        string relativePath,
        AppExecutionContext? context)
    {
        if (context is not null)
        {
            IRepositoryPathService paths = context.Runtime.Paths;
            if (IsRooted(relativePath, paths.Style))
            {
                throw new ArgumentException("File path is outside of the repository.", nameof(relativePath));
            }

            string contextualRepositoryPath = paths.Normalize(repository.Path).TrimEnd('/', '\\');
            string contextualFilePath = paths.Normalize(paths.Combine(contextualRepositoryPath, relativePath));
            char separator = paths.Style == RepositoryPathStyle.Windows ? '\\' : '/';
            StringComparison comparison = paths.Style == RepositoryPathStyle.Windows
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!contextualFilePath.StartsWith(contextualRepositoryPath + separator, comparison))
            {
                throw new ArgumentException("File path is outside of the repository.", nameof(relativePath));
            }


            string currentPath = contextualRepositoryPath;
            char[] separators = paths.Style == RepositoryPathStyle.Windows
                ? ['\\', '/']
                : ['/'];
            foreach (string component in relativePath.Split(
                separators,
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (component == ".")
                {
                    continue;
                }

                currentPath = paths.Combine(currentPath, component);
                if (await context.Runtime.Files.IsSymbolicLinkAsync(currentPath))
                {
                    throw new FileNotFoundException(
                        "Symbolic links cannot be opened through the repository file editor.",
                        contextualFilePath);
                }
            }

            if (!await context.Runtime.Files.FileExistsAsync(contextualFilePath))
            {
                throw new FileNotFoundException(
                    "The selected file is not available in the working tree.",
                    contextualFilePath);
            }

            return contextualFilePath;
        }

        string filePath = RepositoryPathGuard.GetSafeFilePath(repository.Path, relativePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected file is not available in the working tree.", filePath);
        }

        return filePath;
    }

    private static Task WriteAllBytesAsync(
        string path,
        byte[] content,
        AppExecutionContext? context)
    {
        return context is null
            ? File.WriteAllBytesAsync(path, content)
            : context.Runtime.Files.WriteAllBytesAtomicAsync(path, content);
    }

    private static bool IsRooted(string path, RepositoryPathStyle style)
    {
        return style == RepositoryPathStyle.Windows
            ? Path.IsPathRooted(path)
            : path.StartsWith('/');
    }

    private static (Encoding Encoding, bool EmitBom, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (HasPrefix(bytes, Encoding.UTF8.GetPreamble()))
        {
            return (new UTF8Encoding(true, true), true, Encoding.UTF8.GetPreamble().Length);
        }

        if (HasPrefix(bytes, Encoding.UTF32.GetPreamble()))
        {
            return (new UTF32Encoding(false, true, true), true, Encoding.UTF32.GetPreamble().Length);
        }

        var utf32BigEndian = new UTF32Encoding(true, true, true);
        if (HasPrefix(bytes, utf32BigEndian.GetPreamble()))
        {
            return (utf32BigEndian, true, utf32BigEndian.GetPreamble().Length);
        }

        if (HasPrefix(bytes, Encoding.Unicode.GetPreamble()))
        {
            return (new UnicodeEncoding(false, true, true), true, Encoding.Unicode.GetPreamble().Length);
        }

        if (HasPrefix(bytes, Encoding.BigEndianUnicode.GetPreamble()))
        {
            return (new UnicodeEncoding(true, true, true), true, Encoding.BigEndianUnicode.GetPreamble().Length);
        }

        return (Utf8WithoutBom, false, 0);
    }

    private static bool HasPrefix(byte[] bytes, byte[] prefix)
    {
        return prefix.Length > 0 &&
            bytes.Length >= prefix.Length &&
            prefix.SequenceEqual(bytes.Take(prefix.Length));
    }

    private static bool LooksBinary(byte[] bytes)
    {
        int sampleLength = Math.Min(bytes.Length, 8192);
        for (int index = 0; index < sampleLength; index++)
        {
            if (bytes[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string DetectNewLine(string text)
    {
        int lfIndex = text.IndexOf('\n');
        if (lfIndex >= 0)
        {
            return lfIndex > 0 && text[lfIndex - 1] == '\r' ? "\r\n" : "\n";
        }

        return text.Contains('\r', StringComparison.Ordinal) ? "\r" : Environment.NewLine;
    }

    private static string NormalizeNewLines(string text, string newLine)
    {
        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return newLine == "\n"
            ? normalized
            : normalized.Replace("\n", newLine, StringComparison.Ordinal);
    }
}
