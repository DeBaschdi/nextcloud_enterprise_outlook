// Copyright (c) 2025 Bastian Kleinschmidt
// Licensed under the GNU Affero General Public License v3.0.
// See LICENSE.txt for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NcTalkOutlookAddIn.Utilities;

namespace NcTalkOutlookAddIn.Models
{
    internal sealed class NextcloudStorageEntry
    {
        internal NextcloudStorageEntry(
            string relativePath,
            string displayName,
            bool isDirectory,
            long length,
            DateTime? lastModifiedUtc)
        {
            RelativePath = NextcloudPath.Normalize(relativePath);
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? ResolveName(RelativePath)
                : displayName.Trim();
            IsDirectory = isDirectory;
            Length = isDirectory ? 0 : Math.Max(0, length);
            LastModifiedUtc = lastModifiedUtc;
        }

        internal string RelativePath { get; private set; }

        internal string DisplayName { get; private set; }

        internal bool IsDirectory { get; private set; }

        internal long Length { get; private set; }

        internal DateTime? LastModifiedUtc { get; private set; }

        private static string ResolveName(string path)
        {
            string name = NextcloudPath.GetName(path);
            return name.Length == 0 ? "Nextcloud" : name;
        }
    }

    internal sealed class NextcloudStorageListing
    {
        internal NextcloudStorageListing(
            string relativePath,
            IEnumerable<NextcloudStorageEntry> entries,
            long? usedBytes,
            long? availableBytes)
        {
            RelativePath = NextcloudPath.Normalize(relativePath);
            Entries = new ReadOnlyCollection<NextcloudStorageEntry>(
                new List<NextcloudStorageEntry>(
                    entries
                    ?? Enumerable.Empty<NextcloudStorageEntry>()));
            UsedBytes = usedBytes;
            AvailableBytes = availableBytes;
        }

        internal string RelativePath { get; private set; }

        internal ReadOnlyCollection<NextcloudStorageEntry> Entries
        {
            get;
            private set;
        }

        internal long? UsedBytes { get; private set; }

        internal long? AvailableBytes { get; private set; }
    }
}
