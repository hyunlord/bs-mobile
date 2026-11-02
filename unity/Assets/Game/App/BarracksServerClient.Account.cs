using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Game.App
{
    public sealed partial class BarracksServerClient
    {
        [Serializable] sealed class RecruitPayload { public string poolVersion; }
        [Serializable] sealed class ResearchPayload { public string nodeId; }
        readonly string pendingRoot;
        readonly Dictionary<string, Action<string>> commandReplyReaders = new Dictionary<string, Action<string>>(StringComparer.Ordinal);
        PendingAccountCommand pendingStore;
        string pendingScope;
        int authenticationRevision;
        public RecruitAccountView RecruitView { get; private set; }
        public ResearchAccountView ResearchView { get; private set; }

        public bool MatchesCredentials(string bearer, string accountId) => Connected && token == bearer
            && Guid.TryParse(accountId, out var id) && pendingScope == Endpoint + "\n" + id.ToString("D");
        partial void ClearDomainAuthentication();
        public void ClearAuthentication()
        {
            authenticationRevision++;
            token = null; Latest = null; RecruitView = null; ResearchView = null;
            ClearDomainAuthentication();
            Status = "unauthorized";
            // Retain the account-bound command journal for reauthentication and exact replay.
        }
        public void RegisterCommandReplyReader(string domain, Action<string> reader)
        {
            if (string.IsNullOrWhiteSpace(domain) || reader == null) throw new ArgumentException("A domain reply reader is required.");
            commandReplyReaders[domain] = reader;
        }
        public static string AccountCommandJson(Guid id, long version, string domain, string operation, string payloadJson)
        {
            // Domain and operation are fixed by the public methods, payload is produced by JsonUtility.
            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(operation) || domain.IndexOfAny(new[] {'"', '\\'}) >= 0 || operation.IndexOfAny(new[] {'"', '\\'}) >= 0)
                throw new ArgumentException("Invalid account command name.");
            var body = "{\"commandId\":\"" + id.ToString("D") + "\",\"expectedVersion\":" + version.ToString(CultureInfo.InvariantCulture)
                + ",\"domain\":\"" + domain + "\",\"operation\":\"" + operation + "\",\"payload\":" + payloadJson + "}";
            PendingAccountCommand.Domain(body); return body;
        }
        public IEnumerator ConnectAuthenticated(string bearer, string accountId)
        {
            if (Busy || !ValidToken(bearer) || !Guid.TryParse(accountId, out var id)) { Status = "invalid-token"; yield break; }
            try { BindPending(id.ToString("D")); }
            catch (Exception) { Status = "pending-storage-error"; yield break; }
            token = bearer;
            if (Uncertain) yield return Retry(); else yield return Refresh();
        }
        void BindPending(string accountScope)
        {
            var scope = Endpoint + "\n" + accountScope;
            if (pendingScope == scope) return;
            if (Uncertain) throw new InvalidOperationException("Resolve the current account command before changing accounts.");
            var store = new PendingAccountCommand(pendingRoot, scope);
            var saved = store.Read();
            pendingStore = store; pendingScope = scope; pendingJson = saved;
        }
        bool SavePending(string body)
        {
            try
            {
                if (pendingStore == null) throw new InvalidOperationException("No authenticated account scope.");
                pendingStore.Write(body); pendingJson = body; return true;
            }
            catch (Exception) { Status = "pending-storage-error"; return false; }
        }
        void ClearPending()
        {
            pendingStore.Write(null); pendingJson = null;
        }
        IEnumerator SubmitAccountCommand(string domain, string operation, string payloadJson)
        {
            if (Busy || Uncertain || !Connected || Latest?.estate == null) yield break;
            if (!SavePending(AccountCommandJson(Guid.NewGuid(), Latest.estate.version, domain, operation, payloadJson))) yield break;
            yield return Retry();
        }
        public IEnumerator Recruit(string poolVersion) => SubmitAccountCommand("recruit", "recruit", JsonUtility.ToJson(new RecruitPayload { poolVersion = poolVersion }));
        public IEnumerator StartResearch(string nodeId) => SubmitAccountCommand("research", "start-research", JsonUtility.ToJson(new ResearchPayload { nodeId = nodeId }));
        public IEnumerator CompleteResearch(string nodeId) => SubmitAccountCommand("research", "complete-research", JsonUtility.ToJson(new ResearchPayload { nodeId = nodeId }));
    }
}
