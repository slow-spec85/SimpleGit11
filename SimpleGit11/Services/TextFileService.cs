using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public sealed class TextFileService : ITextFileService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);

    public async Task<TextFileDocument> ReadAsync(RepositoryInfo repository, string relativePath)
    {
        string path = GetSafeFilePath(repository, relativePath);
        byte[] bytes = await File.ReadAllBytesAsync(path);
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
            DetectNewLine(text));
    }

    public async Task WriteAsync(TextFileDocument document, string text)
    {
        string normalizedText = NormalizeNewLines(text, document.NewLine);
        byte[] content = document.Encoding.GetBytes(normalizedText);
        byte[] preamble = document.EmitByteOrderMark
            ? document.Encoding.GetPreamble()
            : [];

        if (preamble.Length == 0)
        {
            await File.WriteAllBytesAsync(document.Path, content);
            return;
        }

        byte[] output = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, output, preamble.Length, content.Length);
        await File.WriteAllBytesAsync(document.Path, output);
    }

    private static string GetSafeFilePath(RepositoryInfo repository, string relativePath)
    {
        string repositoryPath = Path.GetFullPath(repository.Path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string filePath = Path.GetFullPath(Path.Combine(repository.Path, relativePath));

        if (!filePath.StartsWith(repositoryPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("File path is outside of the repository.", nameof(relativePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected file is not available in the working tree.", filePath);
        }

        return filePath;
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
