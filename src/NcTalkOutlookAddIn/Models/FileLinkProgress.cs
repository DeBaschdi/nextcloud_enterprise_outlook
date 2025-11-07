/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

namespace NcTalkOutlookAddIn.Models
{
    /**
     * Beschreibt den Upload-Fortschritt über alle Dateien hinweg (für UI-Sammelanzeige).
     */
    internal sealed class FileLinkProgress
    {
        internal FileLinkProgress(long totalBytes, long uploadedBytes, string currentItem)
        {
            TotalBytes = totalBytes;
            UploadedBytes = uploadedBytes;
            CurrentItem = currentItem;
        }

        internal long TotalBytes { get; private set; }

        internal long UploadedBytes { get; private set; }

        internal string CurrentItem { get; private set; }
    }
}
