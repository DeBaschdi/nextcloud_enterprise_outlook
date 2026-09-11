// Copyright (c) 2025 Bastian Kleinschmidt
// Licensed under the GNU Affero General Public License v3.0.
// See LICENSE.txt for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using NcTalkOutlookAddIn.Utilities;

namespace NcTalkOutlookAddIn.Models
{
    // Distinguishes between a single file and a directory (including recursive upload).
    internal enum FileLinkSelectionType
    {
        File,
        Directory
    }

    internal enum FileLinkSelectionSource
    {
        Local,
        Nextcloud
    }

    // Describes one item selected for a share, regardless of its source.
    internal sealed class FileLinkSelection
    {
        internal FileLinkSelection(FileLinkSelectionType type, string localPath)
        {
            SelectionType = type;
            LocalPath = localPath;
            Source = FileLinkSelectionSource.Local;
            DisplayName = ResolveLocalDisplayName(localPath);
            NextcloudEntries = new ReadOnlyCollection<NextcloudStorageEntry>(
                new List<NextcloudStorageEntry>());
        }

        private FileLinkSelection(
            FileLinkSelectionType type,
            string relativePath,
            string displayName,
            IEnumerable<NextcloudStorageEntry> entries)
        {
            SelectionType = type;
            Source = FileLinkSelectionSource.Nextcloud;
            NextcloudPath = NormalizeNextcloudPath(relativePath);
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? ResolveNextcloudDisplayName(NextcloudPath)
                : displayName.Trim();
            NextcloudEntries = new ReadOnlyCollection<NextcloudStorageEntry>(
                new List<NextcloudStorageEntry>(
                    entries
                    ?? Enumerable.Empty<NextcloudStorageEntry>()));
        }

        internal static FileLinkSelection FromNextcloudFile(
            NextcloudStorageEntry entry)
        {
            if (entry == null || entry.IsDirectory)
            {
                throw new ArgumentException(
                    "A Nextcloud file is required.",
                    "entry");
            }

            return new FileLinkSelection(
                FileLinkSelectionType.File,
                entry.RelativePath,
                entry.DisplayName,
                new[] { entry });
        }

        internal static FileLinkSelection FromNextcloudFolder(
            NextcloudStorageEntry root,
            IEnumerable<NextcloudStorageEntry> entries)
        {
            if (root == null || !root.IsDirectory)
            {
                throw new ArgumentException(
                    "A Nextcloud folder is required.",
                    "root");
            }

            var snapshot = new List<NextcloudStorageEntry>();
            snapshot.Add(root);
            if (entries != null)
            {
                snapshot.AddRange(
                    entries.Where(
                        entry => entry != null
                                 && !string.Equals(
                                     entry.RelativePath,
                                     root.RelativePath,
                                     StringComparison.Ordinal)));
            }
            return new FileLinkSelection(
                FileLinkSelectionType.Directory,
                root.RelativePath,
                root.DisplayName,
                snapshot);
        }

        internal FileLinkSelectionType SelectionType { get; private set; }

        internal string LocalPath { get; private set; }

        internal FileLinkSelectionSource Source { get; private set; }

        internal string NextcloudPath { get; private set; }

        internal string DisplayName { get; private set; }

        internal ReadOnlyCollection<NextcloudStorageEntry> NextcloudEntries
        {
            get;
            private set;
        }

        internal string DisplayPath
        {
            get
            {
                return Source == FileLinkSelectionSource.Local
                    ? LocalPath ?? string.Empty
                    : "/" + (NextcloudPath ?? string.Empty);
            }
        }

        internal string IdentityPath
        {
            get
            {
                return Source.ToString()
                       + ":"
                       + (Source == FileLinkSelectionSource.Local
                           ? LocalPath ?? string.Empty
                           : NextcloudPath ?? string.Empty);
            }
        }

        private static string ResolveLocalDisplayName(string path)
        {
            string trimmed = (path ?? string.Empty).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            string name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? trimmed : name;
        }

        private static string ResolveNextcloudDisplayName(string path)
        {
            string normalized =
                NcTalkOutlookAddIn.Utilities.NextcloudPath.Normalize(path);
            if (normalized.Length == 0)
            {
                return "Nextcloud";
            }
            return NcTalkOutlookAddIn.Utilities.NextcloudPath.GetName(
                normalized);
        }

        private static string NormalizeNextcloudPath(string path)
        {
            return NcTalkOutlookAddIn.Utilities.NextcloudPath.Normalize(
                path);
        }
    }
}
