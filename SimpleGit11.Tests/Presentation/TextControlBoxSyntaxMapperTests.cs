using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text.RegularExpressions;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Editor;
using TextControlBoxNS;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class TextControlBoxSyntaxMapperTests
{
    [TestMethod]
    [DataRow("import (", "import", SyntaxHighlightRole.Keyword)]
    [DataRow("var (", "var", SyntaxHighlightRole.Keyword)]
    [DataRow("func (value int) {}", "func", SyntaxHighlightRole.Keyword)]
    [DataRow("func main() {}", "main", SyntaxHighlightRole.Function)]
    public void GoTokensBeforeParenthesis_KeepTheirSemanticRoles(
        string line,
        string token,
        SyntaxHighlightRole expectedRole)
    {
        SyntaxHighlightLanguage language = TextControlBox.GetSyntaxHighlightingFromID(SyntaxHighlightID.Go);
        int tokenIndex = line.IndexOf(token, StringComparison.Ordinal);
        SyntaxHighlightRole role = SyntaxHighlightRole.Custom;

        foreach (SyntaxHighlights rule in language.Highlights)
        {
            foreach (Match match in Regex.Matches(line, rule.Pattern))
            {
                if (tokenIndex >= match.Index && tokenIndex < match.Index + match.Length)
                {
                    role = rule.Role;
                }
            }
        }

        Assert.AreEqual(expectedRole, role);
    }

    [TestMethod]
    [DataRow("source.asm", SyntaxHighlightID.x86Assembly)]
    [DataRow("build.cmd", SyntaxHighlightID.Batch)]
    [DataRow("native.cpp", SyntaxHighlightID.Cpp)]
    [DataRow("Program.cs", SyntaxHighlightID.CSharp)]
    [DataRow("site.scss", SyntaxHighlightID.CSS)]
    [DataRow("data.csv", SyntaxHighlightID.CSVImproved)]
    [DataRow("index.html", SyntaxHighlightID.Html)]
    [DataRow("settings.ini", SyntaxHighlightID.Inifile)]
    [DataRow("Main.java", SyntaxHighlightID.Java)]
    [DataRow("client.ts", SyntaxHighlightID.Javascript)]
    [DataRow("main.go", SyntaxHighlightID.Go)]
    [DataRow("Module.vb", SyntaxHighlightID.VisualBasic)]
    [DataRow("Macro.bas", SyntaxHighlightID.VBA)]
    [DataRow("Workbook.cls", SyntaxHighlightID.VBA)]
    [DataRow("Dialog.frm", SyntaxHighlightID.VBA)]
    [DataRow("script.sh", SyntaxHighlightID.Bash)]
    [DataRow(".bashrc", SyntaxHighlightID.Bash)]
    [DataRow("build.ps1", SyntaxHighlightID.PowerShell)]
    [DataRow("lib.rs", SyntaxHighlightID.Rust)]
    [DataRow("workflow.yml", SyntaxHighlightID.YAML)]
    [DataRow("Dockerfile", SyntaxHighlightID.Dockerfile)]
    [DataRow("Dockerfile.debug", SyntaxHighlightID.Dockerfile)]
    [DataRow("Containerfile", SyntaxHighlightID.Dockerfile)]
    [DataRow("image.dockerfile", SyntaxHighlightID.Dockerfile)]
    [DataRow("main.tf", SyntaxHighlightID.HCL)]
    [DataRow("variables.tfvars", SyntaxHighlightID.HCL)]
    [DataRow("package.json", SyntaxHighlightID.Json)]
    [DataRow("script.lua", SyntaxHighlightID.Lua)]
    [DataRow("README.md", SyntaxHighlightID.Markdown)]
    [DataRow("index.php", SyntaxHighlightID.PHP)]
    [DataRow("script.py", SyntaxHighlightID.Python)]
    [DataRow("Operation.qs", SyntaxHighlightID.QSharp)]
    [DataRow("query.sql", SyntaxHighlightID.SQL)]
    [DataRow("paper.tex", SyntaxHighlightID.Latex)]
    [DataRow("settings.toml", SyntaxHighlightID.TOML)]
    [DataRow("View.xaml", SyntaxHighlightID.XML)]
    [DataRow(".gitignore", SyntaxHighlightID.Gitignore)]
    [DataRow(".editorconfig", SyntaxHighlightID.Inifile)]
    [DataRow("unknown.raku", SyntaxHighlightID.None)]
    [DataRow("unknown.bin", SyntaxHighlightID.None)]
    public void Auto_MapsPathToExactBuiltInLanguage(
        string path,
        SyntaxHighlightID expected)
    {
        Assert.AreEqual(
            expected,
            TextControlBoxSyntaxMapper.Resolve(SyntaxHighlightingMode.Auto, path));
    }

    [TestMethod]
    [DataRow(SyntaxHighlightingMode.CStyle, "site.css", SyntaxHighlightID.CSS)]
    [DataRow(SyntaxHighlightingMode.CStyle, "main.go", SyntaxHighlightID.Go)]
    [DataRow(SyntaxHighlightingMode.CStyle, "lib.rs", SyntaxHighlightID.Rust)]
    [DataRow(SyntaxHighlightingMode.CStyle, "unknown.bin", SyntaxHighlightID.CSharp)]
    [DataRow(SyntaxHighlightingMode.Hash, "settings.toml", SyntaxHighlightID.TOML)]
    [DataRow(SyntaxHighlightingMode.Hash, ".gitignore", SyntaxHighlightID.Gitignore)]
    [DataRow(SyntaxHighlightingMode.Hash, "script.sh", SyntaxHighlightID.Bash)]
    [DataRow(SyntaxHighlightingMode.Hash, "build.ps1", SyntaxHighlightID.PowerShell)]
    [DataRow(SyntaxHighlightingMode.Hash, "workflow.yaml", SyntaxHighlightID.YAML)]
    [DataRow(SyntaxHighlightingMode.Hash, "Dockerfile", SyntaxHighlightID.Dockerfile)]
    [DataRow(SyntaxHighlightingMode.Hash, "main.hcl", SyntaxHighlightID.HCL)]
    [DataRow(SyntaxHighlightingMode.Hash, "unknown.bin", SyntaxHighlightID.Python)]
    [DataRow(SyntaxHighlightingMode.Dash, "script.lua", SyntaxHighlightID.Lua)]
    [DataRow(SyntaxHighlightingMode.Dash, "unknown.bin", SyntaxHighlightID.SQL)]
    [DataRow(SyntaxHighlightingMode.Html, "README.md", SyntaxHighlightID.Markdown)]
    [DataRow(SyntaxHighlightingMode.Html, "View.xaml", SyntaxHighlightID.XML)]
    [DataRow(SyntaxHighlightingMode.Html, "unknown.bin", SyntaxHighlightID.Html)]
    [DataRow(SyntaxHighlightingMode.None, "Program.cs", SyntaxHighlightID.None)]
    public void ExplicitMode_UsesExactLanguageOrFamilyFallback(
        SyntaxHighlightingMode mode,
        string path,
        SyntaxHighlightID expected)
    {
        Assert.AreEqual(expected, TextControlBoxSyntaxMapper.Resolve(mode, path));
    }
}
