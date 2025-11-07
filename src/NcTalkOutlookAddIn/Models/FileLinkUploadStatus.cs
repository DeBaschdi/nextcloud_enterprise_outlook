/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

namespace NcTalkOutlookAddIn.Models
{
    /**
     * Statuswerte, die der Upload-Thread mit dem UI-Thread austauscht.
     */
    internal enum FileLinkUploadStatus
    {
        Pending,
        Uploading,
        Completed,
        Failed
    }
}
