// Copyright (c) 2025 Bastian Kleinschmidt
// Licensed under the GNU Affero General Public License v3.0.
// See LICENSE.txt for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using NcTalkOutlookAddIn.Models;

namespace NcTalkOutlookAddIn.Utilities
{
        // Builds the HTML block inserted into mail compose windows (download link, password, expiration, permissions).
    // The layout is intentionally kept static to remain stable when embedded in Outlook.
    internal static class FileLinkHtmlBuilder
    {
        private const string HomepageUrl = "https://nc-connector.de";
        private const string ShareTemplateKey = "share_html_block_template";
        private const string ShareTemplateV2Key = "share_html_block_template_v2";
        private const string ShareTemplateEffectiveLanguageKey = "share_html_block_effective_language";
        // Outlook's Word renderer ignores div padding and interprets ch widths differently.
        // A fixed table column keeps the built-in block aligned with Thunderbird.
        private const int FieldLabelWidth = 124;
        private static readonly Lazy<string> HeaderBase64 = new Lazy<string>(LoadHeaderBase64);

                // Creates the HTML block including branding and share information.
        internal static string Build(FileLinkResult result, FileLinkRequest request, string languageOverride, BackendPolicyStatus policyStatus = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }
            bool attachmentMode = request != null && request.AttachmentMode;
            bool zipDownloadLink = attachmentMode
                && request.AttachmentLinkTarget == AttachmentLinkTarget.ZipDownload;
            bool separatePassword = request != null
                && request.PasswordSeparateEnabled
                && !string.IsNullOrWhiteSpace(result.Password);
            string templateMode = ResolveEffectiveLanguage(languageOverride, policyStatus);
            string policyTemplate = ResolvePolicyTemplate(policyStatus, false, templateMode);
            string effectiveLanguage = ResolveShareRenderLanguage(policyStatus, templateMode, policyTemplate);
            if (!string.IsNullOrWhiteSpace(policyTemplate))
            {
                return RenderPolicyTemplate(
                    policyTemplate,
                    result,
                    request,
                    effectiveLanguage,
                    attachmentMode,
                    zipDownloadLink,
                    separatePassword,
                    passwordOnly: false,
                    secretLink: false);
            }
            string intro = GetShareLinkIntro(effectiveLanguage, zipDownloadLink, false);

            string footerFormat = Strings.GetInLanguage(
                effectiveLanguage,
                "sharing_html_footer",
                "{0} is a solution for secure email and file exchange.");

            string linkLabel = GetShareLinkLabel(effectiveLanguage, zipDownloadLink);
            string passwordLabel = Strings.GetInLanguage(effectiveLanguage, "sharing_html_password_label", "Password");
            string expireLabel = Strings.GetInLanguage(effectiveLanguage, "sharing_html_expire_label", "Expiration date");
            string permissionsLabel = Strings.GetInLanguage(effectiveLanguage, "sharing_html_permissions_label", "Your permissions");
            string passwordSeparateHint = Strings.GetInLanguage(effectiveLanguage, "sharing_html_password_separate_hint", "The password will be sent in a separate email.");

            string permissionRead = Strings.GetInLanguage(effectiveLanguage, "sharing_permission_read", "Read");
            string permissionCreate = Strings.GetInLanguage(effectiveLanguage, "sharing_permission_create", "Upload");
            string permissionWrite = Strings.GetInLanguage(effectiveLanguage, "sharing_permission_write", "Modify");
            string permissionDelete = Strings.GetInLanguage(effectiveLanguage, "sharing_permission_delete", "Delete");

            string brandBlue = BrandingAssets.BrandBlueHex;

            var builder = new StringBuilder();
            AppendOutlookFrameStart(
                builder,
                brandBlue,
                closeHeaderAnchorOnImageLine: true);
            if (request != null && request.NoteEnabled && !string.IsNullOrWhiteSpace(request.Note))
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "<p style=\"margin:0 0 14px 0;line-height:1.4;\">{0}</p>",
                    HttpUtility.HtmlEncode(request.Note));
            }
            builder.AppendLine("<p style=\"margin:0 0 14px 0;line-height:1.4;\">" + HttpUtility.HtmlEncode(intro) + "<br /></p>");
            AppendFieldTableStart(builder);

            string linkUrl = zipDownloadLink
                ? BuildAttachmentZipDownloadUrl(result.ShareUrl, result.ShareToken)
                : (result.ShareUrl ?? string.Empty);
            AppendRow(builder, linkLabel, string.Format(
                CultureInfo.InvariantCulture,
                "<a href=\"{0}\" style=\"color:{1};text-decoration:none;\">{0}</a>",
                HttpUtility.HtmlEncode(linkUrl),
                brandBlue));

            if (!string.IsNullOrEmpty(result.Password) && !separatePassword)
            {
                AppendRow(builder, passwordLabel, BuildPasswordValueHtml(result.Password));
            }
            else if (separatePassword)
            {
                AppendRow(builder, passwordLabel, HttpUtility.HtmlEncode(passwordSeparateHint));
            }
            if (result.ExpireDate.HasValue)
            {
                string expirationDateText =
                    result.ExpireDate.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture);

                AppendRow(
                    builder,
                    expireLabel,
                    HtmlNoBreakEncoder.EncodeDateTime(
                        expirationDateText));
            }
            if (!attachmentMode)
            {
                AppendRow(builder, permissionsLabel, BuildPermissions(result.Permissions, permissionRead, permissionCreate, permissionWrite, permissionDelete));
            }

            builder.AppendLine("</table>");
            AppendOutlookContentEnd(builder);
            string nextcloudLink = string.Format(CultureInfo.InvariantCulture, "<a href=\"https://nextcloud.com/\" style=\"color:{0};text-decoration:none;\">Nextcloud</a>", brandBlue);
            AppendOutlookFrameEnd(
                builder,
                string.Format(
                    CultureInfo.InvariantCulture,
                    footerFormat,
                    nextcloudLink));
            return builder.ToString();
        }

                // Creates the password-only follow-up HTML block.
        internal static string BuildPasswordOnly(FileLinkResult result, string languageOverride, BackendPolicyStatus policyStatus = null, bool secretLink = false)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }
            string templateMode = ResolveEffectiveLanguage(languageOverride, policyStatus);
            string policyTemplate = ResolvePolicyTemplate(policyStatus, true, templateMode);
            string effectiveLanguage = ResolveShareRenderLanguage(policyStatus, templateMode, policyTemplate);
            if (!string.IsNullOrWhiteSpace(policyTemplate))
            {
                return RenderPolicyTemplate(
                    policyTemplate,
                    result,
                    request: null,
                    effectiveLanguage: effectiveLanguage,
                    attachmentMode: false,
                    zipDownloadLink: false,
                    separatePassword: false,
                    passwordOnly: true,
                    secretLink: secretLink);
            }
            string intro = secretLink
                ? Strings.GetInLanguage(
                    effectiveLanguage,
                    "sharing_html_secret_mail_intro",
                    "Open this one-time secret link to view the password for the shared link.")
                : Strings.GetInLanguage(
                    effectiveLanguage,
                    "sharing_html_password_mail_intro",
                    "Here is your password for the shared link.");
            string passwordLabel = Strings.GetInLanguage(effectiveLanguage, "sharing_html_password_label", "Password");
            string secretLinkLabel = Strings.GetInLanguage(effectiveLanguage, "sharing_html_secret_link_label", "Secret link");
            string brandBlue = BrandingAssets.BrandBlueHex;

            var builder = new StringBuilder();
            AppendOutlookFrameStart(
                builder,
                brandBlue,
                closeHeaderAnchorOnImageLine: false);
            builder.AppendLine("<p style=\"margin:0 0 14px 0;line-height:1.4;\">" + HttpUtility.HtmlEncode(intro) + "<br /></p>");
            AppendFieldTableStart(builder);
            AppendRow(builder, passwordLabel, secretLink
                ? BuildSecretLinkValueHtml(result.Password, secretLinkLabel, brandBlue)
                : BuildPasswordValueHtml(result.Password));
            builder.AppendLine("</table>");
            AppendOutlookContentEnd(builder);
            AppendOutlookFrameEnd(builder, null);
            return builder.ToString();
        }

        internal static string BuildPlainText(FileLinkResult result, FileLinkRequest request, string languageOverride, BackendPolicyStatus policyStatus = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            bool attachmentMode = request != null && request.AttachmentMode;
            bool zipDownloadLink = attachmentMode
                && request.AttachmentLinkTarget == AttachmentLinkTarget.ZipDownload;
            bool separatePassword = request != null
                && request.PasswordSeparateEnabled
                && !string.IsNullOrWhiteSpace(result.Password);
            string templateMode = ResolveEffectiveLanguage(languageOverride, policyStatus);
            string policyTemplate = ResolvePolicyTemplate(policyStatus, false, templateMode);
            string effectiveLanguage = ResolveShareRenderLanguage(policyStatus, templateMode, policyTemplate);
            if (!string.IsNullOrWhiteSpace(policyTemplate))
            {
                return FramePlainTextBlock(RenderPolicyTemplatePlainText(
                    policyTemplate,
                    result,
                    request,
                    effectiveLanguage,
                    attachmentMode,
                    zipDownloadLink,
                    separatePassword,
                    passwordOnly: false));
            }

            string linkUrl = zipDownloadLink
                ? BuildAttachmentZipDownloadUrl(result.ShareUrl, result.ShareToken)
                : (result.ShareUrl ?? string.Empty);

            string passwordLabel = Strings.GetInLanguage(effectiveLanguage, "sharing_html_password_label", "Password");
            string passwordSeparateHint = Strings.GetInLanguage(effectiveLanguage, "sharing_html_password_separate_hint", "The password will be sent in a separate email.");
            var sections = new List<string>();
            if (request != null && request.NoteEnabled && !string.IsNullOrWhiteSpace(request.Note))
            {
                sections.Add(NormalizePlainTextBlock(request.Note));
            }
            sections.Add(NormalizePlainTextBlock(GetShareLinkIntro(effectiveLanguage, zipDownloadLink, true)));

            var fields = new List<string>();
            fields.Add(BuildPlainTextField(GetShareLinkLabel(effectiveLanguage, zipDownloadLink), linkUrl));
            if (!string.IsNullOrWhiteSpace(result.Password) && !separatePassword)
            {
                fields.Add(BuildPlainTextField(passwordLabel, result.Password));
            }
            else if (separatePassword)
            {
                fields.Add(BuildPlainTextField(passwordLabel, passwordSeparateHint));
            }
            if (result.ExpireDate.HasValue)
            {
                fields.Add(BuildPlainTextField(
                    Strings.GetInLanguage(effectiveLanguage, "sharing_html_expire_label", "Expiration date"),
                    result.ExpireDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }
            if (!attachmentMode)
            {
                fields.Add(BuildPlainTextField(
                    Strings.GetInLanguage(effectiveLanguage, "sharing_html_permissions_label", "Your permissions"),
                    BuildPermissionsPlainTextDisplay(
                        result.Permissions,
                        Strings.GetInLanguage(effectiveLanguage, "sharing_permission_read", "Read"),
                        Strings.GetInLanguage(effectiveLanguage, "sharing_permission_create", "Upload"),
                        Strings.GetInLanguage(effectiveLanguage, "sharing_permission_write", "Modify"),
                        Strings.GetInLanguage(effectiveLanguage, "sharing_permission_delete", "Delete"))));
            }

            string fieldsText = NormalizePlainTextBlock(string.Join("\r\n", fields.FindAll(value => !string.IsNullOrWhiteSpace(value)).ToArray()));
            if (!string.IsNullOrWhiteSpace(fieldsText))
            {
                sections.Add(fieldsText);
            }

            string footer = Strings.GetInLanguage(effectiveLanguage, "sharing_html_footer_line", "Nextcloud is a solution for secure e-mail and data exchange.");
            if (!string.IsNullOrWhiteSpace(footer))
            {
                sections.Add(NormalizePlainTextBlock(footer));
            }

            return FramePlainTextBlock(string.Join("\r\n\r\n", sections.FindAll(value => !string.IsNullOrWhiteSpace(value)).ToArray()));
        }

        internal static string BuildPasswordOnlyPlainText(FileLinkResult result, string languageOverride, BackendPolicyStatus policyStatus = null, bool secretLink = false)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            string templateMode = ResolveEffectiveLanguage(languageOverride, policyStatus);
            string policyTemplate = ResolvePolicyTemplate(policyStatus, true, templateMode);
            string effectiveLanguage = ResolveShareRenderLanguage(policyStatus, templateMode, policyTemplate);
            if (!string.IsNullOrWhiteSpace(policyTemplate))
            {
                return FramePlainTextBlock(RenderPolicyTemplatePlainText(
                    policyTemplate,
                    result,
                    request: null,
                    effectiveLanguage: effectiveLanguage,
                    attachmentMode: false,
                    zipDownloadLink: false,
                    separatePassword: false,
                    passwordOnly: true));
            }

            var sections = new List<string>();
            sections.Add(NormalizePlainTextBlock(secretLink
                ? Strings.GetInLanguage(
                    effectiveLanguage,
                    "sharing_html_secret_mail_intro",
                    "Open this one-time secret link to view the password for the shared link.")
                : Strings.GetInLanguage(
                    effectiveLanguage,
                    "sharing_html_password_mail_intro",
                    "Here is your password for the shared link.")));
            sections.Add(BuildPlainTextField(
                secretLink
                    ? Strings.GetInLanguage(effectiveLanguage, "sharing_html_secret_link_label", "Secret link")
                    : Strings.GetInLanguage(effectiveLanguage, "sharing_html_password_label", "Password"),
                result.Password));

            return FramePlainTextBlock(string.Join("\r\n\r\n", sections.FindAll(value => !string.IsNullOrWhiteSpace(value)).ToArray()));
        }

                // Resolve effective HTML block language with policy override support.
        private static string ResolveEffectiveLanguage(string languageOverride, BackendPolicyStatus policyStatus)
        {
            if (policyStatus != null
                && policyStatus.IsDomainActive("share")
                && policyStatus.IsLocked("share", "language_share_html_block"))
            {
                string policyLang = policyStatus.GetPolicyString("share", "language_share_html_block");
                if (!string.IsNullOrWhiteSpace(policyLang))
                {
                    return string.Equals(policyLang, "custom", StringComparison.OrdinalIgnoreCase)
                        ? "custom"
                        : Strings.NormalizeLanguageOverride(policyLang);
                }
            }
            string normalized = Strings.NormalizeLanguageOverride(languageOverride);
            if (string.Equals((languageOverride ?? string.Empty).Trim(), "custom", StringComparison.OrdinalIgnoreCase))
            {
                return "custom";
            }
            return normalized;
        }

        private static string ResolveShareRenderLanguage(BackendPolicyStatus policyStatus, string templateMode, string policyTemplate)
        {
            if (!string.Equals(templateMode, "custom", StringComparison.OrdinalIgnoreCase))
            {
                return templateMode;
            }
            if (string.IsNullOrWhiteSpace(policyTemplate))
            {
                return "default";
            }

            // `custom` selects the backend template; generated labels use its separately reported language.
            string backendLanguage = policyStatus == null
                ? string.Empty
                : policyStatus.GetPolicyString("share", ShareTemplateEffectiveLanguageKey);
            if (string.IsNullOrWhiteSpace(backendLanguage)
                || string.Equals(backendLanguage.Trim(), "custom", StringComparison.OrdinalIgnoreCase))
            {
                return templateMode;
            }
            return Strings.NormalizeLanguageOverride(backendLanguage);
        }

                // Resolve custom policy template for normal or password-only mode.
        private static string ResolvePolicyTemplate(BackendPolicyStatus policyStatus, bool passwordOnly, string effectiveLanguage)
        {
            if (policyStatus == null || !policyStatus.IsDomainActive("share"))
            {
                return string.Empty;
            }
            if (!string.Equals(effectiveLanguage, "custom", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
            if (passwordOnly)
            {
                return policyStatus.GetPolicyString("share", "share_password_template") ?? string.Empty;
            }

            // New backends keep the original key placeholder-free for clients that predate mode-aware link text.
            string template = policyStatus.GetPolicyString("share", ShareTemplateV2Key);
            if (string.IsNullOrWhiteSpace(template))
            {
                template = policyStatus.GetPolicyString("share", ShareTemplateKey);
            }
            return template ?? string.Empty;
        }

                // Render one backend-provided custom HTML template.
        private static string RenderPolicyTemplate(
            string template,
            FileLinkResult result,
            FileLinkRequest request,
            string effectiveLanguage,
            bool attachmentMode,
            bool zipDownloadLink,
            bool separatePassword,
            bool passwordOnly,
            bool secretLink)
        {
            if (string.IsNullOrWhiteSpace(template) || result == null)
            {
                return string.Empty;
            }
            PolicyTemplateValues values = BuildPolicyTemplateValues(
                result,
                request,
                effectiveLanguage,
                attachmentMode,
                zipDownloadLink,
                separatePassword,
                passwordOnly,
                plainText: false);
            string passwordReplacement =
                passwordOnly && secretLink
                    ? BuildSecretLinkValueHtml(
                        values.Password,
                        Strings.GetInLanguage(effectiveLanguage, "sharing_html_secret_link_label", "Secret link"),
                        BrandingAssets.BrandBlueHex)
                    : HttpUtility.HtmlEncode(values.Password);

            // Policy placeholders may occur in either visible content or HTML attributes.
            // Keep replacements context-neutral and attribute-safe until the template
            // renderer supports context-aware substitution.
            string html = ReplacePolicyTemplatePlaceholders(
                template,
                attachmentMode,
                HttpUtility.HtmlEncode(values.LinkUrl ?? string.Empty),
                passwordReplacement,
                HttpUtility.HtmlEncode(values.ExpirationDate),
                values.Rights,
                HttpUtility.HtmlEncode(values.Note),
                HttpUtility.HtmlEncode(values.LinkIntro),
                HttpUtility.HtmlEncode(values.LinkLabel));

            string sanitized = HtmlTemplateSanitizer.SanitizeShareTemplateHtml(html);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                throw new InvalidOperationException("Share HTML template sanitized to empty output.");
            }
            return sanitized;
        }

        private static string RenderPolicyTemplatePlainText(
            string template,
            FileLinkResult result,
            FileLinkRequest request,
            string effectiveLanguage,
            bool attachmentMode,
            bool zipDownloadLink,
            bool separatePassword,
            bool passwordOnly)
        {
            if (string.IsNullOrWhiteSpace(template) || result == null)
            {
                return string.Empty;
            }

            PolicyTemplateValues values = BuildPolicyTemplateValues(
                result,
                request,
                effectiveLanguage,
                attachmentMode,
                zipDownloadLink,
                separatePassword,
                passwordOnly,
                plainText: true);
            string html = ReplacePolicyTemplatePlaceholders(
                template,
                attachmentMode,
                PlainTextToTemplateHtml(values.LinkUrl ?? string.Empty),
                PlainTextToTemplateHtml(values.Password),
                PlainTextToTemplateHtml(values.ExpirationDate),
                PlainTextToTemplateHtml(values.Rights),
                PlainTextToTemplateHtml(values.Note),
                PlainTextToTemplateHtml(values.LinkIntro),
                PlainTextToTemplateHtml(values.LinkLabel));

            string sanitized = HtmlTemplateSanitizer.SanitizeShareTemplateHtml(html);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                throw new InvalidOperationException("Share plain-text template sanitized to empty output.");
            }

            string plainText = HtmlToPlainTextConverter.Convert(sanitized);
            if (string.IsNullOrWhiteSpace(plainText))
            {
                throw new InvalidOperationException("Share plain-text template rendered to empty output.");
            }

            return plainText;
        }

        private static PolicyTemplateValues BuildPolicyTemplateValues(
            FileLinkResult result,
            FileLinkRequest request,
            string effectiveLanguage,
            bool attachmentMode,
            bool zipDownloadLink,
            bool separatePassword,
            bool passwordOnly,
            bool plainText)
        {
            string passwordSeparateHint = Strings.GetInLanguage(effectiveLanguage, "sharing_html_password_separate_hint", "The password will be sent in a separate email.");
            string permissionRead = Strings.GetInLanguage(effectiveLanguage, "sharing_permission_read", "Read");
            string permissionCreate = Strings.GetInLanguage(effectiveLanguage, "sharing_permission_create", "Upload");
            string permissionWrite = Strings.GetInLanguage(effectiveLanguage, "sharing_permission_write", "Modify");
            string permissionDelete = Strings.GetInLanguage(effectiveLanguage, "sharing_permission_delete", "Delete");

            string password = result.Password ?? string.Empty;
            if (!passwordOnly && separatePassword)
            {
                password = passwordSeparateHint;
            }

            string note = string.Empty;
            if (request != null && request.NoteEnabled && !string.IsNullOrWhiteSpace(request.Note))
            {
                note = request.Note.Trim();
            }

            string rights = string.Empty;
            if (!attachmentMode)
            {
                rights = plainText
                    ? BuildPermissionsPlainTextDisplay(result.Permissions, permissionRead, permissionCreate, permissionWrite, permissionDelete)
                    : BuildPermissions(result.Permissions, permissionRead, permissionCreate, permissionWrite, permissionDelete);
            }

            return new PolicyTemplateValues
            {
                LinkUrl = zipDownloadLink
                    ? BuildAttachmentZipDownloadUrl(result.ShareUrl, result.ShareToken)
                    : (result.ShareUrl ?? string.Empty),
                Password = password,
                ExpirationDate = result.ExpireDate.HasValue
                    ? result.ExpireDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : string.Empty,
                Rights = rights,
                Note = note,
                LinkIntro = passwordOnly ? string.Empty : GetShareLinkIntro(effectiveLanguage, zipDownloadLink, plainText),
                LinkLabel = passwordOnly ? string.Empty : GetShareLinkLabel(effectiveLanguage, zipDownloadLink)
            };
        }

        private static string ReplacePolicyTemplatePlaceholders(
            string template,
            bool attachmentMode,
            string url,
            string password,
            string expirationDate,
            string rights,
            string note,
            string linkIntro,
            string linkLabel)
        {
            var emptyPlaceholders = new List<string>();
            if (string.IsNullOrWhiteSpace(url))
            {
                emptyPlaceholders.Add("URL");
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                emptyPlaceholders.Add("PASSWORD");
            }
            if (string.IsNullOrWhiteSpace(expirationDate))
            {
                emptyPlaceholders.Add("EXPIRATIONDATE");
            }
            if (attachmentMode || string.IsNullOrWhiteSpace(rights))
            {
                emptyPlaceholders.Add("RIGHTS");
            }
            if (string.IsNullOrWhiteSpace(note))
            {
                emptyPlaceholders.Add("NOTE");
            }
            string output = PruneEmptyTemplatePlaceholders(
                template,
                emptyPlaceholders);
            output = output.Replace("{URL}", url);
            output = output.Replace("{PASSWORD}", password);
            output = output.Replace("{EXPIRATIONDATE}", expirationDate);
            output = output.Replace("{RIGHTS}", rights);
            output = output.Replace("{NOTE}", note);
            output = output.Replace("{LINK_INTRO}", linkIntro);
            output = output.Replace("{LINK_LABEL}", linkLabel);
            return output;
        }

        private static string GetShareLinkIntro(string effectiveLanguage, bool zipDownloadLink, bool plainText)
        {
            if (zipDownloadLink)
            {
                return Strings.GetInLanguage(
                    effectiveLanguage,
                    "sharing_html_zip_download_intro",
                    "The files have been provided securely via Nextcloud. Download the shared files as a ZIP archive using the link below.");
            }
            return Strings.GetInLanguage(
                effectiveLanguage,
                plainText ? "sharing_html_intro_line" : "sharing_html_intro",
                "The files have been provided securely via Nextcloud. Open the Nextcloud link below to view the share.");
        }

        private static string GetShareLinkLabel(string effectiveLanguage, bool zipDownloadLink)
        {
            return Strings.GetInLanguage(
                effectiveLanguage,
                zipDownloadLink ? "sharing_html_download_label" : "sharing_html_share_link_label",
                zipDownloadLink ? "ZIP download" : "Nextcloud link");
        }

        private static string PlainTextToTemplateHtml(string value)
        {
            string encoded = HttpUtility.HtmlEncode(NormalizePlainTextBlock(value));
            return encoded.Replace("\r\n", "<br />");
        }

        private static string FramePlainTextBlock(string plainText)
        {
            string normalized = NormalizePlainTextBlock(plainText);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidOperationException("Share plain-text block rendered to empty output.");
            }

            string border = new string('#', 60);
            return border + "\r\n" + normalized + "\r\n" + border;
        }

        private static string BuildPlainTextField(string label, string value)
        {
            string normalizedValue = NormalizePlainTextBlock(value);
            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                return string.Empty;
            }

            return (label ?? string.Empty).Trim() + ": " + normalizedValue;
        }

        private static string BuildPermissionsPlainTextDisplay(
            FileLinkPermissionFlags permissions,
            string readLabel,
            string createLabel,
            string writeLabel,
            string deleteLabel)
        {
            var values = new List<string>();
            values.Add(BuildPermissionPlainText(readLabel, (permissions & FileLinkPermissionFlags.Read) == FileLinkPermissionFlags.Read));
            values.Add(BuildPermissionPlainText(createLabel, (permissions & FileLinkPermissionFlags.Create) == FileLinkPermissionFlags.Create));
            values.Add(BuildPermissionPlainText(writeLabel, (permissions & FileLinkPermissionFlags.Write) == FileLinkPermissionFlags.Write));
            values.Add(BuildPermissionPlainText(deleteLabel, (permissions & FileLinkPermissionFlags.Delete) == FileLinkPermissionFlags.Delete));
            return string.Join(", ", values.ToArray());
        }

        private static string BuildPermissionPlainText(string label, bool enabled)
        {
            return (enabled ? "[x] " : "[ ] ") + (label ?? string.Empty).Trim();
        }

        private static string NormalizePlainTextBlock(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u00A0', ' ');
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }

            normalized = string.Join("\n", lines).Trim('\n', ' ', '\t');
            while (normalized.Contains("\n\n\n"))
            {
                normalized = normalized.Replace("\n\n\n", "\n\n");
            }

            return normalized.Replace("\n", "\r\n").Trim();
        }

        private sealed class PolicyTemplateValues
        {
            internal string LinkUrl { get; set; }

            internal string Password { get; set; }

            internal string ExpirationDate { get; set; }

            internal string Rights { get; set; }

            internal string Note { get; set; }

            internal string LinkIntro { get; set; }

            internal string LinkLabel { get; set; }
        }

                // Remove the nearest block wrapper for empty template values.
        private static string PruneEmptyTemplatePlaceholders(
            string template,
            IList<string> placeholders)
        {
            string source = template ?? string.Empty;
            if (string.IsNullOrWhiteSpace(source)
                || placeholders == null
                || placeholders.Count == 0)
            {
                return source;
            }

            var tokens = new List<string>();
            foreach (string placeholder in placeholders)
            {
                string normalized = (placeholder ?? string.Empty).Trim();
                if (normalized.Length == 0)
                {
                    continue;
                }
                string token = "{" + normalized + "}";
                if (source.IndexOf(token, StringComparison.Ordinal) >= 0
                    && !tokens.Contains(token, StringComparer.Ordinal))
                {
                    tokens.Add(token);
                }
            }
            if (tokens.Count == 0)
            {
                return source;
            }

            var parser = new HtmlParser();
            var document = parser.ParseDocument(source);
            IElement body = document != null ? document.Body : null;
            if (body == null)
            {
                throw new InvalidOperationException(
                    "Share template could not be parsed for placeholder pruning.");
            }

            foreach (string token in tokens)
            {
                IElement[] candidates = body
                    .QuerySelectorAll(
                        "tr,li,p,div,section,article,aside,header,footer")
                    .ToArray();
                for (int index = candidates.Length - 1;
                    index >= 0;
                    index--)
                {
                    IElement candidate = candidates[index];
                    if (candidate.Parent == null
                        || candidate.InnerHtml.IndexOf(
                            token,
                            StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }
                    candidate.Remove();
                }
            }

            string output = body.InnerHtml;
            foreach (string token in tokens)
            {
                output = output.Replace(token, string.Empty);
            }
            return output;
        }

        private static void AppendOutlookFrameStart(
            StringBuilder builder,
            string brandBlue,
            bool closeHeaderAnchorOnImageLine)
        {
            builder.AppendLine("<div style=\"font-family:Calibri,'Segoe UI',Arial,sans-serif;font-size:11pt;margin:16px 0;\">");
            builder.AppendLine("<table role=\"presentation\" width=\"640\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:separate;border-spacing:0;width:640px;margin:0;background-color:transparent;border:1px solid #d7d7db;border-radius:8px;overflow:hidden;\">");
            builder.AppendLine("<tr>");
            builder.AppendLine("<td valign=\"top\" style=\"padding:0;vertical-align:top;\">");
            builder.AppendLine("<table role=\"presentation\" width=\"640\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:640px;margin:0;background-color:transparent;\">");
            builder.AppendLine("<tr>");
            builder.AppendFormat(CultureInfo.InvariantCulture, "<td height=\"32\" bgcolor=\"{0}\" style=\"padding:0;background-color:{0};text-align:center;height:32px;line-height:0;font-size:0;mso-line-height-rule:exactly;\">", brandBlue);
            builder.AppendLine();
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<a href=\"{0}\" style=\"display:inline-block;text-decoration:none;line-height:0;font-size:0;vertical-align:middle;\" target=\"_blank\" rel=\"noopener\">",
                HomepageUrl);
            builder.AppendLine();
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<img alt=\"\" height=\"32\" style=\"display:block;width:auto;height:32px;max-width:164px;object-fit:contain;border:0;margin:0 auto;\" src=\"data:image/png;base64,{0}\" />",
                HeaderBase64.Value);
            if (!closeHeaderAnchorOnImageLine)
            {
                builder.AppendLine();
            }
            builder.AppendLine("</a>");
            builder.AppendLine("</td>");
            builder.AppendLine("</tr>");
            builder.AppendLine("</table>");
            builder.AppendLine("<table role=\"presentation\" width=\"100%\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:100%;margin:0;background-color:transparent;\">");
            builder.AppendLine("<tr>");
            builder.AppendLine("<td valign=\"top\" style=\"padding:18px 18px 22px 18px;vertical-align:top;font-family:Calibri,'Segoe UI',Arial,sans-serif;font-size:11pt;\">");
        }

        private static void AppendFieldTableStart(StringBuilder builder)
        {
            builder.AppendLine("<table role=\"presentation\" width=\"100%\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"width:100%;border-collapse:collapse;margin:0;\">");
        }

        private static void AppendOutlookContentEnd(StringBuilder builder)
        {
            builder.AppendLine("</td>");
            builder.AppendLine("</tr>");
            builder.AppendLine("</table>");
        }

        private static void AppendOutlookFrameEnd(
            StringBuilder builder,
            string footerHtml)
        {
            if (footerHtml != null)
            {
                builder.AppendLine("<table role=\"presentation\" width=\"100%\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:100%;margin:0;background-color:transparent;\">");
                builder.AppendLine("<tr>");
                builder.AppendLine("<td valign=\"top\" style=\"padding:10px 18px 16px 18px;font-family:Calibri,'Segoe UI',Arial,sans-serif;font-size:9pt;font-style:italic;vertical-align:top;\">");
                builder.AppendLine(footerHtml);
                builder.AppendLine("</td>");
                builder.AppendLine("</tr>");
                builder.AppendLine("</table>");
            }
            builder.AppendLine("</td>");
            builder.AppendLine("</tr>");
            builder.AppendLine("</table>");
            builder.AppendLine("</div>");
        }

                // Adds a table row with label and content.
        private static void AppendRow(StringBuilder builder, string label, string valueHtml)
        {
            string encodedLabel = HtmlNoBreakEncoder.EncodeFieldLabel(label ?? string.Empty);

            builder.AppendLine("<tr>");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<th width=\"{0}\" valign=\"top\" style=\"text-align:left;width:{0}px;vertical-align:top;padding:6px 10px 6px 0;font-family:Calibri,'Segoe UI',Arial,sans-serif;font-size:11pt;\">{1}</th>",
                FieldLabelWidth,
                encodedLabel);
            builder.Append("<td valign=\"top\" style=\"padding:6px 0;max-width:50ch;word-break:break-word;vertical-align:top;font-family:Calibri,'Segoe UI',Arial,sans-serif;font-size:11pt;\">");
            builder.Append(valueHtml ?? string.Empty);
            builder.Append("</td>");
            builder.AppendLine("</tr>");
        }

        private static string BuildPasswordValueHtml(string password)
        {
            var passwordBuilder = new StringBuilder();
            passwordBuilder.Append("<span style=\"display:inline-block;font-family:'Consolas','Courier New',monospace;padding:2px 6px;border:1px solid #c7c7c7;border-radius:3px;-ms-user-select:all;user-select:all;\">");
            passwordBuilder.Append(HttpUtility.HtmlEncode(password ?? string.Empty));
            passwordBuilder.Append("</span>");
            return passwordBuilder.ToString();
        }

        private static string BuildSecretLinkValueHtml(string secretUrl, string linkText, string brandBlue)
        {
            string href = HttpUtility.HtmlAttributeEncode(secretUrl ?? string.Empty);
            string text = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(linkText) ? "Secret link" : linkText.Trim());
            string color = string.IsNullOrWhiteSpace(brandBlue) ? BrandingAssets.BrandBlueHex : brandBlue;
            return string.Format(
                CultureInfo.InvariantCulture,
                "<a href=\"{0}\" style=\"color:{1};font-weight:bold;text-decoration:underline;word-break:normal;\" target=\"_blank\" rel=\"noopener\">{2}</a>",
                href,
                HttpUtility.HtmlAttributeEncode(color),
                text);
        }

        private static string BuildAttachmentZipDownloadUrl(string shareUrl, string shareToken)
        {
            if (string.IsNullOrWhiteSpace(shareUrl))
            {
                throw new InvalidOperationException(Strings.SharingAttachmentLinkTargetInvalidUrl);
            }
            string token = string.IsNullOrWhiteSpace(shareToken) ? string.Empty : shareToken.Trim();
            Uri shareUri;
            if (!Uri.TryCreate(shareUrl.Trim(), UriKind.Absolute, out shareUri)
                || !(string.Equals(shareUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(shareUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(Strings.SharingAttachmentLinkTargetInvalidUrl);
            }

            string[] segments = shareUri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2
                || !string.Equals(segments[segments.Length - 2], "s", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(segments[segments.Length - 1]))
            {
                throw new InvalidOperationException(Strings.SharingAttachmentLinkTargetInvalidUrl);
            }

            string urlToken = Uri.UnescapeDataString(segments[segments.Length - 1]);
            if (!string.IsNullOrEmpty(token)
                && !string.Equals(urlToken, token, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(Strings.SharingAttachmentLinkTargetInvalidUrl);
            }

            return BuildAbsoluteUrlFromPath(shareUri, shareUri.AbsolutePath.TrimEnd('/') + "/download");
        }

        private static string BuildAbsoluteUrlFromPath(Uri uri, string absolutePath)
        {
            string path = absolutePath ?? string.Empty;
            if (!path.StartsWith("/", StringComparison.Ordinal))
            {
                path = "/" + path;
            }
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + path;
        }

                // Renders the permissions badges (checkmarks / red crosses).
        private static string BuildPermissions(FileLinkPermissionFlags permissions, string readLabel, string createLabel, string writeLabel, string deleteLabel)
        {
            var builder = new StringBuilder();
            builder.Append("<table role=\"presentation\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse: collapse; width: auto; margin: 0; font-family: Calibri, 'Segoe UI', Arial, sans-serif; font-size: 11pt;\">");
            builder.Append("<tbody><tr>");
            AppendPermissionCell(builder, readLabel, (permissions & FileLinkPermissionFlags.Read) == FileLinkPermissionFlags.Read, false);
            AppendPermissionCell(builder, createLabel, (permissions & FileLinkPermissionFlags.Create) == FileLinkPermissionFlags.Create, false);
            AppendPermissionCell(builder, writeLabel, (permissions & FileLinkPermissionFlags.Write) == FileLinkPermissionFlags.Write, false);
            AppendPermissionCell(builder, deleteLabel, (permissions & FileLinkPermissionFlags.Delete) == FileLinkPermissionFlags.Delete, true);
            builder.Append("</tr></tbody>");
            builder.Append("</table>");
            return builder.ToString();
        }

                // Builds the cell for a single permission.
        private static void AppendPermissionCell(StringBuilder builder, string label, bool enabled, bool isLast)
        {
            string color = enabled ? BrandingAssets.BrandBlueHex : "#c62828";
            string padding = isLast ? "0" : "0 12px 0 0";
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<td nowrap=\"nowrap\" valign=\"middle\" style=\"padding: {0}; white-space: nowrap; vertical-align: middle; font-family: Calibri, 'Segoe UI', Arial, sans-serif; font-size: 11pt;\">",
                padding);
            builder.Append("<table role=\"presentation\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse: collapse; width: auto; margin: 0;\"><tbody><tr>");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<td width=\"14\" height=\"14\" valign=\"middle\" style=\"width: 14px; height: 14px; padding: 0; vertical-align: middle;\"><table role=\"presentation\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" width=\"14\" height=\"14\" style=\"border-collapse: collapse; width: 14px; height: 14px; margin: 0;\"><tbody><tr><td width=\"14\" height=\"14\" align=\"center\" valign=\"middle\" style=\"width: 14px; height: 14px; border: 1px solid {0}; color: {0}; font-size: 11px; font-weight: 700; line-height: 14px; padding: 0; text-align: center; vertical-align: middle;\">{1}</td></tr></tbody></table></td>",
                color,
                enabled ? "&#10003;" : "&#10007;");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<td nowrap=\"nowrap\" valign=\"middle\" style=\"padding-left: 5px; white-space: nowrap; font-family: Calibri, 'Segoe UI', Arial, sans-serif; font-size: 11pt; font-weight: 600; vertical-align: middle;\">{0}</td>",
                HttpUtility.HtmlEncode(label));
            builder.Append("</tr></tbody></table></td>");
        }

                // Loads the embedded header banner as a Base64 string.
        private static string LoadHeaderBase64()
        {
            const string resource = "NcTalkOutlookAddIn.Resources.header-transparent-164x48.png";
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
