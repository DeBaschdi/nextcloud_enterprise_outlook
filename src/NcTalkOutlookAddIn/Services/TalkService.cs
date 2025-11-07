/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NcTalkOutlookAddIn.Models;
using NcTalkOutlookAddIn.Utilities;

namespace NcTalkOutlookAddIn.Services
{
    /**
     * Fuehrt die HTTP-Aufrufe gegen die Nextcloud Talk REST-API aus.
     */
    internal sealed class TalkService
    {
        private const int RoomTypePublic = 3;
        private const int ListableNone = 0;
        private const int ListableUsers = 1;

        private readonly TalkServiceConfiguration _configuration;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private static void LogApi(string message)
        {
            DiagnosticsLogger.Log("API", message);
        }

        internal TalkService(TalkServiceConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            _configuration = configuration;
        }

        /**
         * Erstellt einen Talk-Raum entsprechend der Nutzereingaben und liefert Token + Link zurueck.
         */
        internal TalkRoomCreationResult CreateRoom(TalkRoomRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            EnsureConfiguration();

            string baseUrl = _configuration.GetNormalizedBaseUrl();
            string createUrl = baseUrl + "/ocs/v2.php/apps/spreed/api/v4/room";

            bool attemptEventConversation = request.RoomType == TalkRoomType.EventConversation
                                            && request.AppointmentStart.HasValue
                                            && request.AppointmentEnd.HasValue;

            List<bool> attempts = new List<bool>();
            if (attemptEventConversation)
            {
                attempts.Add(true);
            }
            attempts.Add(false);

            Exception lastError = null;

            foreach (bool includeEvent in attempts)
            {
                try
                {
                    IDictionary<string, object> payload = BuildCreatePayload(request, includeEvent);
                    string payloadJson = _serializer.Serialize(payload);

                    IDictionary<string, object> responseData;
                    HttpStatusCode statusCode;
                    string responseText = ExecuteJsonRequest("POST", createUrl, payload, out statusCode, out responseData);

                    if (!IsSuccessStatus(statusCode))
                    {
                        if (includeEvent && ShouldFallbackToStandard(statusCode, responseData))
                        {
                            lastError = null;
                            continue;
                        }

                        ThrowServiceError(statusCode, responseText, responseData);
                    }

                    string token = ExtractRoomToken(responseData);
                    if (string.IsNullOrEmpty(token))
                    {
                        throw new TalkServiceException("Antwort enthaelt kein Raum-Token.", false, statusCode, responseText);
                    }

                    string roomUrl = baseUrl + "/call/" + token;

                    if (request.LobbyEnabled)
                    {
                        TryUpdateLobbyInternal(token, request.AppointmentStart, baseUrl, includeEvent);
                    }

                    if (!includeEvent)
                    {
                        TryUpdateListable(token, request.SearchVisible, baseUrl);
                    }

                    TryUpdateDescription(token, request.Description, includeEvent, baseUrl);

                    return new TalkRoomCreationResult(token, roomUrl, includeEvent, request.LobbyEnabled, request.SearchVisible);
                }
                catch (TalkServiceException ex)
                {
                    lastError = ex;
                    if (includeEvent && !ex.IsAuthenticationError && ShouldFallbackToStandard(ex.StatusCode, null))
                    {
                        continue;
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    throw;
                }
            }

            if (lastError != null)
            {
                throw lastError;
            }

            throw new TalkServiceException("Talk-Raum konnte nicht erstellt werden.", false, HttpStatusCode.InternalServerError, null);
        }

        /**
         * Aktualisiert die Lobby-Zeit eines bestehenden Talk-Raums.
         */
        internal void UpdateLobby(string roomToken, DateTime start, DateTime end, bool isEventConversation)
        {
            EnsureConfiguration();

            if (string.IsNullOrWhiteSpace(roomToken))
            {
                return;
            }

            string baseUrl = _configuration.GetNormalizedBaseUrl();
            if (isEventConversation)
            {
                try
                {
                    TryUpdateEventBinding(roomToken, start, end, baseUrl);
                }
                catch (TalkServiceException ex)
                {
                    if (!IsRecoverableEventBindingStatus(ex.StatusCode))
                    {
                        throw;
                    }
                }
            }

            TryUpdateLobbyInternal(roomToken, start, baseUrl, isEventConversation);
        }

        /**
         * Loescht den Talk-Raum (Teilnehmer self) und ignoriert 404.
         */
        internal void DeleteRoom(string roomToken, bool isEventConversation)
        {
            EnsureConfiguration();

            if (string.IsNullOrWhiteSpace(roomToken))
            {
                return;
            }

            string baseUrl = _configuration.GetNormalizedBaseUrl();
            TryClearActiveParticipants(roomToken, baseUrl);
            if (isEventConversation)
            {
                TryDetachEventBinding(roomToken, baseUrl);
            }

            string url = baseUrl + "/ocs/v2.php/apps/spreed/api/v4/room/" + Uri.EscapeDataString(roomToken.Trim());

            HttpStatusCode statusCode;
            IDictionary<string, object> parsed;
            ExecuteJsonRequest("DELETE", url, (string)null, out statusCode, out parsed);

            if (statusCode != HttpStatusCode.OK &&
                statusCode != HttpStatusCode.NoContent &&
                statusCode != HttpStatusCode.NotFound)
            {
                throw new TalkServiceException("Talk-Raum konnte nicht geloescht werden (Status " + (int)statusCode + ").", false, statusCode, null);
            }
        }

        internal bool VerifyConnection(out string message)
        {
            EnsureConfiguration();

            string baseUrl = _configuration.GetNormalizedBaseUrl();
            string url = baseUrl + "/ocs/v2.php/cloud/capabilities";

            HttpStatusCode statusCode;
            IDictionary<string, object> data;
            ExecuteJsonRequest("GET", url, (string)null, out statusCode, out data);

            if (!IsSuccessStatus(statusCode))
            {
                message = "HTTP " + (int)statusCode;
                return false;
            }

            IDictionary<string, object> ocs = GetDictionary(data, "ocs");
            IDictionary<string, object> meta = GetDictionary(ocs, "meta");
            IDictionary<string, object> payload = GetDictionary(ocs, "data");

            string version = ExtractVersion(payload);
            string status = GetString(meta, "status");
            if (!string.IsNullOrEmpty(version))
            {
                message = version;
            }
            else if (!string.IsNullOrEmpty(status))
            {
                message = status;
            }
            else
            {
                message = "OK";
            }

            return true;
        }

        private IDictionary<string, object> BuildCreatePayload(TalkRoomRequest request, bool includeEvent)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["roomType"] = RoomTypePublic;
            payload["type"] = RoomTypePublic;
            payload["roomName"] = string.IsNullOrWhiteSpace(request.Title) ? "Besprechung" : request.Title.Trim();
            payload["listable"] = request.SearchVisible ? ListableUsers : ListableNone;
            payload["participants"] = new Dictionary<string, object>();

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                payload["password"] = request.Password;
            }

            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                payload["description"] = request.Description.Trim();
            }

            if (includeEvent)
            {
                string objectId = BuildEventObjectId(request);
                if (!string.IsNullOrEmpty(objectId))
                {
                    payload["objectType"] = "event";
                    payload["objectId"] = objectId;
                }
            }

            return payload;
        }

        private void TryUpdateLobbyInternal(string token, DateTime? start, string baseUrl, bool isEventConversation)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            if (isEventConversation)
            {
                if (TrySendLobbyRequest(token, baseUrl, 1, null, true))
                {
                    TrySendLobbyRequest(token, baseUrl, 1, start, true);
                }
            }
            else
            {
                TrySendLobbyRequest(token, baseUrl, 1, start, false);
            }
        }

        private bool TrySendLobbyRequest(string token, string baseUrl, int state, DateTime? start, bool silent)
        {
            long? unixStart = ConvertToUnixTimestamp(start);

            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["state"] = state;
            if (unixStart.HasValue)
            {
                payload["timer"] = unixStart.Value;
            }

            string lobbyUrl = baseUrl + "/ocs/v2.php/apps/spreed/api/v4/room/" + Uri.EscapeDataString(token) + "/webinar/lobby";

            try
            {
                HttpStatusCode statusCode;
                IDictionary<string, object> parsed;
                string payloadJson = _serializer.Serialize(payload);
                ExecuteJsonRequest("PUT", lobbyUrl, payload, out statusCode, out parsed);
                if (!IsSuccessStatus(statusCode) && !silent)
                {
                    throw new TalkServiceException("Lobby-Zeit konnte nicht gesetzt werden (Status " + (int)statusCode + ").", false, statusCode, null);
                }

                return IsSuccessStatus(statusCode);
            }
            catch (TalkServiceException)
            {
                if (!silent)
                {
                    throw;
                }
            }

            return false;
        }

        private void TryUpdateEventBinding(string token, DateTime? start, DateTime? end, string baseUrl)
        {
            string objectId = BuildEventObjectId(start, end);
            if (string.IsNullOrEmpty(objectId))
            {
                return;
            }

            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["objectType"] = "event";
            payload["objectId"] = objectId;

            HttpStatusCode statusCode;
            IDictionary<string, object> parsed;
            string responseText = ExecuteJsonRequest("PUT",
                                                     baseUrl + "/ocs/v2.php/apps/spreed/api/v4/room/" + Uri.EscapeDataString(token) + "/object",
                                                     payload,
                                                     out statusCode,
                                                     out parsed);

            if (!IsSuccessStatus(statusCode))
            {
                ThrowServiceError(statusCode, responseText, parsed);
            }
        }

        private static bool IsRecoverableEventBindingStatus(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.MethodNotAllowed ||
                   statusCode == HttpStatusCode.NotImplemented ||
                   statusCode == HttpStatusCode.BadRequest;
        }

        private void TryClearActiveParticipants(string token, string baseUrl)
        {
            try
            {
                HttpStatusCode statusCode;
                IDictionary<string, object> parsed;
                ExecuteJsonRequest("DELETE",
                                   baseUrl + "/ocs/v2.php/apps/spreed/api/v4/room/" + Uri.EscapeDataString(token) + "/participants/active",
                                   (string)null,
                                   out statusCode,
                                   out parsed);
            }
            catch
            {
                // Ignorieren: Aufraeumvorgang darf nicht abbrechen.
            }
        }

        private void TryDetachEventBinding(string token, string baseUrl)
        {
            try
            {
                HttpStatusCode statusCode;
                IDictionary<string, object> parsed;
                ExecuteJsonRequest("DELETE",
                                   baseUrl + "/ocs/v2.php/apps/spreed/api/v4/room/" + Uri.EscapeDataString(token) + "/object/event",
                                   (string)null,
                                   out statusCode,
                                   out parsed);
            }
            catch
            {
                // Ignorieren: Event-Bindung ist optional, Fehler hier sind unkritisch.
            }
        }

        private void TryUpdateListable(string token, bool searchVisible, string baseUrl)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["scope"] = searchVisible ? ListableUsers : ListableNone;

            HttpStatusCode statusCode;
            IDictionary<string, object> parsed;
            string payloadJson = _serializer.Serialize(payload);
            ExecuteJsonRequest("PUT",
                               baseUrl + "/ocs/v2.php/apps/spreed/api/v4/room/" + Uri.EscapeDataString(token) + "/listable",
                               payload,
                               out statusCode,
                               out parsed);
        }

        internal void UpdateDescription(string token, string description, bool isEventConversation)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            EnsureConfiguration();
            string baseUrl = _configuration.GetNormalizedBaseUrl();
            TryUpdateDescription(token, description, isEventConversation, baseUrl);
        }

        private void TryUpdateDescription(string token, string description, bool isEventConversation, string baseUrl)
        {
            if (isEventConversation)
            {
                return;
            }

            if (description == null)
            {
                return;
            }

            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["description"] = description.Trim();

            HttpStatusCode statusCode;
            IDictionary<string, object> parsed;
            string responseText = ExecuteJsonRequest("PUT",
                                                     baseUrl + "/ocs/v2.php/apps/spreed/api/v4/room/" + Uri.EscapeDataString(token) + "/description",
                                                     payload,
                                                     out statusCode,
                                                     out parsed);

            if (!IsSuccessStatus(statusCode))
            {
                ThrowServiceError(statusCode, responseText, parsed);
            }
        }

        private void EnsureConfiguration()
        {
            if (!_configuration.IsComplete())
            {
                throw new TalkServiceException("Zugangsdaten sind unvollstaendig.", true, HttpStatusCode.BadRequest, null);
            }
        }

        private string ExecuteJsonRequest(string method, string url, object payload, out HttpStatusCode statusCode, out IDictionary<string, object> parsedData)
        {
            string payloadText = null;
            if (payload is string)
            {
                payloadText = (string)payload;
            }
            else if (payload != null)
            {
                payloadText = _serializer.Serialize(payload);
            }

            return SendJsonRequest(method, url, payloadText, out statusCode, out parsedData);
        }
        private string SendJsonRequest(string method, string url, string payload, out HttpStatusCode statusCode, out IDictionary<string, object> parsedData)
        {
            HttpWebResponse response = null;
            string responseText = null;
            parsedData = null;

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = method;
                request.Accept = "application/json";
                request.Headers["OCS-APIRequest"] = "true";
                request.Headers["Authorization"] = "Basic " + EncodeBasicAuth(_configuration.Username, _configuration.AppPassword);
                request.Timeout = 60000;

                bool hasBody = !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                               && !string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase);

                if (hasBody)
                {
                    request.ContentType = "application/json";
                    if (!string.IsNullOrEmpty(payload))
                    {
                        using (StreamWriter writer = new StreamWriter(request.GetRequestStream()))
                        {
                            writer.Write(payload);
                        }
                    }
                    else
                    {
                        request.ContentLength = 0;
                    }
                }

                LogApi(method + " " + url);
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException ex)
                {
                    response = ex.Response as HttpWebResponse;
                    if (response == null)
                    {
                        throw new TalkServiceException("HTTP-Verbindungsfehler: " + ex.Message, false, 0, null);
                    }
                }

                statusCode = response.StatusCode;
                LogApi(method + " " + url + " -> " + statusCode);

                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream ?? Stream.Null))
                {
                    responseText = reader.ReadToEnd();
                }

                if (!string.IsNullOrEmpty(responseText))
                {
                    try
                    {
                        parsedData = _serializer.DeserializeObject(responseText) as IDictionary<string, object>;
                    }
                    catch
                    {
                        parsedData = null;
                    }
                }

                return responseText;
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                }
            }
        }

        private static bool IsSuccessStatus(HttpStatusCode statusCode)
        {
            return statusCode >= HttpStatusCode.OK && statusCode < HttpStatusCode.MultipleChoices;
        }

        private static string EncodeBasicAuth(string username, string password)
        {
            string raw = (username ?? string.Empty) + ":" + (password ?? string.Empty);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }

        private static string ExtractRoomToken(IDictionary<string, object> responseData)
        {
            if (responseData == null)
            {
                return null;
            }

            IDictionary<string, object> ocs = GetDictionary(responseData, "ocs");
            IDictionary<string, object> data = GetDictionary(ocs, "data");
            if (data != null)
            {
                string token = GetString(data, "token");
                if (string.IsNullOrEmpty(token))
                {
                    token = GetString(data, "roomToken");
                }

                if (!string.IsNullOrEmpty(token))
                {
                    return token;
                }
            }

            return GetString(responseData, "token");
        }

        private static Dictionary<string, object> GetDictionary(IDictionary<string, object> parent, string key)
        {
            if (parent == null)
            {
                return null;
            }

            object value;
            if (parent.TryGetValue(key, out value))
            {
                return value as Dictionary<string, object>;
            }

            return null;
        }

        private static string GetString(IDictionary<string, object> parent, string key)
        {
            if (parent == null)
            {
                return null;
            }

            object value;
            if (parent.TryGetValue(key, out value) && value != null)
            {
                if (value is string)
                {
                    return (string)value;
                }

                if (value is IDictionary<string, object>)
                {
                    return null;
                }

                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static string ExtractVersion(IDictionary<string, object> payload)
        {
            if (payload == null)
            {
                return null;
            }

            string version = GetString(payload, "versionstring") ?? GetString(payload, "version");
            string edition = GetString(payload, "edition");
            string result = ComposeVersion(version, edition);
            if (!string.IsNullOrEmpty(result))
            {
                return EnsureProductPrefix(result, payload);
            }

            IDictionary<string, object> versionDict = GetDictionary(payload, "version");
            result = ComposeVersion(
                GetString(versionDict, "string") ?? BuildVersionFromParts(versionDict),
                GetString(versionDict, "edition"));
            if (!string.IsNullOrEmpty(result))
            {
                return EnsureProductPrefix(result, payload);
            }

            IDictionary<string, object> nextcloud = GetDictionary(payload, "nextcloud");
            IDictionary<string, object> system = GetDictionary(nextcloud, "system");
            if (system != null)
            {
                result = ComposeVersion(
                    GetString(system, "versionstring") ?? GetString(system, "version") ?? BuildVersionFromParts(system),
                    GetString(system, "edition"));
                if (!string.IsNullOrEmpty(result))
                {
                    string product = GetString(system, "productname") ?? GetString(nextcloud, "productname");
                    if (!string.IsNullOrEmpty(product))
                    {
                        return product + " " + result;
                    }

                    return EnsureProductPrefix(result, payload);
                }
            }

            return null;
        }

        private static string BuildVersionFromParts(IDictionary<string, object> dictionary)
        {
            if (dictionary == null)
            {
                return null;
            }

            string major = GetString(dictionary, "major");
            string minor = GetString(dictionary, "minor");
            string micro = GetString(dictionary, "micro");

            if (string.IsNullOrEmpty(major))
            {
                return null;
            }

            var builder = new StringBuilder();
            builder.Append(major);
            if (!string.IsNullOrEmpty(minor))
            {
                builder.Append(".").Append(minor);
                if (!string.IsNullOrEmpty(micro))
                {
                    builder.Append(".").Append(micro);
                }
            }

            return builder.ToString();
        }

        private static string ComposeVersion(string version, string edition)
        {
            if (string.IsNullOrEmpty(version))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(edition))
            {
                return edition.Equals(version, StringComparison.OrdinalIgnoreCase)
                    ? version
                    : version + " (" + edition + ")";
            }

            return version;
        }

        private static string EnsureProductPrefix(string version, IDictionary<string, object> payload)
        {
            if (string.IsNullOrEmpty(version))
            {
                return null;
            }

            string prefix = "Nextcloud";
            string product = GetString(payload, "productname");
            if (!string.IsNullOrEmpty(product))
            {
                prefix = product;
            }

            if (version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }

            return prefix + " " + version;
        }

        private static void ThrowServiceError(HttpStatusCode statusCode, string responseText, IDictionary<string, object> responseData)
        {
            IDictionary<string, object> meta = GetDictionary(GetDictionary(responseData, "ocs"), "meta");
            string message = GetString(meta, "message");
            IDictionary<string, object> payload = GetDictionary(GetDictionary(responseData, "ocs"), "data");
            string errorDetail = GetString(payload, "error");

            StringBuilder builder = new StringBuilder();

            if (!string.IsNullOrEmpty(message))
            {
                builder.Append(message);
            }
            if (!string.IsNullOrEmpty(errorDetail))
            {
                if (builder.Length > 0)
                {
                    builder.Append(" / ");
                }
                builder.Append(errorDetail);
            }
            if (builder.Length == 0)
            {
                builder.Append("HTTP ").Append((int)statusCode);
            }

            bool authError = statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden;
            throw new TalkServiceException(builder.ToString(), authError, statusCode, responseText);
        }

        private static bool ShouldFallbackToStandard(HttpStatusCode statusCode, IDictionary<string, object> responseData)
        {
            if (statusCode == HttpStatusCode.Conflict ||
                statusCode == HttpStatusCode.NotImplemented ||
                statusCode == HttpStatusCode.BadRequest ||
                statusCode == (HttpStatusCode)422)
            {
                return true;
            }

            IDictionary<string, object> meta = GetDictionary(GetDictionary(responseData, "ocs"), "meta");
            IDictionary<string, object> payload = GetDictionary(GetDictionary(responseData, "ocs"), "data");
            string message = GetString(meta, "message");
            if (string.IsNullOrEmpty(message))
            {
                message = GetString(payload, "error");
            }

            if (!string.IsNullOrEmpty(message))
            {
                string normalized = message.ToLowerInvariant();
                if (normalized.IndexOf("object", StringComparison.Ordinal) >= 0 &&
                    normalized.IndexOf("event", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildEventObjectId(TalkRoomRequest request)
        {
            if (request == null)
            {
                return null;
            }

            return BuildEventObjectId(request.AppointmentStart, request.AppointmentEnd);
        }

        private static string BuildEventObjectId(DateTime? start, DateTime? end)
        {
            long? startEpoch = ConvertToUnixTimestamp(start);
            long? endEpoch = ConvertToUnixTimestamp(end);

            if (!startEpoch.HasValue || !endEpoch.HasValue)
            {
                return null;
            }

            return startEpoch.Value + "#" + endEpoch.Value;
        }

        private static long? ConvertToUnixTimestamp(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            DateTime date = value.Value;
            if (date.Kind == DateTimeKind.Unspecified)
            {
                date = DateTime.SpecifyKind(date, DateTimeKind.Local);
            }

            DateTimeOffset offset = new DateTimeOffset(date);
            return offset.ToUnixTimeSeconds();
        }
    }
}



