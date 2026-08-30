namespace NcTalkOutlookAddIn.Utilities
{
    using System.Web;

    /// <summary>
    /// Encodes short semantic values that must remain one visual token after
    /// Outlook Classic passes the HTML through the Word compose engine.
    /// </summary>
    internal static class HtmlNoBreakEncoder
    {
        internal static string EncodeFieldLabel(string value)
        {
            return EncodeToken(value);
        }

        internal static string EncodeDateTime(string value)
        {
            return EncodeToken(value);
        }

        private static string EncodeToken(string value)
        {
            string encoded = HttpUtility.HtmlEncode(value ?? string.Empty);

            // These character-level protections survive Outlook's Word HTML
            // rewriting even when white-space CSS is removed.
            encoded = encoded
                .Replace(" ", "&nbsp;")
                .Replace("-", "&#8209;");

            return "<nobr style=\"white-space: nowrap;\">"
                + encoded
                + "</nobr>";
        }
    }
}
