/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web;
using NcTalkOutlookAddIn.Models;

namespace NcTalkOutlookAddIn.Utilities
{
    /**
     * Baut den HTML-Block für Freigabe-E-Mails (Download-Link, Passwort, Ablaufdatum, Rechte).
     * Das Layout ist bewusst statisch gehalten, um beim Einbetten in Outlook stabil zu bleiben.
     */
    internal static class FileLinkHtmlBuilder
    {
        private static readonly Lazy<string> LogoBase64 = new Lazy<string>(LoadLogoBase64);

        /**
         * Erzeugt den HTML-Block samt Branding und Freigabeinformationen.
         */
        internal static string Build(FileLinkResult result, FileLinkRequest request)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            var builder = new StringBuilder();
            builder.AppendLine("<div style=\"font-family:Calibri,'Segoe UI',Arial,sans-serif;font-size:11pt;color:#1f1f1f;margin:24px 0;background-color:#f5f5f5;\">");
            builder.AppendLine("<table role=\"presentation\" width=\"640\" style=\"border-collapse:collapse;width:640px;margin:0 auto;background-color:#ffffff;border:1px solid #c7c7c7;box-shadow:0 2px 6px rgba(0,0,0,0.12);\">");
            builder.AppendLine("<tr>");
            builder.AppendLine("<td style=\"padding:0;\">");
            builder.AppendLine("<table role=\"presentation\" width=\"640\" style=\"border-collapse:collapse;width:640px;background-color:#0078d4;height:54px;\">");
            builder.AppendLine("<tr>");
            builder.AppendLine("<td style=\"text-align:center;vertical-align:middle;background-color:#0078d4;\">");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<img alt=\"Nextcloud\" style=\"height:30px;width:auto;display:inline-block;\" height=\"30\" src=\"data:image/png;base64,{0}\" />",
                LogoBase64.Value);
            builder.AppendLine("</td>");
            builder.AppendLine("</tr>");
            builder.AppendLine("</table>");
            builder.AppendLine("<div style=\"padding:18px 22px 12px 22px;\">");
            if (request != null && request.NoteEnabled && !string.IsNullOrWhiteSpace(request.Note))
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "<p style=\"margin:0 0 14px 0;line-height:1.4;\">{0}</p>",
                    HttpUtility.HtmlEncode(request.Note));
            }
            builder.AppendLine("<p style=\"margin:0 0 14px 0;line-height:1.4;\">" + HttpUtility.HtmlEncode(Strings.FileLinkHtmlIntro) + "<br /></p>");
            builder.AppendLine("<table style=\"width:100%;border-collapse:collapse;margin-bottom:10px;\">");

            AppendRow(builder, Strings.FileLinkDownloadLabel, string.Format(
                CultureInfo.InvariantCulture,
                "<a href=\"{0}\" style=\"color:#0067c0;text-decoration:none;\">{0}</a>",
                HttpUtility.HtmlEncode(result.ShareUrl)));

            if (!string.IsNullOrEmpty(result.Password))
            {
                var passwordBuilder = new StringBuilder();
                passwordBuilder.Append("<span style=\"display:inline-block;font-family:'Consolas','Courier New',monospace;padding:2px 6px;border:1px solid #c7c7c7;border-radius:3px;background-color:#f4f4f4;-ms-user-select:all;user-select:all;\" ondblclick=\"try{window.getSelection().selectAllChildren(this);}catch(e){}\" onclick=\"try{window.getSelection().selectAllChildren(this);}catch(e){}\">");
                passwordBuilder.Append(HttpUtility.HtmlEncode(result.Password));
                passwordBuilder.Append("</span>");
                string passwordHtml = passwordBuilder.ToString();
                AppendRow(builder, Strings.FileLinkPasswordLabel, passwordHtml);
            }

            if (result.ExpireDate.HasValue)
            {
                AppendRow(builder, Strings.FileLinkExpireLabel, HttpUtility.HtmlEncode(result.ExpireDate.Value.ToString("d", CultureInfo.CurrentCulture)));
            }

            AppendRow(builder, Strings.FileLinkPermissionsLabel, BuildPermissions(result.Permissions));

            builder.AppendLine("</table>");
            builder.AppendLine("</div>");
            builder.AppendLine("<div style=\"padding:10px 22px 16px 22px;font-size:9pt;font-style:italic;color:#555555;\">");
            string nextcloudLink = "<a href=\"https://nextcloud.com/\" style=\"color:#0067c0;text-decoration:none;\">Nextcloud</a>";
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, Strings.FileLinkHtmlFooter, nextcloudLink));
            builder.AppendLine("</div>");
            builder.AppendLine("</td>");
            builder.AppendLine("</tr>");
            builder.AppendLine("</table>");
            builder.AppendLine("</div>");
            return builder.ToString();
        }

        /**
         * Fügt eine Tabellenzeile mit Label und Inhalt hinzu.
         */
        private static void AppendRow(StringBuilder builder, string label, string valueHtml)
        {
            builder.AppendLine("<tr>");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<th style=\"text-align:left;width:12ch;vertical-align:top;padding:6px 10px 6px 0;color:#333333;\">{0}</th>",
                HttpUtility.HtmlEncode(label));
            builder.Append("<td style=\"padding:6px 0;max-width:50ch;word-break:break-word;\">");
            builder.Append(valueHtml ?? string.Empty);
            builder.Append("</td>");
            builder.AppendLine("</tr>");
        }

        /**
         * Rendert die Auflistung der Berechtigungen (Häkchen/rote Kreuze).
         */
        private static string BuildPermissions(FileLinkPermissionFlags permissions)
        {
            var builder = new StringBuilder();
            builder.Append("<table style=\"border-collapse:collapse;\">");
            builder.Append("<tr>");
            AppendPermissionCell(builder, "Lesen", (permissions & FileLinkPermissionFlags.Read) == FileLinkPermissionFlags.Read);
            AppendPermissionCell(builder, "Erstellen", (permissions & FileLinkPermissionFlags.Create) == FileLinkPermissionFlags.Create);
            AppendPermissionCell(builder, "Bearbeiten", (permissions & FileLinkPermissionFlags.Write) == FileLinkPermissionFlags.Write);
            AppendPermissionCell(builder, "L&ouml;schen", (permissions & FileLinkPermissionFlags.Delete) == FileLinkPermissionFlags.Delete);
            builder.Append("</tr>");
            builder.Append("</table>");
            return builder.ToString();
        }

        /**
         * Baut eine Zelle für eine einzelne Berechtigung.
         */
        private static void AppendPermissionCell(StringBuilder builder, string label, bool enabled)
        {
            builder.Append("<td style=\"padding:0 18px 6px 0;\">");
            builder.Append("<span style=\"display:inline-flex;align-items:center;\">");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<span style=\"display:inline-flex;align-items:center;justify-content:center;width:16px;height:16px;border:1px solid {0};color:{0};font-size:13px;font-weight:700;\">{1}</span>",
                enabled ? "#0078d4" : "#c62828",
                enabled ? "&#10003;" : "&#10007;");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<span style=\"padding-left:6px;font-weight:600;\">{0}</span>",
                label);
            builder.Append("</span>");
            builder.Append("</td>");
        }

        /**
         * Lädt das eingebettete Nextcloud-Logo als Base64-String für den Header.
         */
        private static string LoadLogoBase64()
        {
            const string resource = "NcTalkOutlookAddIn.Resources.nextcloud-filelink.png";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    return string.Empty;
                }

                using (var reader = new BinaryReader(stream))
                {
                    byte[] data = reader.ReadBytes((int)stream.Length);
                    return Convert.ToBase64String(data);
                }
            }
        }
    }
}
