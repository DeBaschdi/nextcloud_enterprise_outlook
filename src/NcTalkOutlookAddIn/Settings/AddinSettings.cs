/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

namespace NcTalkOutlookAddIn.Settings
{
    /**
     * Persistente Einstellungen des Add-ins (Anmeldedaten, Filelink/IFB-Optionen etc.).
     */
    internal class AddinSettings
    {
        public AddinSettings()
        {
            ServerUrl = string.Empty;
            Username = string.Empty;
            AppPassword = string.Empty;
            AuthMode = AuthenticationMode.LoginFlow;
            OutlookMuzzleEnabled = true;
            IfbEnabled = false;
            IfbDays = 30;
            IfbCacheHours = 24;
            IfbPreviousFreeBusyPath = string.Empty;
            DebugLoggingEnabled = false;
            LastKnownServerVersion = string.Empty;
            FileLinkBasePath = "90 Freigaben - extern";
        }

        public string ServerUrl { get; set; }

        public string Username { get; set; }

        public string AppPassword { get; set; }

        public AuthenticationMode AuthMode { get; set; }

        public bool OutlookMuzzleEnabled { get; set; }

        public bool IfbEnabled { get; set; }

        public int IfbDays { get; set; }

        public int IfbCacheHours { get; set; }

        public string IfbPreviousFreeBusyPath { get; set; }

        public bool DebugLoggingEnabled { get; set; }

        public string LastKnownServerVersion { get; set; }

        public string FileLinkBasePath { get; set; }

        public AddinSettings Clone()
        {
            return (AddinSettings)MemberwiseClone();
        }
    }
}
