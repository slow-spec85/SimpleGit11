using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Editor;
using TextControlBoxNS;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class TextControlBoxSyntaxMapperTests
{
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
    [DataRow("unknown.yaml", SyntaxHighlightID.None)]
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
    [DataRow(SyntaxHighlightingMode.CStyle, "unknown.bin", SyntaxHighlightID.CSharp)]
    [DataRow(SyntaxHighlightingMode.Hash, "settings.toml", SyntaxHighlightID.TOML)]
    [DataRow(SyntaxHighlightingMode.Hash, ".gitignore", SyntaxHighlightID.Gitignore)]
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
