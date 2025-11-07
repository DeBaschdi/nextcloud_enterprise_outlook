/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

using NcTalkOutlookAddIn.Models;

namespace NcTalkOutlookAddIn.Services
{
    /**
     * Beschreibt einen Namenskonflikt waehrend des Uploads und ermoeglicht dem Wizard eine Entscheidung.
     */
    internal sealed class FileLinkDuplicateInfo
    {
        internal FileLinkDuplicateInfo(FileLinkSelection selection, string remoteFolder, string originalName, bool isDirectory)
        {
            Selection = selection;
            RemoteFolder = remoteFolder;
            OriginalName = originalName;
            IsDirectory = isDirectory;
        }

        internal FileLinkSelection Selection { get; private set; }

        internal string RemoteFolder { get; private set; }

        internal string OriginalName { get; private set; }

        internal bool IsDirectory { get; private set; }
    }
}
