/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

using System;

namespace NcTalkOutlookAddIn.Services
{
    /**
     * Kapselt die fuer die Nextcloud Talk REST-Aufrufe benoetigten Verbindungsdaten.
     */
    internal sealed class TalkServiceConfiguration
    {
        internal string BaseUrl { get; private set; }
        internal string Username { get; private set; }
        internal string AppPassword { get; private set; }

        public TalkServiceConfiguration(string baseUrl, string username, string appPassword)
        {
            BaseUrl = baseUrl ?? string.Empty;
            Username = username ?? string.Empty;
            AppPassword = appPassword ?? string.Empty;
        }

        /**
         * Entfernt ueberfluessige Slashes und liefert eine kanonische Basis-URL zurueck.
         */
        public string GetNormalizedBaseUrl()
        {
            var trimmed = (BaseUrl ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "https://" + trimmed;
            }

            while (trimmed.EndsWith("/", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return trimmed;
        }

        public bool IsComplete()
        {
            return !string.IsNullOrWhiteSpace(BaseUrl)
                   && !string.IsNullOrWhiteSpace(Username)
                   && !string.IsNullOrWhiteSpace(AppPassword);
        }
    }
}
