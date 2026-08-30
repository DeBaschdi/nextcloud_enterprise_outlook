Param(
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nc4ol-filelink-tests-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null

try {
    $testSource = Join-Path $TempRoot "OutlookFileLinkRenderingTests.cs"
    @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using NcTalkOutlookAddIn.Models;
using NcTalkOutlookAddIn.Utilities;

namespace NcTalkOutlookAddIn.Utilities
{
    internal static class DiagnosticsLogger
    {
        internal static bool IsEnabled { get { return false; } }
        internal static void Log(string category, string message) { }
        internal static void LogException(string category, string message, Exception ex) { }
    }

    internal static class LogCategories
    {
        internal const string Core = "core";
        internal const string FileLink = "filelink";
    }
}

internal static class OutlookFileLinkRenderingTests
{
    private static int failures;

    private static void Check(string name, bool condition, string detail = "")
    {
        if (condition)
        {
            Console.WriteLine("[OK] " + name);
            return;
        }
        failures++;
        Console.Error.WriteLine("[FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : ": " + detail));
    }

    private static string BuildPermissionsHtml(FileLinkPermissionFlags permissions, string[] labels)
    {
        MethodInfo generator = typeof(FileLinkHtmlBuilder).GetMethod(
            "BuildPermissions",
            BindingFlags.NonPublic | BindingFlags.Static);
        Check("Rights HTML generator is discoverable", generator != null);
        if (generator == null)
        {
            return string.Empty;
        }
        return (string)generator.Invoke(null, new object[]
        {
            permissions,
            labels[0],
            labels[1],
            labels[2],
            labels[3]
        });
    }

    private static Dictionary<string, string> ParseStyle(IElement element)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string style = element == null ? string.Empty : (element.GetAttribute("style") ?? string.Empty);
        foreach (string declaration in style.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = declaration.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }
            string name = declaration.Substring(0, separator).Trim();
            string value = Regex.Replace(declaration.Substring(separator + 1).Trim(), @"\s+", " ");
            if (name.Length > 0)
            {
                properties[name] = value;
            }
        }
        return properties;
    }

    private static List<IElement> DirectChildren(IElement parent, string tagName)
    {
        if (parent == null)
        {
            return new List<IElement>();
        }
        return parent.Children
            .Where(child => string.Equals(child.TagName, tagName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static IElement SingleDirectChild(string name, IElement parent, string tagName)
    {
        List<IElement> children = DirectChildren(parent, tagName);
        Check(name, children.Count == 1, "count=" + children.Count);
        return children.Count == 1 ? children[0] : null;
    }

    private static void AttributeEquals(string name, IElement element, string attributeName, string expected)
    {
        string actual = element == null ? null : element.GetAttribute(attributeName);
        Check(name, string.Equals(expected, actual, StringComparison.Ordinal), "expected '" + expected + "', got '" + actual + "'");
    }

    private static void StyleEquals(string name, Dictionary<string, string> style, string propertyName, string expected)
    {
        string actual;
        bool present = style.TryGetValue(propertyName, out actual);
        string normalizedExpected = NormalizeCssValue(propertyName, expected);
        string normalizedActual = NormalizeCssValue(propertyName, actual);
        Check(name, present && string.Equals(normalizedExpected, normalizedActual, StringComparison.OrdinalIgnoreCase), "expected '" + expected + "', got '" + (actual ?? "<missing>") + "'");
    }

    private static string NormalizeCssValue(string propertyName, string value)
    {
        string normalized = Regex.Replace(
            value ?? string.Empty,
            @"rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)",
            delegate (Match match)
            {
                return "#"
                    + int.Parse(match.Groups[1].Value).ToString("x2")
                    + int.Parse(match.Groups[2].Value).ToString("x2")
                    + int.Parse(match.Groups[3].Value).ToString("x2");
            },
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized.Trim(), @"\s+", " ");
        if (!string.Equals(propertyName, "padding", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }
        string[] parts = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return string.Join(" ", new[] { parts[0], parts[0], parts[0], parts[0] });
        }
        if (parts.Length == 2)
        {
            return string.Join(" ", new[] { parts[0], parts[1], parts[0], parts[1] });
        }
        if (parts.Length == 3)
        {
            return string.Join(" ", new[] { parts[0], parts[1], parts[2], parts[1] });
        }
        return normalized;
    }

    private static void AssertPresentationTable(string name, IElement table)
    {
        AttributeEquals(name + " role", table, "role", "presentation");
        AttributeEquals(name + " border", table, "border", "0");
        AttributeEquals(name + " cellspacing", table, "cellspacing", "0");
        AttributeEquals(name + " cellpadding", table, "cellpadding", "0");
        Dictionary<string, string> style = ParseStyle(table);
        StyleEquals(name + " border collapse", style, "border-collapse", "collapse");
        StyleEquals(name + " natural width", style, "width", "auto");
        StyleEquals(name + " margin", style, "margin", "0");
    }

    private static void AssertIconPresentationTable(string name, IElement table)
    {
        AttributeEquals(name + " role", table, "role", "presentation");
        AttributeEquals(name + " border", table, "border", "0");
        AttributeEquals(name + " cellspacing", table, "cellspacing", "0");
        AttributeEquals(name + " cellpadding", table, "cellpadding", "0");
        AttributeEquals(name + " width", table, "width", "14");
        AttributeEquals(name + " height", table, "height", "14");
        Dictionary<string, string> style = ParseStyle(table);
        StyleEquals(name + " border collapse", style, "border-collapse", "collapse");
        StyleEquals(name + " CSS width", style, "width", "14px");
        StyleEquals(name + " CSS height", style, "height", "14px");
        StyleEquals(name + " margin", style, "margin", "0");
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0;
        int offset = 0;
        while (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(token))
        {
            int index = value.IndexOf(token, offset, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }
            count++;
            offset = index + token.Length;
        }
        return count;
    }

    private static bool ContainsCodePoint(string value, char codePoint)
    {
        return !string.IsNullOrEmpty(value) && value.IndexOf(codePoint) >= 0;
    }

    private static int CountCodePoint(string value, char codePoint)
    {
        return string.IsNullOrEmpty(value)
            ? 0
            : value.Count(character => character == codePoint);
    }

    private static string NormalizeNoBreakCharacters(string value)
    {
        return (value ?? string.Empty)
            .Replace('\u00A0', ' ')
            .Replace('\u2011', '-');
    }

    private static string GetNormalizedVisibleText(string html)
    {
        var document = new HtmlParser().ParseDocument(html ?? string.Empty);
        return NormalizeNoBreakCharacters(document.Body == null ? string.Empty : document.Body.TextContent);
    }

    private static IElement AssertSingleNoBreakElement(string caseName, string html)
    {
        var document = new HtmlParser().ParseDocument(
            "<!doctype html><html><body>" + (html ?? string.Empty) + "</body></html>");
        List<IElement> wrappers = document.QuerySelectorAll("nobr").ToList();
        Check(caseName + " has exactly one no-break wrapper", wrappers.Count == 1, html);
        Check(
            caseName + " wrapper is the only top-level element",
            document.Body != null
                && document.Body.Children.Count() == 1
                && string.Equals(document.Body.Children.First().TagName, "nobr", StringComparison.OrdinalIgnoreCase),
            html);
        if (wrappers.Count != 1)
        {
            return null;
        }

        IElement wrapper = wrappers[0];
        StyleEquals(caseName + " wrapper white space", ParseStyle(wrapper), "white-space", "nowrap");
        return wrapper;
    }

    private static IElement AssertSingleNoBreakElementByText(
        string caseName,
        string html,
        string expectedVisibleText)
    {
        var document = new HtmlParser().ParseDocument(html ?? string.Empty);
        List<IElement> matches = document.QuerySelectorAll("nobr")
            .Where(element => string.Equals(
                NormalizeNoBreakCharacters(element.TextContent),
                expectedVisibleText,
                StringComparison.Ordinal))
            .ToList();
        Check(
            caseName + " has exactly one matching no-break element",
            matches.Count == 1,
            "count=" + matches.Count + "; html=" + html);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static IElement AssertNoBreakFieldLabel(
        string caseName,
        string html,
        string expectedVisibleText)
    {
        IElement element = AssertSingleNoBreakElementByText(caseName, html, expectedVisibleText);
        if (element == null)
        {
            return null;
        }

        string text = element.TextContent;
        Check(caseName + " uses one non-breaking space", CountCodePoint(text, '\u00A0') == 1, text);
        Check(caseName + " contains no ordinary space", !ContainsCodePoint(text, ' '), text);
        Check(
            caseName + " preserves visible text",
            string.Equals(NormalizeNoBreakCharacters(text), expectedVisibleText, StringComparison.Ordinal),
            text);
        return element;
    }

    private static IElement AssertNoBreakDate(
        string caseName,
        string html,
        string expectedVisibleText)
    {
        IElement element = AssertSingleNoBreakElementByText(caseName, html, expectedVisibleText);
        if (element == null)
        {
            return null;
        }

        string text = element.TextContent;
        Check(caseName + " uses two non-breaking hyphens", CountCodePoint(text, '\u2011') == 2, text);
        Check(caseName + " contains no ASCII hyphen", !ContainsCodePoint(text, '-'), text);
        Check(
            caseName + " preserves visible date",
            string.Equals(text.Replace('\u2011', '-'), expectedVisibleText, StringComparison.Ordinal),
            text);
        return element;
    }

    private static void AssertAttributeAndTextSafeCustomTemplateValue(
        string caseName,
        string html,
        string expectedTagName,
        string attributeName,
        string expectedValue,
        string placeholder)
    {
        var document = new HtmlParser().ParseDocument(
            "<!doctype html><html><body>" + (html ?? string.Empty) + "</body></html>");
        IElement body = document.Body;

        Check(caseName + " has a parsed body", body != null, html);
        Check(
            caseName + " has one top-level element",
            body != null && body.Children.Count() == 1,
            html);
        Check(
            caseName + " contains no unresolved placeholder",
            (html ?? string.Empty).IndexOf(placeholder, StringComparison.Ordinal) < 0,
            html);
        Check(
            caseName + " contains no no-break element",
            body != null && body.QuerySelectorAll("nobr").Count() == 0,
            html);

        if (body == null || body.Children.Count() != 1)
        {
            return;
        }

        IElement element = body.Children.First();
        Check(
            caseName + " preserves the expected element",
            string.Equals(element.TagName, expectedTagName, StringComparison.OrdinalIgnoreCase),
            element.OuterHtml);
        AttributeEquals(caseName + " preserves the attribute value", element, attributeName, expectedValue);
        Check(
            caseName + " attribute contains no no-break fragment",
            (element.GetAttribute(attributeName) ?? string.Empty).IndexOf("<nobr", StringComparison.OrdinalIgnoreCase) < 0,
            element.OuterHtml);
        Check(
            caseName + " preserves visible text",
            string.Equals(element.TextContent, expectedValue, StringComparison.Ordinal),
            element.OuterHtml);
        Check(
            caseName + " keeps the replacement as text",
            element.Children.Count() == 0,
            element.OuterHtml);
    }

    private static void AssertPlainTextNoBreakContract(string caseName, string plainText)
    {
        Check(caseName + " keeps ASCII-hyphen date", plainText.Contains("2026-08-01"), plainText);
        Check(caseName + " keeps ordinary-space label", plainText.Contains("Nextcloud link"), plainText);
        Check(caseName + " contains no nobr markup", plainText.IndexOf("<nobr", StringComparison.OrdinalIgnoreCase) < 0, plainText);
        Check(caseName + " contains no non-breaking-space entity", plainText.IndexOf("&nbsp;", StringComparison.OrdinalIgnoreCase) < 0, plainText);
        Check(caseName + " contains no HTML entity", !Regex.IsMatch(plainText, @"&(?:#\d+|#x[0-9a-f]+|[a-z][a-z0-9]+);", RegexOptions.IgnoreCase), plainText);
        Check(caseName + " contains no non-breaking hyphen", !ContainsCodePoint(plainText, '\u2011'), plainText);
        Check(caseName + " contains no non-breaking space", !ContainsCodePoint(plainText, '\u00A0'), plainText);
    }

    private static void AssertPermissionsHtmlContract(
        string caseName,
        string html,
        string[] labels,
        bool[] enabledStates,
        bool requireEncodedEntities)
    {
        Check(caseName + " avoids flexbox", !Regex.IsMatch(html ?? string.Empty, @"display\s*:\s*(?:inline-)?flex", RegexOptions.IgnoreCase), html);
        Check(caseName + " avoids CSS grid", !Regex.IsMatch(html ?? string.Empty, @"display\s*:\s*(?:inline-)?grid", RegexOptions.IgnoreCase), html);
        if (requireEncodedEntities)
        {
            Check(caseName + " check entity count", CountOccurrences(html, "&#10003;") == enabledStates.Count(enabled => enabled), html);
            Check(caseName + " cross entity count", CountOccurrences(html, "&#10007;") == enabledStates.Count(enabled => !enabled), html);
        }

        var parser = new HtmlParser();
        var document = parser.ParseDocument("<!doctype html><html><body>" + (html ?? string.Empty) + "</body></html>");
        IElement body = document.Body;
        Check(caseName + " outer element count", body != null && body.Children.Count() == 1, html);
        if (body == null || body.Children.Count() != 1)
        {
            return;
        }
        IElement outerTable = body.Children.First();
        Check(caseName + " outer element is a table", string.Equals(outerTable.TagName, "table", StringComparison.OrdinalIgnoreCase), outerTable.OuterHtml);
        Check(caseName + " table count", body.QuerySelectorAll("table").Count() == 9, html);
        Check(caseName + " tbody count", body.QuerySelectorAll("tbody").Count() == 9, html);
        Check(caseName + " row count", body.QuerySelectorAll("tr").Count() == 9, html);
        Check(caseName + " cell count", body.QuerySelectorAll("td").Count() == 16, html);
        AssertPresentationTable(caseName + " outer table", outerTable);

        IElement outerBody = SingleDirectChild(caseName + " outer tbody", outerTable, "tbody");
        IElement outerRow = SingleDirectChild(caseName + " parent row", outerBody, "tr");
        List<IElement> permissionCells = DirectChildren(outerRow, "td");
        Check(caseName + " parent permission cell count", permissionCells.Count == 4, "count=" + permissionCells.Count);
        if (permissionCells.Count != 4)
        {
            return;
        }

        for (int index = 0; index < permissionCells.Count; index++)
        {
            IElement permissionCell = permissionCells[index];
            AttributeEquals(caseName + " item " + (index + 1) + " nowrap", permissionCell, "nowrap", "nowrap");
            AttributeEquals(caseName + " item " + (index + 1) + " valign", permissionCell, "valign", "middle");
            Dictionary<string, string> parentStyle = ParseStyle(permissionCell);
            string expectedPadding = index == permissionCells.Count - 1 ? "0" : "0 12px 0 0";
            StyleEquals(
                caseName + " item " + (index + 1) + " spacing",
                parentStyle,
                "padding",
                expectedPadding);
            if (requireEncodedEntities)
            {
                string actualPadding;
                bool hasPadding = parentStyle.TryGetValue("padding", out actualPadding);
                Check(
                    caseName + " item " + (index + 1) + " direct spacing syntax",
                    hasPadding && string.Equals(expectedPadding, actualPadding, StringComparison.Ordinal),
                    "expected '" + expectedPadding + "', got '" + (actualPadding ?? "<missing>") + "'");
            }
            StyleEquals(caseName + " item " + (index + 1) + " white space", parentStyle, "white-space", "nowrap");
            StyleEquals(caseName + " item " + (index + 1) + " vertical alignment", parentStyle, "vertical-align", "middle");
            StyleEquals(caseName + " item " + (index + 1) + " font size", parentStyle, "font-size", "11pt");
            Check(
                caseName + " item " + (index + 1) + " font family",
                parentStyle.ContainsKey("font-family") && parentStyle["font-family"].IndexOf("Calibri", StringComparison.OrdinalIgnoreCase) >= 0,
                permissionCell.GetAttribute("style"));
            Check(caseName + " item " + (index + 1) + " permission wrapper is unbordered", !parentStyle.ContainsKey("border"), permissionCell.GetAttribute("style"));

            IElement nestedTable = SingleDirectChild(caseName + " item " + (index + 1) + " permission-group table", permissionCell, "table");
            AssertPresentationTable(caseName + " permission-group table " + (index + 1), nestedTable);
            IElement nestedBody = SingleDirectChild(caseName + " item " + (index + 1) + " permission-group tbody", nestedTable, "tbody");
            IElement nestedRow = SingleDirectChild(caseName + " item " + (index + 1) + " permission-group row", nestedBody, "tr");
            List<IElement> iconAndLabelCells = DirectChildren(nestedRow, "td");
            Check(caseName + " item " + (index + 1) + " icon-wrapper and label cell count", iconAndLabelCells.Count == 2, "count=" + iconAndLabelCells.Count);
            if (iconAndLabelCells.Count != 2)
            {
                continue;
            }

            IElement iconWrapperCell = iconAndLabelCells[0];
            AttributeEquals(caseName + " icon wrapper " + (index + 1) + " width", iconWrapperCell, "width", "14");
            AttributeEquals(caseName + " icon wrapper " + (index + 1) + " height", iconWrapperCell, "height", "14");
            AttributeEquals(caseName + " icon wrapper " + (index + 1) + " valign", iconWrapperCell, "valign", "middle");
            Dictionary<string, string> iconWrapperStyle = ParseStyle(iconWrapperCell);
            StyleEquals(caseName + " icon wrapper " + (index + 1) + " CSS width", iconWrapperStyle, "width", "14px");
            StyleEquals(caseName + " icon wrapper " + (index + 1) + " CSS height", iconWrapperStyle, "height", "14px");
            StyleEquals(caseName + " icon wrapper " + (index + 1) + " padding", iconWrapperStyle, "padding", "0");
            StyleEquals(caseName + " icon wrapper " + (index + 1) + " vertical alignment", iconWrapperStyle, "vertical-align", "middle");
            Check(caseName + " icon wrapper " + (index + 1) + " is unbordered", !iconWrapperStyle.ContainsKey("border"), iconWrapperCell.GetAttribute("style"));

            IElement iconTable = SingleDirectChild(caseName + " icon wrapper " + (index + 1) + " icon-only table", iconWrapperCell, "table");
            AssertIconPresentationTable(caseName + " icon table " + (index + 1), iconTable);
            IElement iconBody = SingleDirectChild(caseName + " icon table " + (index + 1) + " tbody", iconTable, "tbody");
            IElement iconRow = SingleDirectChild(caseName + " icon table " + (index + 1) + " row", iconBody, "tr");
            IElement iconCell = SingleDirectChild(caseName + " icon table " + (index + 1) + " bordered cell", iconRow, "td");
            string expectedColor = enabledStates[index] ? BrandingAssets.BrandBlueHex : "#c62828";
            string expectedSymbol = enabledStates[index] ? "\u2713" : "\u2717";
            AttributeEquals(caseName + " icon " + (index + 1) + " width", iconCell, "width", "14");
            AttributeEquals(caseName + " icon " + (index + 1) + " height", iconCell, "height", "14");
            AttributeEquals(caseName + " icon " + (index + 1) + " align", iconCell, "align", "center");
            AttributeEquals(caseName + " icon " + (index + 1) + " valign", iconCell, "valign", "middle");
            Dictionary<string, string> iconStyle = ParseStyle(iconCell);
            StyleEquals(caseName + " icon " + (index + 1) + " CSS width", iconStyle, "width", "14px");
            StyleEquals(caseName + " icon " + (index + 1) + " CSS height", iconStyle, "height", "14px");
            StyleEquals(caseName + " icon " + (index + 1) + " border", iconStyle, "border", "1px solid " + expectedColor);
            StyleEquals(caseName + " icon " + (index + 1) + " color", iconStyle, "color", expectedColor);
            StyleEquals(caseName + " icon " + (index + 1) + " font size", iconStyle, "font-size", "11px");
            StyleEquals(caseName + " icon " + (index + 1) + " font weight", iconStyle, "font-weight", "700");
            StyleEquals(caseName + " icon " + (index + 1) + " line height", iconStyle, "line-height", "14px");
            StyleEquals(caseName + " icon " + (index + 1) + " padding", iconStyle, "padding", "0");
            Check(caseName + " icon " + (index + 1) + " omits MSO line-height rule", !iconStyle.ContainsKey("mso-line-height-rule"), iconCell.GetAttribute("style"));
            StyleEquals(caseName + " icon " + (index + 1) + " text alignment", iconStyle, "text-align", "center");
            StyleEquals(caseName + " icon " + (index + 1) + " vertical alignment", iconStyle, "vertical-align", "middle");
            Check(caseName + " icon " + (index + 1) + " symbol", string.Equals(iconCell.TextContent.Trim(), expectedSymbol, StringComparison.Ordinal), iconCell.TextContent);

            IElement labelCell = iconAndLabelCells[1];
            AttributeEquals(caseName + " label " + (index + 1) + " nowrap", labelCell, "nowrap", "nowrap");
            AttributeEquals(caseName + " label " + (index + 1) + " valign", labelCell, "valign", "middle");
            Dictionary<string, string> labelStyle = ParseStyle(labelCell);
            StyleEquals(caseName + " label " + (index + 1) + " spacing", labelStyle, "padding-left", "5px");
            Check(caseName + " label " + (index + 1) + " avoids padding shorthand", !labelStyle.ContainsKey("padding"), labelCell.GetAttribute("style"));
            StyleEquals(caseName + " label " + (index + 1) + " white space", labelStyle, "white-space", "nowrap");
            StyleEquals(caseName + " label " + (index + 1) + " font weight", labelStyle, "font-weight", "600");
            StyleEquals(caseName + " label " + (index + 1) + " vertical alignment", labelStyle, "vertical-align", "middle");
            StyleEquals(caseName + " label " + (index + 1) + " font size", labelStyle, "font-size", "11pt");
            Check(
                caseName + " label " + (index + 1) + " font family",
                labelStyle.ContainsKey("font-family") && labelStyle["font-family"].IndexOf("Calibri", StringComparison.OrdinalIgnoreCase) >= 0,
                labelCell.GetAttribute("style"));
            Check(caseName + " label " + (index + 1) + " localized text", string.Equals(labelCell.TextContent.Trim(), labels[index], StringComparison.Ordinal), labelCell.TextContent);
        }
    }

    private static void TestPermissionsHtmlContract()
    {
        string[] defaultLabels = { "Read", "Upload", "Modify", "Delete" };
        AssertPermissionsHtmlContract(
            "Read-only Rights HTML",
            BuildPermissionsHtml(FileLinkPermissionFlags.Read, defaultLabels),
            defaultLabels,
            new[] { true, false, false, false },
            true);
        AssertPermissionsHtmlContract(
            "All-enabled Rights HTML",
            BuildPermissionsHtml(FileLinkPermissionFlags.Read | FileLinkPermissionFlags.Create | FileLinkPermissionFlags.Write | FileLinkPermissionFlags.Delete, defaultLabels),
            defaultLabels,
            new[] { true, true, true, true },
            true);
        AssertPermissionsHtmlContract(
            "All-disabled Rights HTML",
            BuildPermissionsHtml(FileLinkPermissionFlags.None, defaultLabels),
            defaultLabels,
            new[] { false, false, false, false },
            true);
        AssertPermissionsHtmlContract(
            "Mixed Rights HTML",
            BuildPermissionsHtml(FileLinkPermissionFlags.Create | FileLinkPermissionFlags.Delete, defaultLabels),
            defaultLabels,
            new[] { false, true, false, true },
            true);

        string[] longLabels =
        {
            "Leseberechtigung f\u00fcr sehr lange \u00dcbersetzung",
            "Hochladen und neue Dateien erstellen",
            "Vorhandene Dokumente vollst\u00e4ndig bearbeiten",
            "Freigegebene Inhalte dauerhaft l\u00f6schen"
        };
        AssertPermissionsHtmlContract(
            "Long translated Rights HTML",
            BuildPermissionsHtml(FileLinkPermissionFlags.Read | FileLinkPermissionFlags.Write, longLabels),
            longLabels,
            new[] { true, false, true, false },
            true);

        string[] escapedLabels = { "Read & <inspect>", "Upload", "Modify", "Delete" };
        string escapedHtml = BuildPermissionsHtml(FileLinkPermissionFlags.Read | FileLinkPermissionFlags.Delete, escapedLabels);
        AssertPermissionsHtmlContract(
            "Escaped Rights HTML",
            escapedHtml,
            escapedLabels,
            new[] { true, false, false, true },
            true);
        Check("Rights HTML escapes localized label markup", escapedHtml.Contains("Read &amp; &lt;inspect&gt;") && !escapedHtml.Contains("<inspect>"), escapedHtml);

        BackendPolicyStatus policy = BuildCustomTemplatePolicy("{RIGHTS}");
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", string.Empty);
        string sanitizedHtml = FileLinkHtmlBuilder.Build(result, new FileLinkRequest(), "custom", policy);
        AssertPermissionsHtmlContract(
            "Sanitized custom-template Rights HTML",
            sanitizedHtml,
            defaultLabels,
            new[] { true, true, false, false },
            false);
    }

    private static void TestHtmlNoBreakEncoderContract()
    {
        const string input = "Alpha beta-gamma <span data-evil=\"quoted\">&\"'</span>";
        string html = HtmlNoBreakEncoder.EncodeFieldLabel(input);
        IElement wrapper = AssertSingleNoBreakElement("Direct no-break encoder", html);
        if (wrapper == null)
        {
            return;
        }

        string text = wrapper.TextContent;
        Check(
            "Direct no-break encoder converts every ordinary space",
            CountCodePoint(text, '\u00A0') == CountCodePoint(input, ' ')
                && !ContainsCodePoint(text, ' '),
            text);
        Check(
            "Direct no-break encoder converts every ASCII hyphen",
            CountCodePoint(text, '\u2011') == CountCodePoint(input, '-')
                && !ContainsCodePoint(text, '-'),
            text);
        Check(
            "Direct no-break encoder preserves visible text",
            string.Equals(NormalizeNoBreakCharacters(text), input, StringComparison.Ordinal),
            text);
        Check(
            "Direct no-break encoder keeps HTML-sensitive input as text",
            wrapper.Children.Count() == 0,
            wrapper.OuterHtml);
        Check(
            "Direct no-break encoder prevents injected attributes",
            !wrapper.HasAttribute("data-evil") && wrapper.QuerySelector("[data-evil]") == null,
            wrapper.OuterHtml);
    }

    private static void TestBuiltInNoBreakValues()
    {
        FileLinkResult result = BuildResult(
            "https://cloud.example.test/nc/s/AbCd1234",
            "AbCd1234",
            string.Empty,
            new DateTime(2026, 8, 1));
        string html = FileLinkHtmlBuilder.Build(result, new FileLinkRequest(), "en");

        AssertNoBreakDate("Built-in expiration date", html, "2026-08-01");
        AssertNoBreakFieldLabel("Built-in Nextcloud link field label", html, "Nextcloud link");
    }

    private static void TestTransparentHeaderAssetContract()
    {
        FileLinkResult result = BuildResult(
            "https://cloud.example.test/nc/s/AbCd1234",
            "AbCd1234",
            string.Empty);
        string html = FileLinkHtmlBuilder.Build(result, new FileLinkRequest(), "en");
        var parser = new HtmlParser();
        IElement image = parser.ParseDocument(html).QuerySelector("img[src^='data:image/png;base64,']");
        Check("Built-in header embeds a PNG data URI", image != null);
        if (image == null)
        {
            return;
        }

        const string prefix = "data:image/png;base64,";
        string source = image.GetAttribute("src") ?? string.Empty;
        Check("Built-in header data URI is not empty", source.Length > prefix.Length, source);
        if (!source.StartsWith(prefix, StringComparison.Ordinal) || source.Length <= prefix.Length)
        {
            return;
        }

        byte[] imageBytes = Convert.FromBase64String(source.Substring(prefix.Length));
        using (var stream = new MemoryStream(imageBytes))
        using (var bitmap = new Bitmap(stream))
        {
            Check("Header asset width", bitmap.Width == 164, bitmap.Width.ToString());
            Check("Header asset height", bitmap.Height == 48, bitmap.Height.ToString());
            Check("Header asset has a transparent canvas", bitmap.GetPixel(0, 0).A == 0);
        }
    }

    private static void TestOutlookCompactFrameContract()
    {
        FileLinkResult result = BuildResult(
            "https://cloud.example.test/nc/s/AbCd1234",
            "AbCd1234",
            "Example-password",
            new DateTime(2026, 8, 1));
        string html = FileLinkHtmlBuilder.Build(result, new FileLinkRequest(), "en");
        var document = new HtmlParser().ParseDocument(html);

        IElement outerTable = document.QuerySelector("div > table[role='presentation'][width='640']");
        Check("Compact frame has a presentation outer table", outerTable != null, html);
        if (outerTable != null)
        {
            AttributeEquals("Compact frame outer cellspacing", outerTable, "cellspacing", "0");
            AttributeEquals("Compact frame outer cellpadding", outerTable, "cellpadding", "0");
        }

        IElement contentCell = document.QuerySelector("td[style*='padding:18px 18px 22px 18px']");
        Check("Compact frame uses a Word-safe padded content cell", contentCell != null, html);
        if (contentCell != null)
        {
            Dictionary<string, string> contentStyle = ParseStyle(contentCell);
            StyleEquals("Compact frame content font size", contentStyle, "font-size", "11pt");
            Check(
                "Compact frame content font family",
                contentStyle.ContainsKey("font-family") && contentStyle["font-family"].IndexOf("Calibri", StringComparison.OrdinalIgnoreCase) >= 0,
                contentCell.GetAttribute("style"));

            IElement fieldTable = contentCell.QuerySelector("table[role='presentation']");
            Check("Compact frame has a field table", fieldTable != null, html);
            if (fieldTable != null)
            {
                StyleEquals("Compact frame field table has no repeated Word paragraph margin", ParseStyle(fieldTable), "margin", "0");
            }
        }
        IElement footerCell = document.QuerySelector("td[style*='padding:10px 18px 16px 18px']");
        Check("Compact frame uses a Word-safe padded footer cell", footerCell != null, html);

        IElement labelCell = document.QuerySelector("th[width='124']");
        Check("Compact frame uses the Thunderbird-sized label column", labelCell != null, html);
        if (labelCell != null)
        {
            Dictionary<string, string> labelStyle = ParseStyle(labelCell);
            StyleEquals("Compact frame label width", labelStyle, "width", "124px");
            StyleEquals("Compact frame label font size", labelStyle, "font-size", "11pt");
        }

        IElement headerLink = document.QuerySelector("td[bgcolor] > a");
        Check("Compact frame has a header link", headerLink != null, html);
        if (headerLink != null)
        {
            StyleEquals("Compact frame header link stays compact", ParseStyle(headerLink), "display", "inline-block");
        }
    }

    private static void TestCustomTemplateAttributeSafeValues()
    {
        const string expirationTemplate = "<time datetime=\"{EXPIRATIONDATE}\">{EXPIRATIONDATE}</time>";
        BackendPolicyStatus expirationPolicy = BuildCustomTemplatePolicy(expirationTemplate);
        FileLinkResult result = BuildResult(
            "https://cloud.example.test/nc/s/AbCd1234",
            "AbCd1234",
            string.Empty,
            new DateTime(2026, 8, 1));
        string expirationHtml = FileLinkHtmlBuilder.Build(
            result,
            new FileLinkRequest(),
            "custom",
            expirationPolicy);

        AssertAttributeAndTextSafeCustomTemplateValue(
            "Custom-template expiration date",
            expirationHtml,
            "time",
            "datetime",
            "2026-08-01",
            "{EXPIRATIONDATE}");

        const string labelTemplate = "<span title=\"{LINK_LABEL}\">{LINK_LABEL}</span>";
        const string expectedLabel = "Nextcloud & \"link\" <safe>";
        BackendPolicyStatus labelPolicy = BuildCustomTemplatePolicy(labelTemplate, null, "fr");
        string labelHtml = FileLinkHtmlBuilder.Build(
            result,
            new FileLinkRequest(),
            "custom",
            labelPolicy);

        AssertAttributeAndTextSafeCustomTemplateValue(
            "Custom-template LINK_LABEL",
            labelHtml,
            "span",
            "title",
            expectedLabel,
            "{LINK_LABEL}");
    }

    private static void TestCustomTemplateVisibleNoBreakMarkup()
    {
        const string template = "<p><nobr style=\"white-space: nowrap;\">{LINK_LABEL}</nobr></p>"
            + "<p><nobr style=\"white-space: nowrap;\">{EXPIRATIONDATE}</nobr></p>";
        BackendPolicyStatus policy = BuildCustomTemplatePolicy(template);
        FileLinkResult result = BuildResult(
            "https://cloud.example.test/nc/s/AbCd1234",
            "AbCd1234",
            string.Empty,
            new DateTime(2026, 8, 1));
        string html = FileLinkHtmlBuilder.Build(
            result,
            new FileLinkRequest(),
            "custom",
            policy);

        var document = new HtmlParser().ParseDocument(html);
        List<IElement> noBreakElements = document.QuerySelectorAll("nobr").ToList();
        Check("Custom-template visible no-break elements survive sanitization", noBreakElements.Count == 2, html);
        Check("Custom-template no-break label stays visible text", noBreakElements.Any(element => element.TextContent == "Nextcloud link"), html);
        Check("Custom-template no-break date stays visible text", noBreakElements.Any(element => element.TextContent == "2026-08-01"), html);
        foreach (IElement element in noBreakElements)
        {
            StyleEquals("Custom-template no-break style", ParseStyle(element), "white-space", "nowrap");
        }
    }

    private static void TestPlainTextNoBreakContract()
    {
        const string template = "<p>{LINK_LABEL}: {URL}</p><p>{EXPIRATIONDATE}</p>";
        BackendPolicyStatus policy = BuildCustomTemplatePolicy(template);
        FileLinkResult result = BuildResult(
            "https://cloud.example.test/nc/s/AbCd1234",
            "AbCd1234",
            string.Empty,
            new DateTime(2026, 8, 1));
        FileLinkRequest request = new FileLinkRequest();

        AssertPlainTextNoBreakContract(
            "Built-in plain text",
            FileLinkHtmlBuilder.BuildPlainText(result, request, "en"));
        AssertPlainTextNoBreakContract(
            "Custom-template plain text",
            FileLinkHtmlBuilder.BuildPlainText(result, request, "custom", policy));
    }

    public static int Main()
    {
        Strings.SetPreferredUiLanguage("en");
        TestHtmlNoBreakEncoderContract();
        TestBuiltInNoBreakValues();
        TestTransparentHeaderAssetContract();
        TestOutlookCompactFrameContract();
        TestCustomTemplateAttributeSafeValues();
        TestCustomTemplateVisibleNoBreakMarkup();
        TestPlainTextNoBreakContract();
        TestPermissionsHtmlContract();
        TestNormalModeUsesNextcloudLinkWording();
        TestAttachmentModeKeepsNextcloudSubpath();
        TestPlainTextKeepsNextcloudSubpath();
        TestAttachmentSharePageTarget();
        TestManualShareIgnoresAttachmentTarget();
        TestInvalidZipUrlFailsVisibly();
        TestCustomTemplateResolvesModeAwareLinkVariables();
        TestCustomTemplatePrunesEmptyDynamicBlocks();
        TestCustomTemplateKeepsPasswordContent();
        TestBackendEffectiveLanguageLocalizesCustomTemplateCopy();
        TestOlderBackendModeAwareTemplateStillRenders();
        TestLegacyCustomTemplateStillRenders();
        TestSecretLinkLabelHidesLongUrlInHtml();

        if (failures > 0)
        {
            Console.Error.WriteLine(failures + " FileLink rendering test(s) failed.");
            return 1;
        }
        Console.WriteLine("All Outlook FileLink rendering tests passed.");
        return 0;
    }

    private static void TestNormalModeUsesNextcloudLinkWording()
    {
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", "Secret!");
        FileLinkRequest request = new FileLinkRequest
        {
            ShareName = "Folder",
            AttachmentMode = false,
            PasswordSeparateEnabled = false,
            NoteEnabled = false,
            Permissions = FileLinkPermissionFlags.Read | FileLinkPermissionFlags.Create
        };
        string html = FileLinkHtmlBuilder.Build(result, request, "en");
        string plainText = FileLinkHtmlBuilder.BuildPlainText(result, request, "en");

        Check("Normal HTML labels the share page as a Nextcloud link", GetNormalizedVisibleText(html).Contains("Nextcloud link"), html);
        Check("Normal plain text labels the share page as a Nextcloud link", plainText.Contains("Nextcloud link: https://cloud.example.test/nc/s/AbCd1234"), plainText);
        Check("Normal share URL does not gain a ZIP suffix", !html.Contains("/AbCd1234/download") && !plainText.Contains("/AbCd1234/download"));
    }

    private static void TestAttachmentModeKeepsNextcloudSubpath()
    {
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", "Secret!");
        FileLinkRequest request = BuildAttachmentRequest();
        string html = FileLinkHtmlBuilder.Build(result, request, "en");

        Check("Attachment ZIP URL keeps /nc subpath in HTML", html.Contains("https://cloud.example.test/nc/s/AbCd1234/download"), html);
        Check("Attachment ZIP URL does not drop /nc subpath in HTML", !html.Contains("https://cloud.example.test/s/AbCd1234/download"), html);
        Check("Attachment HTML labels the link as ZIP download", GetNormalizedVisibleText(html).Contains("ZIP download"), html);
        Check("Attachment HTML explains ZIP download behavior", html.Contains("Download the shared files as a ZIP archive"), html);
    }

    private static void TestPlainTextKeepsNextcloudSubpath()
    {
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", "Secret!");
        FileLinkRequest request = BuildAttachmentRequest();
        string plainText = FileLinkHtmlBuilder.BuildPlainText(result, request, "en");

        Check("Attachment ZIP URL keeps /nc subpath in plain text", plainText.Contains("https://cloud.example.test/nc/s/AbCd1234/download"), plainText);
        Check("Attachment plain text labels the link as ZIP download", plainText.Contains("ZIP download: https://cloud.example.test/nc/s/AbCd1234/download"), plainText);
        Check("Plain text stays plain", !plainText.Contains("<a "), plainText);
    }

    private static void TestAttachmentSharePageTarget()
    {
        FileLinkResult result = BuildResult("https://cloud.example.test/index.php/s/AbCd1234", "AbCd1234", string.Empty);
        FileLinkRequest request = BuildAttachmentRequest(AttachmentLinkTarget.SharePage);
        string html = FileLinkHtmlBuilder.Build(result, request, "en");
        string plainText = FileLinkHtmlBuilder.BuildPlainText(result, request, "en");

        foreach (string output in new[] { html, plainText })
        {
            Check("Attachment share-page target keeps the OCS URL", output.Contains("https://cloud.example.test/index.php/s/AbCd1234"), output);
            Check("Attachment share-page target does not add ZIP suffix", !output.Contains("/AbCd1234/download"), output);
            Check("Attachment share-page target uses share-page wording", output.Contains("Nextcloud link"), output);
            Check("Attachment mode still hides recipient rights", !output.Contains("Your permissions") && !output.Contains("Upload"), output);
        }
    }

    private static void TestManualShareIgnoresAttachmentTarget()
    {
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", string.Empty);
        var request = new FileLinkRequest
        {
            AttachmentMode = false,
            AttachmentLinkTarget = AttachmentLinkTarget.ZipDownload,
            Permissions = FileLinkPermissionFlags.Read | FileLinkPermissionFlags.Create
        };
        string plainText = FileLinkHtmlBuilder.BuildPlainText(result, request, "en");

        Check("Manual share always keeps the share-page URL", plainText.Contains("Nextcloud link: https://cloud.example.test/nc/s/AbCd1234"), plainText);
        Check("Manual share ignores ZIP target", !plainText.Contains("/AbCd1234/download"), plainText);
        Check("Manual share keeps rights", plainText.Contains("Your permissions") && plainText.Contains("Upload"), plainText);
    }

    private static void TestInvalidZipUrlFailsVisibly()
    {
        FileLinkRequest request = BuildAttachmentRequest();
        FileLinkResult invalidPath = BuildResult("https://cloud.example.test/index.php/apps/files/", "AbCd1234", string.Empty);
        FileLinkResult tokenMismatch = BuildResult("https://cloud.example.test/s/OtherToken", "AbCd1234", string.Empty);
        FileLinkResult invalidScheme = BuildResult("ftp://cloud.example.test/s/AbCd1234", "AbCd1234", string.Empty);
        FileLinkResult trailingPath = BuildResult("https://cloud.example.test/s/AbCd1234/extra", "AbCd1234", string.Empty);

        Check("ZIP mode rejects a URL without /s/<token>", ThrowsInvalidZipUrl(delegate { FileLinkHtmlBuilder.Build(invalidPath, request, "en"); }));
        Check("ZIP mode rejects a share-token mismatch", ThrowsInvalidZipUrl(delegate { FileLinkHtmlBuilder.BuildPlainText(tokenMismatch, request, "en"); }));
        Check("ZIP mode rejects a non-HTTP(S) URL", ThrowsInvalidZipUrl(delegate { FileLinkHtmlBuilder.Build(invalidScheme, request, "en"); }));
        Check("ZIP mode rejects content after /s/<token>", ThrowsInvalidZipUrl(delegate { FileLinkHtmlBuilder.BuildPlainText(trailingPath, request, "en"); }));
    }

    private static bool ThrowsInvalidZipUrl(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("ZIP download link could not be created");
        }
    }

    private static void TestCustomTemplateResolvesModeAwareLinkVariables()
    {
        const string template = "<p>{LINK_INTRO}</p><p>{LINK_LABEL}: <a href=\"{URL}\">{URL}</a></p>";
        BackendPolicyStatus policy = BuildCustomTemplatePolicy("<p>Legacy template: {URL}</p>", template);
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", string.Empty);

        string normalHtml = FileLinkHtmlBuilder.Build(result, new FileLinkRequest(), "custom", policy);
        string zipHtml = FileLinkHtmlBuilder.Build(result, BuildAttachmentRequest(), "custom", policy);
        string sharePageHtml = FileLinkHtmlBuilder.Build(result, BuildAttachmentRequest(AttachmentLinkTarget.SharePage), "custom", policy);
        string normal = FileLinkHtmlBuilder.BuildPlainText(result, new FileLinkRequest(), "custom", policy);
        string zip = FileLinkHtmlBuilder.BuildPlainText(result, BuildAttachmentRequest(), "custom", policy);

        Check("Custom normal template resolves LINK_INTRO", normal.Contains("Open the Nextcloud link below to view the share."), normal);
        Check("Custom normal template resolves LINK_LABEL", normal.Contains("Nextcloud link: https://cloud.example.test/nc/s/AbCd1234"), normal);
        Check("Custom attachment template resolves ZIP LINK_INTRO", zip.Contains("Download the shared files as a ZIP archive"), zip);
        Check("Custom attachment template resolves ZIP LINK_LABEL", zip.Contains("ZIP download: https://cloud.example.test/nc/s/AbCd1234/download"), zip);
        Check("Custom normal HTML uses the versioned template", GetNormalizedVisibleText(normalHtml).Contains("Open the Nextcloud link below to view the share."), normalHtml);
        Check("Custom attachment HTML resolves the versioned template in ZIP mode", GetNormalizedVisibleText(zipHtml).Contains("ZIP download"), zipHtml);
        Check("Custom attachment HTML resolves the versioned template in share-page mode", GetNormalizedVisibleText(sharePageHtml).Contains("Nextcloud link") && !sharePageHtml.Contains("/download"), sharePageHtml);
        Check("Versioned template takes precedence over compatibility template", !normal.Contains("Legacy template") && !normalHtml.Contains("Legacy template"), normal + normalHtml);
    }

    private static void TestLegacyCustomTemplateStillRenders()
    {
        BackendPolicyStatus policy = BuildCustomTemplatePolicy("<p>Legacy link: {URL}</p>");
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", string.Empty);
        string plainText = FileLinkHtmlBuilder.BuildPlainText(result, new FileLinkRequest(), "custom", policy);

        Check("Legacy custom template still resolves its existing URL variable", plainText.Contains("Legacy link: https://cloud.example.test/nc/s/AbCd1234"), plainText);
        Check("Legacy custom template is not forced to contain new variables", !plainText.Contains("LINK_INTRO") && !plainText.Contains("LINK_LABEL"), plainText);
    }

    private static void TestCustomTemplatePrunesEmptyDynamicBlocks()
    {
        const string template =
            "<table>"
            + "<tr><th>PW-LABEL</th><td><span>{PASSWORD}</span></td></tr>"
            + "<tr><th>EXP-LABEL</th><td>{EXPIRATIONDATE}</td></tr>"
            + "<tr><th>RIGHTS-LABEL</th><td>{RIGHTS}</td></tr>"
            + "</table>"
            + "<p>PW-PARAGRAPH: {PASSWORD}</p>"
            + "<p>NOTE-LABEL: {NOTE}</p>"
            + "<p>LINK-LABEL: {URL}</p>";
        BackendPolicyStatus policy = BuildCustomTemplatePolicy(
            template,
            template,
            "en");
        var result = new FileLinkResult(
            "https://cloud.example.test/nc/s/AbCd1234",
            "42",
            "AbCd1234",
            string.Empty,
            null,
            FileLinkPermissionFlags.Read | FileLinkPermissionFlags.Create,
            "Folder",
            "NC Connector/Folder");
        var manualRequest = new FileLinkRequest
        {
            PasswordEnabled = false,
            NoteEnabled = false,
            Permissions =
                FileLinkPermissionFlags.Read
                | FileLinkPermissionFlags.Create
        };
        FileLinkRequest attachmentRequest = BuildAttachmentRequest();

        string manualHtml = FileLinkHtmlBuilder.Build(
            result,
            manualRequest,
            "custom",
            policy);
        string manualPlainText = FileLinkHtmlBuilder.BuildPlainText(
            result,
            manualRequest,
            "custom",
            policy);
        string attachmentHtml = FileLinkHtmlBuilder.Build(
            result,
            attachmentRequest,
            "custom",
            policy);
        string attachmentPlainText = FileLinkHtmlBuilder.BuildPlainText(
            result,
            attachmentRequest,
            "custom",
            policy);

        foreach (string output in new[] {
            manualHtml,
            manualPlainText,
            attachmentHtml,
            attachmentPlainText
        })
        {
            Check(
                "Custom template removes an empty password block",
                !output.Contains("PW-LABEL")
                    && !output.Contains("PW-PARAGRAPH")
                    && !output.Contains("{PASSWORD}"),
                output);
            Check(
                "Custom template removes an empty expiration block",
                !output.Contains("EXP-LABEL")
                    && !output.Contains("{EXPIRATIONDATE}"),
                output);
            Check(
                "Custom template removes an empty note block",
                !output.Contains("NOTE-LABEL")
                    && !output.Contains("{NOTE}"),
                output);
            Check(
                "Custom template keeps a populated link block",
                output.Contains("LINK-LABEL")
                    && output.Contains("AbCd1234"),
                output);
        }
        foreach (string output in new[] { manualHtml, manualPlainText })
        {
            Check(
                "Custom manual template keeps populated rights",
                output.Contains("RIGHTS-LABEL")
                    && output.Contains("Upload"),
                output);
        }
        foreach (string output in new[] {
            attachmentHtml,
            attachmentPlainText
        })
        {
            Check(
                "Custom attachment template removes hidden rights",
                !output.Contains("RIGHTS-LABEL")
                    && !output.Contains("{RIGHTS}"),
                output);
        }
    }

    private static void TestCustomTemplateKeepsPasswordContent()
    {
        const string template =
            "<table><tr><th>PW-LABEL</th>"
            + "<td><span>{PASSWORD}</span></td></tr></table>"
            + "<p>LINK-LABEL: {URL}</p>";
        BackendPolicyStatus policy = BuildCustomTemplatePolicy(
            template,
            template,
            "en");
        FileLinkResult result = BuildResult(
            "https://cloud.example.test/nc/s/AbCd1234",
            "AbCd1234",
            "Secret!");
        var directRequest = new FileLinkRequest
        {
            PasswordEnabled = true,
            PasswordSeparateEnabled = false,
            Permissions = FileLinkPermissionFlags.Read
        };
        var separateRequest = new FileLinkRequest
        {
            PasswordEnabled = true,
            PasswordSeparateEnabled = true,
            Permissions = FileLinkPermissionFlags.Read
        };

        string directHtml = FileLinkHtmlBuilder.Build(
            result,
            directRequest,
            "custom",
            policy);
        string directPlainText = FileLinkHtmlBuilder.BuildPlainText(
            result,
            directRequest,
            "custom",
            policy);
        string separateHtml = FileLinkHtmlBuilder.Build(
            result,
            separateRequest,
            "custom",
            policy);
        string separatePlainText = FileLinkHtmlBuilder.BuildPlainText(
            result,
            separateRequest,
            "custom",
            policy);

        foreach (string output in new[] { directHtml, directPlainText })
        {
            Check(
                "Custom template keeps a populated password block",
                output.Contains("PW-LABEL")
                    && output.Contains("Secret!"),
                output);
        }
        foreach (string output in new[] {
            separateHtml,
            separatePlainText
        })
        {
            Check(
                "Custom template keeps the separate-password hint",
                output.Contains("PW-LABEL")
                    && output.Contains(
                        "The password will be sent in a separate email.")
                    && !output.Contains("Secret!"),
                output);
        }
    }

    private static void TestBackendEffectiveLanguageLocalizesCustomTemplateCopy()
    {
        const string template = "<p>{LINK_INTRO}</p><p>{LINK_LABEL}: {URL}</p><p>{PASSWORD}</p><p>{RIGHTS}</p>";
        BackendPolicyStatus policy = BuildCustomTemplatePolicy("<p>Legacy: {URL}</p>", template, "de");
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", "Secret!");
        var request = new FileLinkRequest
        {
            PasswordSeparateEnabled = true,
            Permissions = FileLinkPermissionFlags.Read | FileLinkPermissionFlags.Create
        };

        string html = FileLinkHtmlBuilder.Build(result, request, "custom", policy);
        string plainText = FileLinkHtmlBuilder.BuildPlainText(result, request, "custom", policy);

        foreach (string output in new[] { html, plainText })
        {
            Check("Backend template language localizes LINK_INTRO", output.Contains("\u00d6ffnen Sie den untenstehenden Nextcloud-Link"), output);
            Check("Backend template language localizes LINK_LABEL", output.Contains("Nextcloud-Link"), output);
            Check("Backend template language localizes separate-password hint", output.Contains("Das Passwort wird in einer separaten E-Mail gesendet."), output);
            Check("Backend template language localizes permission names", output.Contains("Lesen") && output.Contains("Hochladen") && output.Contains("Bearbeiten") && output.Contains("L\u00f6schen"), output);
        }
    }

    private static void TestOlderBackendModeAwareTemplateStillRenders()
    {
        const string template = "<p>{LINK_INTRO}</p><p>{LINK_LABEL}: <a href=\"{URL}\">{URL}</a></p>";
        BackendPolicyStatus policy = BuildCustomTemplatePolicy(template);
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", string.Empty);
        string plainText = FileLinkHtmlBuilder.BuildPlainText(result, new FileLinkRequest(), "custom", policy);

        Check("Older backend template field still resolves LINK_INTRO", plainText.Contains("Open the Nextcloud link below to view the share."), plainText);
        Check("Older backend template field still resolves LINK_LABEL", plainText.Contains("Nextcloud link: https://cloud.example.test/nc/s/AbCd1234"), plainText);
    }

    private static void TestSecretLinkLabelHidesLongUrlInHtml()
    {
        const string secretUrl = "https://cloud.example.test/index.php/apps/secrets/share/1234567890#VeryLongLocalKey";
        FileLinkResult result = BuildResult("https://cloud.example.test/nc/s/AbCd1234", "AbCd1234", secretUrl);
        string html = FileLinkHtmlBuilder.BuildPasswordOnly(result, "en", null, true);

        Check("Secret password mail uses compact link label", html.Contains(">Secret link<"), html);
        Check("Secret password mail keeps URL in href", html.Contains("href=\"" + secretUrl + "\""), html);
        Check("Secret password mail does not render the long URL as visible text", !html.Contains(">" + secretUrl + "<"), html);
    }

    private static FileLinkResult BuildResult(string shareUrl, string token, string password)
    {
        return BuildResult(shareUrl, token, password, new DateTime(2026, 7, 7));
    }

    private static FileLinkResult BuildResult(
        string shareUrl,
        string token,
        string password,
        DateTime? expireDate)
    {
        return new FileLinkResult(
            shareUrl,
            "42",
            token,
            password,
            expireDate,
            FileLinkPermissionFlags.Read | FileLinkPermissionFlags.Create,
            "Folder",
            "NC Connector/Folder");
    }

    private static FileLinkRequest BuildAttachmentRequest(AttachmentLinkTarget target = AttachmentLinkTarget.ZipDownload)
    {
        return new FileLinkRequest
        {
            ShareName = "Folder",
            AttachmentMode = true,
            AttachmentLinkTarget = target,
            PasswordSeparateEnabled = false,
            NoteEnabled = false,
            Permissions = FileLinkPermissionFlags.Read | FileLinkPermissionFlags.Create
        };
    }

    private static BackendPolicyStatus BuildCustomTemplatePolicy(string template, string versionedTemplate = null, string effectiveLanguage = null)
    {
        var sharePolicy = new Dictionary<string, object>
        {
            { "share_html_block_template", template }
        };
        if (!string.IsNullOrWhiteSpace(versionedTemplate))
        {
            sharePolicy.Add("share_html_block_template_v2", versionedTemplate);
        }
        if (!string.IsNullOrWhiteSpace(effectiveLanguage))
        {
            sharePolicy.Add("share_html_block_effective_language", effectiveLanguage);
        }
        var empty = new Dictionary<string, object>();
        return new BackendPolicyStatus(
            true,
            true,
            true,
            false,
            string.Empty,
            "policy",
            string.Empty,
            true,
            true,
            "active",
            sharePolicy,
            empty,
            empty,
            empty,
            empty,
            empty);
    }
}
'@ | Set-Content -Path $testSource -Encoding UTF8

    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) {
        throw "csc.exe not found at $csc"
    }

    $vendorDir = Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\vendor\htmlsanitizer"
    $sources = @(
        $testSource,
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Models\BackendPolicyStatus.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Models\AttachmentLinkTargetPolicy.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Models\FileLinkPermissions.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Models\FileLinkSelection.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Models\FileLinkRequest.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Models\FileLinkResult.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Models\SharePasswordDeliveryMode.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Utilities\BrandingAssets.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Utilities\FileLinkHtmlBuilder.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Utilities\HtmlNoBreakEncoder.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Utilities\HtmlTemplateSanitizer.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Utilities\HtmlToPlainTextConverter.cs"),
        (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\Utilities\Strings.cs")
    )
    $references = @(
        "/reference:System.dll",
        "/reference:System.Core.dll",
        "/reference:System.Drawing.dll",
        "/reference:System.Web.dll",
        "/reference:System.Web.Extensions.dll"
    )
    Get-ChildItem -Path $vendorDir -Filter "*.dll" | ForEach-Object {
        $references += "/reference:$($_.FullName)"
    }

	# Use a synthetic localized label containing HTML-sensitive characters so the
	# custom-template tests exercise both visible-text and attribute substitution.
    $attributeSafetyLocale = Join-Path $TempRoot "attribute-safety-messages.json"
    @'
{
  "sharing_html_share_link_label": {
    "message": "Nextcloud & \"link\" <safe>"
  }
}
'@ | Set-Content -Path $attributeSafetyLocale -Encoding UTF8
    $resources = @(
        "/resource:$((Resolve-Path (Join-Path $ProjectRoot 'src\NcTalkOutlookAddIn\Resources\header-transparent-164x48.png')).Path),NcTalkOutlookAddIn.Resources.header-transparent-164x48.png",
        "/resource:$((Resolve-Path (Join-Path $ProjectRoot 'src\NcTalkOutlookAddIn\Resources\_locales\en\messages.json')).Path),OutlookFileLinkRenderingTests.Resources._locales.en.messages.json",
        "/resource:$((Resolve-Path (Join-Path $ProjectRoot 'src\NcTalkOutlookAddIn\Resources\_locales\de\messages.json')).Path),OutlookFileLinkRenderingTests.Resources._locales.de.messages.json",
        "/resource:$((Resolve-Path $attributeSafetyLocale).Path),OutlookFileLinkRenderingTests.Resources._locales.fr.messages.json"
    )

    $exe = Join-Path $TempRoot "OutlookFileLinkRenderingTests.exe"
    Copy-Item -LiteralPath (Join-Path $ProjectRoot "src\NcTalkOutlookAddIn\app.config") -Destination ($exe + ".config")
    & $csc /nologo /nowarn:1702 /target:exe "/out:$exe" @references @resources @sources
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Get-ChildItem -Path $vendorDir -Filter "*.dll" | Copy-Item -Force -Destination $TempRoot

    & $exe
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    if (Test-Path $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force
    }
}
