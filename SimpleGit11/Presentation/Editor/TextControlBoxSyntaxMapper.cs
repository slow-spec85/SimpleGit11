using System;
using System.IO;
using SimpleGit11.Models;
using TextControlBoxNS;

namespace SimpleGit11.Presentation.Editor;

internal static class TextControlBoxSyntaxMapper
{
    public static SyntaxHighlightID Resolve(
        SyntaxHighlightingMode selectedMode,
        string? filePath)
    {
        if (selectedMode == SyntaxHighlightingMode.None)
        {
            return SyntaxHighlightID.None;
        }

        SyntaxHighlightID exactLanguage = ResolveExact(filePath);
        if (selectedMode == SyntaxHighlightingMode.Auto)
        {
            return exactLanguage;
        }

        return selectedMode switch
        {
            SyntaxHighlightingMode.CStyle when IsCStyle(exactLanguage) => exactLanguage,
            SyntaxHighlightingMode.CStyle => SyntaxHighlightID.CSharp,
            SyntaxHighlightingMode.Hash when IsHashStyle(exactLanguage) => exactLanguage,
            SyntaxHighlightingMode.Hash => SyntaxHighlightID.Python,
            SyntaxHighlightingMode.Dash when IsDashStyle(exactLanguage) => exactLanguage,
            SyntaxHighlightingMode.Dash => SyntaxHighlightID.SQL,
            SyntaxHighlightingMode.Html when IsMarkup(exactLanguage) => exactLanguage,
            SyntaxHighlightingMode.Html => SyntaxHighlightID.Html,
            _ => SyntaxHighlightID.None,
        };
    }

    private static SyntaxHighlightID ResolveExact(string? filePath)
    {
        string path = filePath ?? string.Empty;
        string fileName = Path.GetFileName(path).ToLowerInvariant();
        if (fileName == ".gitignore")
        {
            return SyntaxHighlightID.Gitignore;
        }

        if (fileName == ".editorconfig")
        {
            return SyntaxHighlightID.Inifile;
        }

        if (IsDockerfile(fileName))
        {
            return SyntaxHighlightID.Dockerfile;
        }

        if (IsShellProfile(fileName))
        {
            return SyntaxHighlightID.Bash;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".asm" or ".s" => SyntaxHighlightID.x86Assembly,
            ".bat" or ".cmd" => SyntaxHighlightID.Batch,
            ".c" or ".cc" or ".cpp" or ".cxx" or ".h" or ".hpp" =>
                SyntaxHighlightID.Cpp,
            ".cs" => SyntaxHighlightID.CSharp,
            ".css" or ".scss" => SyntaxHighlightID.CSS,
            ".csv" => SyntaxHighlightID.CSVImproved,
            ".htm" or ".html" or ".cshtml" => SyntaxHighlightID.Html,
            ".ini" => SyntaxHighlightID.Inifile,
            ".java" => SyntaxHighlightID.Java,
            ".js" or ".jsx" or ".ts" or ".tsx" => SyntaxHighlightID.Javascript,
            ".go" => SyntaxHighlightID.Go,
            ".vb" => SyntaxHighlightID.VisualBasic,
            ".bas" or ".cls" or ".frm" or ".vba" => SyntaxHighlightID.VBA,
            ".bash" or ".sh" or ".zsh" => SyntaxHighlightID.Bash,
            ".ps1" or ".psd1" or ".psm1" => SyntaxHighlightID.PowerShell,
            ".rs" => SyntaxHighlightID.Rust,
            ".yaml" or ".yml" => SyntaxHighlightID.YAML,
            ".dockerfile" => SyntaxHighlightID.Dockerfile,
            ".hcl" or ".tf" or ".tfvars" => SyntaxHighlightID.HCL,
            ".json" => SyntaxHighlightID.Json,
            ".lua" => SyntaxHighlightID.Lua,
            ".md" or ".markdown" => SyntaxHighlightID.Markdown,
            ".php" => SyntaxHighlightID.PHP,
            ".py" => SyntaxHighlightID.Python,
            ".qs" => SyntaxHighlightID.QSharp,
            ".sql" => SyntaxHighlightID.SQL,
            ".tex" => SyntaxHighlightID.Latex,
            ".toml" => SyntaxHighlightID.TOML,
            ".axaml" or ".props" or ".resw" or ".targets" or ".wxs" or
                ".xaml" or ".xml" => SyntaxHighlightID.XML,
            _ => SyntaxHighlightID.None,
        };
    }

    private static bool IsCStyle(SyntaxHighlightID language)
    {
        return language is SyntaxHighlightID.Cpp
            or SyntaxHighlightID.CSharp
            or SyntaxHighlightID.CSS
            or SyntaxHighlightID.Java
            or SyntaxHighlightID.Javascript
            or SyntaxHighlightID.Json
            or SyntaxHighlightID.PHP
            or SyntaxHighlightID.Go
            or SyntaxHighlightID.Rust;
    }

    private static bool IsHashStyle(SyntaxHighlightID language)
    {
        return language is SyntaxHighlightID.Batch
            or SyntaxHighlightID.Inifile
            or SyntaxHighlightID.Python
            or SyntaxHighlightID.TOML
            or SyntaxHighlightID.Gitignore
            or SyntaxHighlightID.Bash
            or SyntaxHighlightID.PowerShell
            or SyntaxHighlightID.YAML
            or SyntaxHighlightID.Dockerfile
            or SyntaxHighlightID.HCL;
    }

    private static bool IsDashStyle(SyntaxHighlightID language)
    {
        return language is SyntaxHighlightID.Lua or SyntaxHighlightID.SQL;
    }

    private static bool IsMarkup(SyntaxHighlightID language)
    {
        return language is SyntaxHighlightID.Html
            or SyntaxHighlightID.Markdown
            or SyntaxHighlightID.XML;
    }

    private static bool IsDockerfile(string fileName)
    {
        return fileName == "dockerfile"
            || fileName.StartsWith("dockerfile.", StringComparison.Ordinal)
            || fileName == "containerfile"
            || fileName.StartsWith("containerfile.", StringComparison.Ordinal);
    }

    private static bool IsShellProfile(string fileName)
    {
        return fileName is ".bash_profile"
            or ".bash_logout"
            or ".bashrc"
            or ".profile"
            or ".zprofile"
            or ".zshrc";
    }
}
