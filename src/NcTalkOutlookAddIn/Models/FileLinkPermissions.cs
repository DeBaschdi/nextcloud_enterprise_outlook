/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

using System;

namespace NcTalkOutlookAddIn.Models
{
    /**
     * Bitmaske der Nextcloud-Share-Berechtigungen, damit UI und Service identische Flags nutzen.
     */
    [Flags]
    internal enum FileLinkPermissionFlags
    {
        None = 0,
        Read = 1,
        Write = 2,
        Create = 4,
        Delete = 8
    }
}
