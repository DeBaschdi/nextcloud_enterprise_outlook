// Copyright (c) 2025 Bastian Kleinschmidt
// Licensed under the GNU Affero General Public License v3.0.
// See LICENSE.txt for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NcTalkOutlookAddIn.Models;
using NcTalkOutlookAddIn.Utilities;

namespace NcTalkOutlookAddIn.Services
{
    internal sealed class FileLinkSelectionScanResult
    {
        internal FileLinkSelectionScanResult(
            IList<FileLinkPlannedFile> files,
            ISet<string> directories,
            IDictionary<FileLinkSelection, long> selectionBytes,
            IDictionary<FileLinkSelection, int> selectionFileCounts,
            long totalBytes)
        {
            Files = files;
            Directories = directories;
            SelectionBytes = selectionBytes;
            SelectionFileCounts = selectionFileCounts;
            TotalBytes = totalBytes;
        }

        internal IList<FileLinkPlannedFile> Files { get; private set; }

        internal ISet<string> Directories { get; private set; }

        internal IDictionary<FileLinkSelection, long> SelectionBytes
        {
            get;
            private set;
        }

        internal IDictionary<FileLinkSelection, int> SelectionFileCounts
        {
            get;
            private set;
        }

        internal long TotalBytes { get; private set; }
    }

    internal sealed class FileLinkSelectionScanner
    {
        private readonly Func<FileLinkDuplicateInfo, string>
            _duplicateResolver;
        private readonly CancellationToken _cancellationToken;
        private readonly List<FileLinkPlannedFile> _files =
            new List<FileLinkPlannedFile>();
        private readonly HashSet<string> _directories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reservedFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reservedFolders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<FileLinkSelection, long>
            _selectionBytes =
                new Dictionary<FileLinkSelection, long>();
        private readonly Dictionary<FileLinkSelection, int>
            _selectionFileCounts =
                new Dictionary<FileLinkSelection, int>();
        private long _totalBytes;

        private FileLinkSelectionScanner(
            Func<FileLinkDuplicateInfo, string> duplicateResolver,
            CancellationToken cancellationToken)
        {
            _duplicateResolver = duplicateResolver;
            _cancellationToken = cancellationToken;
        }

        internal static FileLinkSelectionScanResult Scan(
            IList<FileLinkSelection> selections,
            Func<FileLinkDuplicateInfo, string> duplicateResolver,
            CancellationToken cancellationToken)
        {
            if (selections == null)
            {
                throw new ArgumentNullException("selections");
            }

            return new FileLinkSelectionScanner(
                duplicateResolver,
                cancellationToken)
                .ScanSelections(selections);
        }

        private FileLinkSelectionScanResult ScanSelections(
            IEnumerable<FileLinkSelection> selections)
        {
            foreach (FileLinkSelection selection in selections)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (selection == null)
                {
                    continue;
                }

                _selectionBytes[selection] = 0;
                _selectionFileCounts[selection] = 0;

                if (selection.Source
                    == FileLinkSelectionSource.Nextcloud)
                {
                    AddNextcloudSelection(selection);
                    continue;
                }

                if (selection.SelectionType
                    == FileLinkSelectionType.File)
                {
                    AddSingleFile(selection);
                }
                else
                {
                    AddDirectory(selection);
                }
            }

            return new FileLinkSelectionScanResult(
                _files,
                _directories,
                _selectionBytes,
                _selectionFileCounts,
                _totalBytes);
        }

        private void AddNextcloudSelection(
            FileLinkSelection selection)
        {
            if (selection.NextcloudEntries == null
                || selection.NextcloudEntries.Count == 0)
            {
                throw CreateSourceChangedException();
            }

            if (selection.SelectionType == FileLinkSelectionType.File)
            {
                NextcloudStorageEntry entry = selection.NextcloudEntries
                    .FirstOrDefault(
                        item => item != null
                                && !item.IsDirectory
                                && string.Equals(
                                    item.RelativePath,
                                    selection.NextcloudPath,
                                    StringComparison.Ordinal));
                if (entry == null)
                {
                    throw CreateSourceChangedException();
                }

                string fileName = FileLinkPath.SanitizeComponent(
                    entry.DisplayName);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw CreateSourceChangedException();
                }
                AddPlannedNextcloudFile(
                    selection,
                    entry,
                    ReserveUniqueName(
                        string.Empty,
                        fileName,
                        selection,
                        false));
                return;
            }

            AddNextcloudDirectory(selection);
        }

        private void AddNextcloudDirectory(
            FileLinkSelection selection)
        {
            NextcloudStorageEntry root = selection.NextcloudEntries
                .FirstOrDefault(
                    item => item != null
                            && item.IsDirectory
                            && string.Equals(
                                item.RelativePath,
                                selection.NextcloudPath,
                                StringComparison.Ordinal));
            if (root == null)
            {
                throw CreateSourceChangedException();
            }

            string rootName = FileLinkPath.SanitizeComponent(
                selection.DisplayName);
            if (string.IsNullOrWhiteSpace(rootName))
            {
                rootName = "Nextcloud";
            }
            string remoteRoot = ReserveUniqueName(
                string.Empty,
                rootName,
                selection,
                true);
            _directories.Add(remoteRoot);

            var directoryTargets = new Dictionary<string, string>(
                StringComparer.Ordinal);
            directoryTargets[root.RelativePath] = remoteRoot;
            IEnumerable<NextcloudStorageEntry> descendants =
                selection.NextcloudEntries
                    .Where(
                        item => item != null
                                && !string.Equals(
                                    item.RelativePath,
                                    root.RelativePath,
                                    StringComparison.Ordinal))
                    .OrderBy(
                        item => NextcloudPath.GetDepth(
                            item.RelativePath))
                    .ThenBy(item => item.IsDirectory ? 0 : 1)
                    .ThenBy(
                        item => item.RelativePath,
                        StringComparer.Ordinal);
            foreach (NextcloudStorageEntry entry in descendants)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!IsDescendantPath(
                    root.RelativePath,
                    entry.RelativePath))
                {
                    throw CreateSourceChangedException();
                }

                string sourceParent = NextcloudPath.GetParent(
                    entry.RelativePath);
                string targetParent;
                if (!directoryTargets.TryGetValue(
                    sourceParent,
                    out targetParent))
                {
                    throw CreateSourceChangedException();
                }
                string name = FileLinkPath.SanitizeComponent(
                    entry.DisplayName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = entry.IsDirectory ? "Folder" : "File";
                }
                string uniqueName = ReserveUniqueName(
                    targetParent,
                    name,
                    selection,
                    entry.IsDirectory);
                string targetPath = FileLinkPath.Combine(
                    targetParent,
                    uniqueName);
                if (entry.IsDirectory)
                {
                    directoryTargets[entry.RelativePath] = targetPath;
                    _directories.Add(targetPath);
                }
                else
                {
                    AddPlannedNextcloudFile(
                        selection,
                        entry,
                        targetPath);
                }
            }
        }

        private void AddPlannedNextcloudFile(
            FileLinkSelection selection,
            NextcloudStorageEntry entry,
            string remotePath)
        {
            long length = Math.Max(0, entry.Length);
            _totalBytes = checked(_totalBytes + length);
            _selectionBytes[selection] = checked(
                _selectionBytes[selection] + length);
            _selectionFileCounts[selection] = checked(
                _selectionFileCounts[selection] + 1);
            _files.Add(new FileLinkPlannedFile(
                selection,
                entry.RelativePath,
                remotePath,
                length,
                entry.LastModifiedUtc ?? DateTime.MinValue,
                FileLinkUploadTransport.ServerCopy));
        }

        private static bool IsDescendantPath(
            string parent,
            string candidate)
        {
            string normalizedParent = (parent ?? string.Empty)
                .Trim('/');
            string normalizedCandidate = (candidate ?? string.Empty)
                .Trim('/');
            if (normalizedParent.Length == 0)
            {
                return normalizedCandidate.Length > 0;
            }
            return normalizedCandidate.StartsWith(
                normalizedParent + "/",
                StringComparison.Ordinal);
        }

        private void AddSingleFile(FileLinkSelection selection)
        {
            var fileInfo = new FileInfo(selection.LocalPath);
            if (!fileInfo.Exists)
            {
                throw CreateSourceChangedException();
            }
            RejectReparsePoint(fileInfo);

            string fileName = FileLinkPath.SanitizeComponent(
                fileInfo.Name);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw CreateSourceChangedException();
            }
            string uniqueName = ReserveUniqueName(
                string.Empty,
                fileName,
                selection,
                false);
            AddPlannedFile(
                selection,
                fileInfo,
                uniqueName);
        }

        private void AddDirectory(FileLinkSelection selection)
        {
            var rootInfo = new DirectoryInfo(selection.LocalPath);
            if (!rootInfo.Exists)
            {
                throw CreateSourceChangedException();
            }
            RejectReparsePoint(rootInfo);

            string rootName = FileLinkPath.SanitizeComponent(
                rootInfo.Name);
            if (string.IsNullOrWhiteSpace(rootName))
            {
                rootName = "Folder";
            }
            string remoteRoot = ReserveUniqueName(
                string.Empty,
                rootName,
                selection,
                true);

            var pending = new Stack<DirectoryScanEntry>();
            pending.Push(new DirectoryScanEntry(
                rootInfo,
                remoteRoot));

            while (pending.Count > 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                DirectoryScanEntry current = pending.Pop();
                _directories.Add(current.RemotePath);

                FileSystemInfo[] children = current.LocalDirectory
                    .GetFileSystemInfos()
                    .OrderBy(
                        item => item.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var childDirectoryEntries =
                    new List<DirectoryScanEntry>();
                foreach (DirectoryInfo child
                    in children.OfType<DirectoryInfo>())
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    RejectReparsePoint(child);
                    string childName = FileLinkPath.SanitizeComponent(
                        child.Name);
                    if (string.IsNullOrWhiteSpace(childName))
                    {
                        childName = "Folder";
                    }
                    string uniqueChildName = ReserveUniqueName(
                        current.RemotePath,
                        childName,
                        selection,
                        true);
                    childDirectoryEntries.Add(
                        new DirectoryScanEntry(
                            child,
                            FileLinkPath.Combine(
                                current.RemotePath,
                                uniqueChildName)));
                }
                for (int index = childDirectoryEntries.Count - 1;
                    index >= 0;
                    index--)
                {
                    pending.Push(childDirectoryEntries[index]);
                }

                foreach (FileInfo childFile
                    in children.OfType<FileInfo>())
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    RejectReparsePoint(childFile);
                    string fileName = FileLinkPath.SanitizeComponent(
                        childFile.Name);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        fileName = "File";
                    }
                    string uniqueFileName = ReserveUniqueName(
                        current.RemotePath,
                        fileName,
                        selection,
                        false);
                    AddPlannedFile(
                        selection,
                        childFile,
                        FileLinkPath.Combine(
                            current.RemotePath,
                            uniqueFileName));
                }
            }
        }

        private void AddPlannedFile(
            FileLinkSelection selection,
            FileInfo fileInfo,
            string remotePath)
        {
            fileInfo.Refresh();
            if (!fileInfo.Exists)
            {
                throw CreateSourceChangedException();
            }

            long length = Math.Max(0, fileInfo.Length);
            _totalBytes = checked(_totalBytes + length);
            _selectionBytes[selection] = checked(
                _selectionBytes[selection] + length);
            _selectionFileCounts[selection] = checked(
                _selectionFileCounts[selection] + 1);
            _files.Add(new FileLinkPlannedFile(
                selection,
                fileInfo.FullName,
                remotePath,
                length,
                fileInfo.LastWriteTimeUtc));
        }

        private static void RejectReparsePoint(
            FileSystemInfo item)
        {
            if (item != null
                && (item.Attributes & FileAttributes.ReparsePoint)
                == FileAttributes.ReparsePoint)
            {
                throw new TalkServiceException(
                    Strings.FileLinkUploadLinkedItemUnsupported,
                    false,
                    0,
                    null);
            }
        }

        private string ReserveUniqueName(
            string remoteFolder,
            string sanitizedName,
            FileLinkSelection selection,
            bool isDirectory)
        {
            string folderKey = remoteFolder ?? string.Empty;
            string fullPath = FileLinkPath.Combine(
                folderKey,
                sanitizedName);
            ISet<string> primaryReserved = isDirectory
                ? _reservedFolders
                : _reservedFiles;
            ISet<string> secondaryReserved = isDirectory
                ? _reservedFiles
                : _reservedFolders;

            while (primaryReserved.Contains(fullPath)
                || secondaryReserved.Contains(fullPath))
            {
                if (_duplicateResolver == null)
                {
                    throw new TalkServiceException(
                        Strings.FileLinkWizardUploadFailed,
                        false,
                        0,
                        null);
                }

                string replacement = _duplicateResolver(
                    new FileLinkDuplicateInfo(
                        selection,
                        remoteFolder,
                        sanitizedName,
                        isDirectory));
                if (string.IsNullOrWhiteSpace(replacement))
                {
                    throw new OperationCanceledException(
                        Strings.FileLinkWizardUploadCancelledMessage);
                }

                _cancellationToken.ThrowIfCancellationRequested();
                sanitizedName = FileLinkPath.SanitizeComponent(
                    replacement);
                if (string.IsNullOrWhiteSpace(sanitizedName))
                {
                    throw CreateSourceChangedException();
                }
                fullPath = FileLinkPath.Combine(
                    folderKey,
                    sanitizedName);
            }

            primaryReserved.Add(fullPath);
            return sanitizedName;
        }

        private static TalkServiceException
            CreateSourceChangedException()
        {
            return new TalkServiceException(
                Strings.FileLinkUploadSourceChanged,
                false,
                0,
                null);
        }

        private sealed class DirectoryScanEntry
        {
            internal DirectoryScanEntry(
                DirectoryInfo localDirectory,
                string remotePath)
            {
                LocalDirectory = localDirectory;
                RemotePath = remotePath;
            }

            internal DirectoryInfo LocalDirectory
            {
                get;
                private set;
            }

            internal string RemotePath { get; private set; }
        }
    }
}
