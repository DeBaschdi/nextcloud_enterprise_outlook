/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

namespace NcTalkOutlookAddIn.Models
{
    /**
     * Unterscheidet zwischen Einzeldatei und Ordner (inkl. rekursivem Upload).
     */
    internal enum FileLinkSelectionType
    {
        File,
        Directory
    }

    /**
     * Kapselt Pfad + Typ einer vom Benutzer ausgewaehlten Ressource.
     */
    internal sealed class FileLinkSelection
    {
        internal FileLinkSelection(FileLinkSelectionType type, string localPath)
        {
            SelectionType = type;
            LocalPath = localPath;
        }

        internal FileLinkSelectionType SelectionType { get; private set; }

        internal string LocalPath { get; private set; }
    }
}
