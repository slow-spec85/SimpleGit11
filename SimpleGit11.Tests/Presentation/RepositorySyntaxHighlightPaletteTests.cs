using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Presentation.Editor;
using System;
using System.Collections.Generic;
using TextControlBoxNS;
using Windows.UI;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class RepositorySyntaxHighlightPaletteTests
{
    private static readonly Color Light = Color.FromArgb(255, 1, 2, 3);
    private static readonly Color Dark = Color.FromArgb(255, 4, 5, 6);
    private static readonly Color HighContrast = Color.FromArgb(255, 7, 8, 9);

    [TestMethod]
    public void NormalTheme_MapsApplicationResourcesToSemanticRoles()
    {
        Dictionary<SyntaxHighlightRole, string> expectedResources = new()
        {
            [SyntaxHighlightRole.Comment] = "SyntaxCommentForegroundBrush",
            [SyntaxHighlightRole.Keyword] = "SyntaxKeywordForegroundBrush",
            [SyntaxHighlightRole.ControlFlow] = "SyntaxControlFlowForegroundBrush",
            [SyntaxHighlightRole.Type] = "SyntaxTypeForegroundBrush",
            [SyntaxHighlightRole.Function] = "SyntaxFunctionForegroundBrush",
            [SyntaxHighlightRole.String] = "SyntaxStringForegroundBrush",
            [SyntaxHighlightRole.Number] = "SyntaxNumberForegroundBrush",
            [SyntaxHighlightRole.MarkupName] = "SyntaxMarkupNameForegroundBrush",
            [SyntaxHighlightRole.AttributeName] = "SyntaxAttributeNameForegroundBrush",
        };
        List<(string Theme, string Resource)> requests = [];

        SyntaxHighlightPalette palette = RepositorySyntaxHighlightPalette.Create(
            isHighContrast: false,
            (theme, resource) =>
            {
                requests.Add((theme, resource));
                return theme == "Light" ? Light : Dark;
            },
            _ => throw new AssertFailedException("High Contrast resolver must not be used."));

        foreach (KeyValuePair<SyntaxHighlightRole, string> expected in expectedResources)
        {
            Assert.IsTrue(palette.TryGetColors(expected.Key, out SyntaxHighlightPaletteEntry colors));
            Assert.AreEqual(Light, colors.Light);
            Assert.AreEqual(Dark, colors.Dark);
            CollectionAssert.Contains(requests, ("Light", expected.Value));
            CollectionAssert.Contains(requests, ("Dark", expected.Value));
        }

        Assert.IsFalse(palette.TryGetColors(SyntaxHighlightRole.Operator, out _));
        Assert.HasCount(expectedResources.Count * 2, requests);
    }

    [TestMethod]
    public void HighContrast_UsesCurrentResourceColorForBothThemes()
    {
        int currentColorRequests = 0;

        SyntaxHighlightPalette palette = RepositorySyntaxHighlightPalette.Create(
            isHighContrast: true,
            (_, _) => throw new AssertFailedException("Theme dictionaries must not be used."),
            _ =>
            {
                currentColorRequests++;
                return HighContrast;
            });

        Assert.IsTrue(palette.TryGetColors(
            SyntaxHighlightRole.Comment,
            out SyntaxHighlightPaletteEntry colors));
        Assert.AreEqual(HighContrast, colors.Light);
        Assert.AreEqual(HighContrast, colors.Dark);
        Assert.AreEqual(9, currentColorRequests);
    }
}
