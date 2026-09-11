// Copyright (c) 2025 Bastian Kleinschmidt
// Licensed under the GNU Affero General Public License v3.0.
// See LICENSE.txt for details.

using System;
using System.Linq;

namespace NcTalkOutlookAddIn.Utilities
{
    // Handles paths that belong to Nextcloud and are not limited by Windows file-name rules.
    internal static class NextcloudPath
    {
        internal static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string[] parts = path.Split(
                new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(
                part => string.Equals(part, ".", StringComparison.Ordinal)
                        || string.Equals(
                            part,
                            "..",
                            StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "Relative Nextcloud paths cannot contain dot segments.",
                    "path");
            }
            return string.Join("/", parts);
        }

        internal static string GetParent(string path)
        {
            string normalized = Normalize(path);
            int separator = normalized.LastIndexOf('/');
            return separator < 0
                ? string.Empty
                : normalized.Substring(0, separator);
        }

        internal static string GetName(string path)
        {
            string normalized = Normalize(path);
            int separator = normalized.LastIndexOf('/');
            return separator < 0
                ? normalized
                : normalized.Substring(separator + 1);
        }

        internal static int GetDepth(string path)
        {
            string normalized = Normalize(path);
            return normalized.Length == 0
                ? 0
                : normalized.Count(character => character == '/') + 1;
        }
    }
}
