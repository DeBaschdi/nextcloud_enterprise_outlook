/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NcTalkOutlookAddIn.Utilities
{
    /**
     * Schreibt interne Diagnosen in eine lokalisierte Logdatei unter %LOCALAPPDATA%.
     */
    internal static class DiagnosticsLogger
    {
        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NextcloudTalkOutlookAddInData");
        private static readonly string LogFilePath = Path.Combine(LogDirectory, "addin-runtime.log");
        private static bool _enabled;

        internal static string LogFileFullPath
        {
            get { return LogFilePath; }
        }

        internal static void SetEnabled(bool enabled)
        {
            lock (SyncRoot)
            {
                _enabled = enabled;
            }
        }

        internal static bool IsEnabled
        {
            get
            {
                lock (SyncRoot)
                {
                    return _enabled;
                }
            }
        }

        internal static void Log(string category, string message)
        {
            if (!IsEnabled)
            {
                return;
            }

            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    string line = string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}",
                        DateTime.UtcNow,
                        string.IsNullOrWhiteSpace(category) ? "GENERAL" : category,
                        message ?? string.Empty);
                    File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging darf keine weiteren Fehler ausloesen.
            }
        }

        internal static void Log(string message)
        {
            Log(null, message);
        }
    }
}
