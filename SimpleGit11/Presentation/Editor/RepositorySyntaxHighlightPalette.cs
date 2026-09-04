using System;
using System.Collections.Generic;
using SimpleGit11.Presentation.Theming;
using TextControlBoxNS;
using Windows.UI;

namespace SimpleGit11.Presentation.Editor;

internal static class RepositorySyntaxHighlightPalette
{
    private static readonly IReadOnlyDictionary<SyntaxHighlightRole, string> ResourceKeys =
        new Dictionary<SyntaxHighlightRole, string>
        {
            [SyntaxHighlightRole.Comment] = "SyntaxCommentForegroundBrush",
            [SyntaxHighlightRole.Keyword] = "SyntaxKeywordForegroundBrush",
            [SyntaxHighlightRole.ControlFlow] = "SyntaxControlFlowForegroundBrush",
            [SyntaxHighlightRole.Type] = "SyntaxTypeForegroundBrush",
            [SyntaxHighlightRole.Function] = "SyntaxFunctionForegroundBrush",
            [SyntaxHighlightRole.String] = "SyntaxStringForegroundBrush",
            [SyntaxHighlightRole.Number] = "SyntaxNumberForegroundBrush",
            [SyntaxHighlightRole.Constant] = "SyntaxConstantForegroundBrush",
            [SyntaxHighlightRole.MarkupName] = "SyntaxMarkupNameForegroundBrush",
            [SyntaxHighlightRole.AttributeName] = "SyntaxAttributeNameForegroundBrush",
            [SyntaxHighlightRole.Operator] = "SyntaxOperatorForegroundBrush",
            [SyntaxHighlightRole.Punctuation] = "SyntaxPunctuationForegroundBrush",
            [SyntaxHighlightRole.Directive] = "SyntaxDirectiveForegroundBrush",
            [SyntaxHighlightRole.Variable] = "SyntaxVariableForegroundBrush",
            [SyntaxHighlightRole.Label] = "SyntaxLabelForegroundBrush",
            [SyntaxHighlightRole.Key] = "SyntaxKeyForegroundBrush",
            // Value colors remain language-defined because CSV columns and diagnostics use
            // distinct colors within the same semantic role.
        };

    public static SyntaxHighlightPalette Create()
    {
        return Create(
            ThemeResourceResolver.IsHighContrast,
            ThemeResourceResolver.GetThemeColor,
            ThemeResourceResolver.GetColor);
    }

    internal static SyntaxHighlightPalette Create(
        bool isHighContrast,
        Func<string, string, Color> getThemeColor,
        Func<string, Color> getCurrentColor)
    {
        ArgumentNullException.ThrowIfNull(getThemeColor);
        ArgumentNullException.ThrowIfNull(getCurrentColor);

        List<SyntaxHighlightPaletteEntry> entries = [];
        foreach (KeyValuePair<SyntaxHighlightRole, string> resource in ResourceKeys)
        {
            Color light;
            Color dark;
            if (isHighContrast)
            {
                light = getCurrentColor(resource.Value);
                dark = light;
            }
            else
            {
                light = getThemeColor("Light", resource.Value);
                dark = getThemeColor("Dark", resource.Value);
            }

            entries.Add(new SyntaxHighlightPaletteEntry(resource.Key, light, dark));
        }

        return new SyntaxHighlightPalette([.. entries]);
    }
}
