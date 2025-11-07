/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

namespace NcTalkOutlookAddIn.Models
{
    /**
     * Detailfortschritt für ein einzelnes Upload-Item; dient der UI-Anzeige im Wizard.
     */
    internal sealed class FileLinkUploadItemProgress
    {
        internal FileLinkUploadItemProgress(
            FileLinkSelection selection,
            long uploadedBytes,
            long totalBytes,
            FileLinkUploadStatus status,
            string message,
            long deltaBytes)
        {
            Selection = selection;
            UploadedBytes = uploadedBytes;
            TotalBytes = totalBytes;
            Status = status;
            Message = message;
            DeltaBytes = deltaBytes;
        }

        internal FileLinkSelection Selection { get; private set; }

        internal long UploadedBytes { get; private set; }

        internal long TotalBytes { get; private set; }

        internal FileLinkUploadStatus Status { get; private set; }

        internal string Message { get; private set; }

        internal long DeltaBytes { get; private set; }
    }
}
