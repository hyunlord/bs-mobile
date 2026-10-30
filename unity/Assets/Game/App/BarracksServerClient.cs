using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.App
{
    // JSON fields mirror the public server DTOs; no Core state or device-save blob crosses this boundary.
    [Serializable] public sealed class BarracksResource { public string id; public int amount; }
    [Serializable] public sealed class BarracksCost { public string materialId; public int amount; }
    [Serializable] public sealed class BarracksRefundPreview { public string materialId; public int immediateAmount, pendingAmount; }
    [Serializable] public sealed class BarracksQuote
    {
        public int count, occupiedAfter, capacity;
        public BarracksCost[] totalCost;
        public long durationMilliseconds, estimatedCompletesAtMilliseconds;
        public bool canStart;
        public string reason;
    }
    [Serializable] public sealed class BarracksUnit
    {
        public string unitId, name;
        public int requiredBarracksLevel, ready, deployed, wounded, reserved, secondsPerUnit;
        public bool unlocked;
        public BarracksCost[] costPerUnit;
        public BarracksQuote[] quotes;
    }
    [Serializable] public sealed class BarracksJob
    {
        public string jobId, unitId;
        public int count, bonusPermille;
        public long startedAtMilliseconds, completesAtMilliseconds, durationMilliseconds;
        public BarracksCost[] chargedCost;
        public bool Exists => !string.IsNullOrEmpty(jobId);
    }
    [Serializable] public sealed class BarracksView
    {
        public int barracksLevel, capacity, batchLimit, occupied;
        public long serverUtcMilliseconds;
        public BarracksUnit[] units;
        public BarracksJob active, waiting;
        public BarracksCost[] pendingRefunds;
        public BarracksRefundPreview[] waitingCancellationRefunds;
        public bool HasActive => active != null && active.Exists;
        public bool HasWaiting => waiting != null && waiting.Exists;
    }
    [Serializable] public sealed class BarracksDomains { public BarracksView training; }
    [Serializable] public sealed class BarracksEstate { public long version; }
    [Serializable] public sealed class BarracksReply
    {
        public bool done;
        public string reason;
        public BarracksEstate estate;
        public BarracksResource[] resources;
        public BarracksDomains domains;
        public BarracksView Training => domains?.training;
    }

    public sealed class BarracksServerClient
    {
        [Serializable] sealed class Created { public string token; }
        [Serializable] sealed class Error { public string code; }
        [Serializable] sealed class StartPayload { public string unitId; public int count; }
        [Serializable] sealed class CancelPayload { public string jobId; }
        public string Endpoint { get; }
        public BarracksReply Latest { get; private set; }
        public string Status { get; private set; } = "not-connected";
        public bool Busy { get; private set; }
        public bool Uncertain => pendingJson != null;
        public bool Connected => !string.IsNullOrEmpty(token);
        string token, pendingJson;

        public BarracksServerClient(string endpoint)
        {
            if (!ValidEndpoint(endpoint)) throw new ArgumentException("A loopback prototype server URL is required.", nameof(endpoint));
            Endpoint = endpoint.TrimEnd('/');
        }
        public static bool ValidEndpoint(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.IsLoopback && (uri.Scheme == "http" || uri.Scheme == "https")
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/";
        public static bool ValidToken(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
        public static string StartJson(Guid id, long version, string unit, int count) => Envelope(id, version, "start",
            JsonUtility.ToJson(new StartPayload { unitId = unit, count = count }));
        public static string CancelJson(Guid id, long version, string job) => Envelope(id, version, "cancel-waiting",
            JsonUtility.ToJson(new CancelPayload { jobId = job }));
        static string Envelope(Guid id, long version, string operation, string payload) =>
            "{\"commandId\":\"" + id.ToString("D") + "\",\"expectedVersion\":" + version.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"domain\":\"training\",\"operation\":\"" + operation + "\",\"payload\":" + payload + "}";

        public IEnumerator Connect(string bearer, bool create)
        {
            if (Busy || Uncertain) yield break;
            if (!create && !ValidToken(bearer)) { Status = "invalid-token"; yield break; }
            if (create)
            {
                yield return Request("POST", "/prototype/accounts", "{}", json =>
                {
                    var created = JsonUtility.FromJson<Created>(json);
                    if (created == null || !ValidToken(created.token)) throw new FormatException();
                    token = created.token;
                }, false);
                if (!Connected) yield break;
            }
            else token = bearer;
            yield return Refresh();
        }
        public IEnumerator Refresh()
        {
            if (Busy || !Connected || Uncertain) yield break;
            yield return Request("GET", "/v1/account", null, Accept, false);
        }
        public IEnumerator Start(string unit, int count)
        {
            if (Busy || Uncertain || Latest?.estate == null) yield break;
            pendingJson = StartJson(Guid.NewGuid(), Latest.estate.version, unit, count);
            yield return Retry();
        }
        public IEnumerator CancelWaiting()
        {
            if (Busy || Uncertain || Latest?.Training?.HasWaiting != true) yield break;
            pendingJson = CancelJson(Guid.NewGuid(), Latest.estate.version, Latest.Training.waiting.jobId);
            yield return Retry();
        }
        public IEnumerator Retry()
        {
            if (Busy || pendingJson == null || !Connected) yield break;
            yield return Request("POST", "/v1/account/commands", pendingJson, Accept, true);
        }
        void Accept(string json)
        {
            var reply = JsonUtility.FromJson<BarracksReply>(json);
            if (reply?.estate == null || reply.Training?.units == null || reply.resources == null) throw new FormatException();
            Latest = reply;
            Status = string.IsNullOrEmpty(reply.reason) ? "connected" : reply.reason;
        }
        IEnumerator Request(string method, string path, string json, Action<string> accept, bool command)
        {
            Busy = true; Status = "pending";
            using (var request = new UnityWebRequest(Endpoint + path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 15; request.redirectLimit = 0;
                if (json != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                if (Connected) request.SetRequestHeader("Authorization", "Bearer " + token);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try { accept(request.downloadHandler.text); if (command) pendingJson = null; }
                    catch (Exception) { Status = "invalid-response"; }
                }
                else if (request.responseCode >= 400 && request.responseCode < 500)
                {
                    Error error = null;
                    try { error = JsonUtility.FromJson<Error>(request.downloadHandler.text); } catch (ArgumentException) { }
                    Status = request.responseCode == 401 ? "unauthorized" : error?.code ?? "request-rejected";
                    if (request.responseCode == 401) { token = null; Latest = null; }
                    // A definite rejection did not spend; an uncertain transport/5xx retains the original ID and body.
                    if (command && request.responseCode != 429) pendingJson = null;
                }
                else Status = command ? "outcome-unknown" : "connection-failed";
            }
            Busy = false;
        }
    }
}
