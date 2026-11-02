using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.App
{
    [Serializable] public sealed class DeviceAuthRecord
    {
        public int schema = 1;
        public string accountId, keyId, accessToken, accessExpiresAt, refreshToken, refreshExpiresAt, purgeAt;
        public bool creationPending, deletionPending, restored;
    }
    public sealed class DeviceAuthResponse
    {
        public long Code;
        public string Json;
        public bool TransportFailed;
    }
    public interface IDeviceAuthTransport
    {
        IEnumerator Post(string path, string body, string bearer, Action<DeviceAuthResponse> receive);
    }
    public sealed class DeviceAuthHttp : IDeviceAuthTransport
    {
        readonly string endpoint;
        public DeviceAuthHttp(string value, bool allowLoopback)
        {
            if (!ValidEndpoint(value, allowLoopback)) throw new ArgumentException("Invalid account endpoint.", nameof(value));
            endpoint = value.TrimEnd('/');
        }
        public static bool ValidEndpoint(string value, bool allowLoopback) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == "https" || allowLoopback && uri.Scheme == "http" && uri.IsLoopback)
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/";
        public IEnumerator Post(string path, string body, string bearer, Action<DeviceAuthResponse> receive)
        {
            using var request = new UnityWebRequest(endpoint + path, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 15; request.redirectLimit = 0;
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);
            yield return request.SendWebRequest();
            receive(new DeviceAuthResponse { Code = request.responseCode, Json = request.downloadHandler.text,
                TransportFailed = request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError });
        }
    }
    public sealed class DeviceAuthClient
    {
        [Serializable] sealed class PublicKeyRequest { public string publicKey; }
        [Serializable] sealed class ChallengeRequest { public string keyId; }
        [Serializable] sealed class ChallengeReply { public string keyId, nonce, expiresAt; }
        [Serializable] sealed class SignRequest { public string keyId, nonce, signature; }
        [Serializable] sealed class DeleteReply { public string purgeAt; }
        readonly IDeviceIdentityVault vault;
        readonly IDeviceAuthTransport transport;
        DeviceAuthRecord record;
        public bool Busy { get; private set; }
        public string Status { get; private set; } = "not-connected";
        public bool DeletionPending => record?.deletionPending == true;
        public bool CreationPending => record?.creationPending == true;
        public string PurgeAt => record?.purgeAt;
        public string AccountId => record?.accountId;
        public bool HasDeviceAccount => !string.IsNullOrEmpty(record?.keyId);
        public bool Restored => record?.restored == true;
        public string AccessToken => !DeletionPending && ValidTime(record?.accessExpiresAt, out var expires) && expires > DateTimeOffset.UtcNow
            ? record.accessToken : null;
        public bool Connected => !string.IsNullOrEmpty(AccessToken);

        public DeviceAuthClient(IDeviceIdentityVault storage, IDeviceAuthTransport requests)
        {
            vault = storage ?? throw new ArgumentNullException(nameof(storage)); transport = requests ?? throw new ArgumentNullException(nameof(requests));
            try
            {
                var json = vault.Load(); record = string.IsNullOrEmpty(json) ? new DeviceAuthRecord() : JsonUtility.FromJson<DeviceAuthRecord>(json);
                if (record == null || record.schema != 1
                    || (!string.IsNullOrEmpty(record.keyId) && (!Guid.TryParse(record.keyId, out _) || !Guid.TryParse(record.accountId, out _)))
                    || (!string.IsNullOrEmpty(record.purgeAt) && !ValidTime(record.purgeAt, out _))
                    || (!string.IsNullOrEmpty(record.accessToken) && (!HexToken(record.accessToken) || !HasDeviceAccount)))
                    throw new FormatException();
                Status = DeletionPending ? "deletion-pending" : CreationPending ? "creation-unknown" : Connected ? "connected" : "not-connected";
            }
            catch (Exception) { record = null; Status = "secure-storage-unavailable"; }
        }
        public IEnumerator CreateGuest()
        {
            if (Busy || record == null || HasDeviceAccount || CreationPending || DeletionPending) yield break;
            Busy = true;
            try
            {
                string body = null;
                try
                {
                    body = JsonUtility.ToJson(new PublicKeyRequest { publicKey = Convert.ToBase64String(vault.PublicKeySpki()) });
                    Persist(new DeviceAuthRecord { creationPending = true });
                }
                catch (Exception) { Status = "secure-storage-unavailable"; }
                if (body == null || Status == "secure-storage-unavailable") yield break;
                DeviceAuthResponse reply = null; Status = "connecting";
                yield return transport.Post("/v1/auth/guest", body, null, value => reply = value);
                if (IsSuccess(reply)) AcceptTokens(reply.Json);
                else
                {
                    Status = reply?.Code == 403 ? "guest-disabled" : "creation-unknown";
                    // A lost guest response cannot be retried safely: stage 1 has no public-key lookup/idempotency key.
                    if (reply != null && !reply.TransportFailed && (reply.Code == 400 || reply.Code == 403 || reply.Code == 429))
                    { try { Persist(new DeviceAuthRecord()); } catch (Exception) { Status = "secure-storage-unavailable"; } }
                }
            }
            finally { Busy = false; }
        }
        public IEnumerator SignIn(bool restoreExplicitly)
        {
            if (Busy || record == null || !HasDeviceAccount || DeletionPending && !restoreExplicitly) yield break;
            Busy = true;
            try
            {
                DeviceAuthResponse reply = null; Status = "connecting";
                yield return transport.Post("/v1/auth/challenge", JsonUtility.ToJson(new ChallengeRequest { keyId = record.keyId }), null, value => reply = value);
                if (!IsSuccess(reply)) { Status = "sign-in-failed"; yield break; }
                string body = null;
                try
                {
                    var challenge = JsonUtility.FromJson<ChallengeReply>(reply.Json);
                    if (challenge == null || challenge.keyId != record.keyId || !ValidTime(challenge.expiresAt, out var expiry) || expiry <= DateTimeOffset.UtcNow)
                        throw new FormatException();
                    body = JsonUtility.ToJson(new SignRequest { keyId = record.keyId, nonce = challenge.nonce,
                        signature = Convert.ToBase64String(vault.Sign(DeviceSignature.Message(record.keyId, challenge.nonce))) });
                }
                catch (Exception) { Status = "invalid-challenge-or-key"; }
                if (body == null) yield break;
                reply = null;
                yield return transport.Post("/v1/auth/device", body, null, value => reply = value);
                if (IsSuccess(reply)) AcceptTokens(reply.Json);
                else Status = "sign-in-failed";
            }
            finally { Busy = false; }
        }
        public IEnumerator DeleteConfirmed()
        {
            if (Busy || !Connected || DeletionPending) yield break;
            Busy = true; string bearer = AccessToken;
            try
            {
                try
                {
                    // Commit the tombstone before sending: a crash/lost reply must never cause automatic restoration.
                    Persist(new DeviceAuthRecord { accountId = record.accountId, keyId = record.keyId, deletionPending = true });
                    Status = "deleting";
                }
                catch (Exception) { Status = "secure-storage-unavailable"; }
                if (Status != "deleting") yield break;
                DeviceAuthResponse reply = null;
                yield return transport.Post("/v1/account/delete", "{\"confirm\":\"delete\"}", bearer, value => reply = value);
                Status = "deletion-unknown";
                if (!IsSuccess(reply)) yield break;
                try
                {
                    var deleted = JsonUtility.FromJson<DeleteReply>(reply.Json);
                    if (deleted == null || !ValidTime(deleted.purgeAt, out _)) throw new FormatException();
                    Persist(new DeviceAuthRecord { accountId = record.accountId, keyId = record.keyId, deletionPending = true, purgeAt = deleted.purgeAt });
                    Status = "deletion-pending";
                }
                catch (Exception) { Status = "deletion-unknown"; }
            }
            finally { Busy = false; }
        }
        void AcceptTokens(string json)
        {
            try
            {
                var next = JsonUtility.FromJson<DeviceAuthRecord>(json);
                if (next == null || !Guid.TryParse(next.accountId, out _) || !Guid.TryParse(next.keyId, out _) || !HexToken(next.accessToken)
                    || !HexToken(next.refreshToken) || !ValidTime(next.accessExpiresAt, out var access) || access <= DateTimeOffset.UtcNow
                    || !ValidTime(next.refreshExpiresAt, out var refresh) || refresh <= access
                    || HasDeviceAccount && (next.keyId != record.keyId || next.accountId != record.accountId)) throw new FormatException();
                next.schema = 1; next.creationPending = false; next.deletionPending = false; next.purgeAt = null;
                Persist(next); Status = next.restored ? "restored" : "connected";
            }
            catch (Exception) { Status = "invalid-response-or-storage"; }
        }
        void Persist(DeviceAuthRecord next) { vault.Save(JsonUtility.ToJson(next)); record = next; }
        static bool IsSuccess(DeviceAuthResponse value) => value != null && !value.TransportFailed && value.Code == 200;
        static bool ValidTime(string value, out DateTimeOffset result) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
        static bool HexToken(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++) if (!Uri.IsHexDigit(value[i])) return false;
            return true;
        }
    }
}
