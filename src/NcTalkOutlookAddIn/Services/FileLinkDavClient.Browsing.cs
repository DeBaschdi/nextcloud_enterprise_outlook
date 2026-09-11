// Copyright (c) 2025 Bastian Kleinschmidt
// Licensed under the GNU Affero General Public License v3.0.
// See LICENSE.txt for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml;
using NcTalkOutlookAddIn.Models;
using NcTalkOutlookAddIn.Utilities;

namespace NcTalkOutlookAddIn.Services
{
    // Reads folders and storage values from the user's DAV file space.
    internal sealed partial class FileLinkDavClient
    {
        private const string DavDirectoryListingXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
            + "<d:propfind xmlns:d=\"DAV:\"><d:prop>"
            + "<d:displayname/><d:resourcetype/>"
            + "<d:getcontentlength/><d:getlastmodified/>"
            + "<d:quota-used-bytes/><d:quota-available-bytes/>"
            + "</d:prop></d:propfind>";

        internal NextcloudStorageListing ListDirectory(
            string baseUrl,
            string userId,
            string relativePath,
            CancellationToken cancellationToken)
        {
            string normalizedPath = NextcloudPath.Normalize(
                relativePath);
            string url = BuildNextcloudSourceUrl(
                baseUrl,
                userId,
                normalizedPath);
            NcHttpResponse response = SendWithRetry(
                () => new NcHttpRequestOptions
                {
                    Method = "PROPFIND",
                    Url = url,
                    Payload = DavDirectoryListingXml,
                    ContentType = "application/xml; charset=utf-8",
                    TimeoutMs = 60000,
                    ReadWriteTimeoutMs = 60000,
                    IncludeAuthHeader = true,
                    IncludeOcsApiHeader = false,
                    ParseJson = false,
                    CancellationToken = cancellationToken,
                    ConnectionLimit =
                        FileLinkUploadPolicy.MaxParallelRequests,
                    Headers = new Dictionary<string, string>
                    {
                        { "Depth", "1" }
                    }
                },
                "nextcloud_folder_list",
                cancellationToken,
                null);

            bool successful = response != null
                              && response.HasHttpResponse
                              && (response.StatusCode == HttpStatusCode.OK
                                  || (int)response.StatusCode == 207);
            if (!successful)
            {
                ThrowFailure(
                    response,
                    Strings.NextcloudPickerLoadFailed,
                    cancellationToken);
            }

            return ParseDirectoryListing(
                baseUrl,
                userId,
                normalizedPath,
                response.ResponseText);
        }

        internal static NextcloudStorageListing ParseDirectoryListing(
            string baseUrl,
            string userId,
            string relativePath,
            string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                throw new TalkServiceException(
                    Strings.NextcloudPickerLoadFailed,
                    false,
                    0,
                    responseText);
            }

            try
            {
                var document = new XmlDocument();
                document.XmlResolver = null;
                document.LoadXml(responseText);

                string normalizedPath = NextcloudPath.Normalize(
                    relativePath);
                string davRootPath = DecodeUriPath(
                    new Uri(
                        BuildNextcloudSourceUrl(
                            baseUrl,
                            userId,
                            string.Empty))
                        .AbsolutePath)
                    .TrimEnd('/');
                var entries = new List<NextcloudStorageEntry>();
                long? usedBytes = null;
                long? availableBytes = null;

                XmlNodeList responseNodes = document.GetElementsByTagName(
                    "response",
                    "DAV:");
                foreach (XmlNode responseNode in responseNodes)
                {
                    string href = ReadDavValue(responseNode, "href");
                    string entryPath = ResolveRelativeDavPath(
                        baseUrl,
                        davRootPath,
                        href);
                    if (entryPath == null)
                    {
                        continue;
                    }

                    if (string.Equals(
                        entryPath,
                        normalizedPath,
                        StringComparison.Ordinal))
                    {
                        usedBytes = ReadNonNegativeLong(
                            responseNode,
                            "quota-used-bytes");
                        availableBytes = ReadNonNegativeLong(
                            responseNode,
                            "quota-available-bytes");
                        continue;
                    }

                    string expectedParent = NextcloudPath.GetParent(
                        entryPath);
                    if (!string.Equals(
                        expectedParent,
                        normalizedPath,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    bool isDirectory = ResponseNodeContainsCollection(
                        responseNode);
                    string displayName = ReadDavValue(
                        responseNode,
                        "displayname");
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = ResolvePathName(entryPath);
                    }
                    long length = isDirectory
                        ? 0
                        : ReadNonNegativeLong(
                              responseNode,
                              "getcontentlength") ?? 0;
                    entries.Add(new NextcloudStorageEntry(
                        entryPath,
                        displayName,
                        isDirectory,
                        length,
                        ReadDavDate(responseNode, "getlastmodified")));
                }

                return new NextcloudStorageListing(
                    normalizedPath,
                    entries
                        .OrderBy(entry => entry.IsDirectory ? 0 : 1)
                        .ThenBy(
                            entry => entry.DisplayName,
                            StringComparer.CurrentCultureIgnoreCase),
                    usedBytes,
                    availableBytes);
            }
            catch (XmlException ex)
            {
                throw new TalkServiceException(
                    Strings.NextcloudPickerLoadFailed
                    + " "
                    + ex.Message,
                    false,
                    0,
                    responseText);
            }
            catch (UriFormatException ex)
            {
                throw new TalkServiceException(
                    Strings.NextcloudPickerLoadFailed
                    + " "
                    + ex.Message,
                    false,
                    0,
                    responseText);
            }
        }

        private static string ResolveRelativeDavPath(
            string baseUrl,
            string decodedDavRootPath,
            string href)
        {
            if (string.IsNullOrWhiteSpace(href))
            {
                return null;
            }

            Uri hrefUri;
            if (!Uri.TryCreate(href, UriKind.Absolute, out hrefUri))
            {
                hrefUri = new Uri(
                    new Uri(baseUrl.TrimEnd('/') + "/"),
                    href);
            }
            string decodedPath = DecodeUriPath(hrefUri.AbsolutePath)
                .TrimEnd('/');
            if (!decodedPath.StartsWith(
                decodedDavRootPath,
                StringComparison.Ordinal))
            {
                return null;
            }
            if (decodedPath.Length > decodedDavRootPath.Length
                && decodedPath[decodedDavRootPath.Length] != '/')
            {
                return null;
            }

            return decodedPath
                .Substring(decodedDavRootPath.Length)
                .Trim('/');
        }

        private static string DecodeUriPath(string value)
        {
            return Uri.UnescapeDataString(value ?? string.Empty)
                .Replace('\\', '/');
        }

        private static string ResolvePathName(string path)
        {
            string normalized = (path ?? string.Empty).Trim('/');
            int separator = normalized.LastIndexOf('/');
            return separator >= 0
                ? normalized.Substring(separator + 1)
                : normalized;
        }

        private static string ReadDavValue(
            XmlNode scope,
            string localName)
        {
            XmlElement element = scope as XmlElement;
            if (element == null)
            {
                return string.Empty;
            }
            XmlNodeList nodes = element.GetElementsByTagName(
                localName,
                "DAV:");
            return nodes != null && nodes.Count > 0
                ? nodes[0].InnerText ?? string.Empty
                : string.Empty;
        }

        private static bool ResponseNodeContainsCollection(XmlNode scope)
        {
            XmlElement element = scope as XmlElement;
            return element != null
                   && element.GetElementsByTagName(
                       "collection",
                       "DAV:").Count > 0;
        }

        private static long? ReadNonNegativeLong(
            XmlNode scope,
            string localName)
        {
            long value;
            return long.TryParse(
                       ReadDavValue(scope, localName),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value)
                   && value >= 0
                ? (long?)value
                : null;
        }

        private static DateTime? ReadDavDate(
            XmlNode scope,
            string localName)
        {
            DateTimeOffset value;
            if (!DateTimeOffset.TryParse(
                ReadDavValue(scope, localName),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces
                | DateTimeStyles.AssumeUniversal,
                out value))
            {
                return null;
            }
            return value.UtcDateTime;
        }
    }
}
