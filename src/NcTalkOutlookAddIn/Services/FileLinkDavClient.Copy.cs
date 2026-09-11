// Copyright (c) 2025 Bastian Kleinschmidt
// Licensed under the GNU Affero General Public License v3.0.
// See LICENSE.txt for details.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using NcTalkOutlookAddIn.Utilities;

namespace NcTalkOutlookAddIn.Services
{
    // Copies files within the same Nextcloud account without downloading them.
    internal sealed partial class FileLinkDavClient
    {
        internal void CopyFile(
            string baseUrl,
            string userId,
            string sourceRelativePath,
            string destinationRelativePath,
            long expectedLength,
            CancellationToken cancellationToken)
        {
            string sourceUrl = BuildNextcloudSourceUrl(
                baseUrl,
                userId,
                sourceRelativePath);
            string destinationUrl = BuildFileUrl(
                baseUrl,
                userId,
                destinationRelativePath);

            if (!ResourceHasContentLength(
                sourceUrl,
                expectedLength,
                Strings.FileLinkUploadSourceChanged,
                cancellationToken))
            {
                throw new TalkServiceException(
                    Strings.FileLinkUploadSourceChanged,
                    false,
                    0,
                    null);
            }

            NcHttpResponse response = SendWithRetry(
                () => new NcHttpRequestOptions
                {
                    Method = "COPY",
                    Url = sourceUrl,
                    ContentType = "application/octet-stream",
                    TimeoutMs = 90000,
                    ReadWriteTimeoutMs = 90000,
                    IncludeAuthHeader = true,
                    IncludeOcsApiHeader = false,
                    ParseJson = false,
                    CancellationToken = cancellationToken,
                    ConnectionLimit =
                        FileLinkUploadPolicy.MaxParallelRequests,
                    Headers = new Dictionary<string, string>
                    {
                        { "Destination", destinationUrl },
                        { "Overwrite", "F" }
                    }
                },
                "nextcloud_file_copy",
                cancellationToken,
                null);

            bool copied = response != null
                          && response.HasHttpResponse
                          && (response.StatusCode == HttpStatusCode.Created
                              || response.StatusCode
                              == HttpStatusCode.NoContent);
            if (copied)
            {
                return;
            }

            bool mayAlreadyExist = response == null
                                   || !response.HasHttpResponse
                                   || response.StatusCode
                                   == HttpStatusCode.PreconditionFailed;
            if (mayAlreadyExist
                && ResourceHasContentLength(
                    destinationUrl,
                    expectedLength,
                    Strings.FileLinkWizardUploadFailed,
                    cancellationToken))
            {
                DiagnosticsLogger.Log(
                    LogCategories.FileLink,
                    "Recovered a Nextcloud file copy after an indeterminate response.");
                return;
            }

            ThrowFailure(
                response,
                Strings.FileLinkWizardUploadFailed,
                cancellationToken);
        }
    }
}
