using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Game.App
{
    public sealed partial class BarracksServerClient
    {
        public WorldMarchView[] WorldMarches { get; private set; }
        public WorldMarchResult WorldMarchResult { get; private set; }
        public WorldShieldView WorldShield { get; private set; }
        public string WorldMarchStatus { get; private set; } = "not-loaded";
        public string WorldShieldStatus { get; private set; } = "not-loaded";
        public bool MarchUncertain => marchPending != null;
        PendingAccountCommand marchJournal;
        string marchScope, marchPending;
        void ClearMarchAuthentication()
        {
            WorldMarches = null; WorldMarchResult = null; WorldMarchStatus = "not-loaded"; WorldShield = null; WorldShieldStatus = "not-loaded";
            marchJournal = null; marchScope = null; marchPending = null;
        }
        bool BindMarchJournal()
        {
            if (!Connected || string.IsNullOrEmpty(pendingScope)) return false;
            if (marchScope == pendingScope) return true;
            try
            {
                var journal = new PendingAccountCommand(Path.Combine(pendingRoot, "world-marches"), pendingScope + "\n" + WorldMarchResponse.Endpoint, WorldMarchResponse.ValidateOrder);
                var saved = journal.Read(); marchJournal = journal; marchScope = pendingScope; marchPending = saved; return true;
            }
            catch (Exception) { Status = "pending-storage-error"; return false; }
        }
        void ClearMarchPending() { marchJournal.Write(null); marchPending = null; }
        public IEnumerator LoadWorldMarches()
        {
            if (Busy || !BindMarchJournal()) yield break;
            var revision = authenticationRevision;
            var previousStatus = Status;
            WorldMarches = null;
            WorldMarchStatus = "pending";
            yield return Request("GET", "/v1/world/marches", null, json => { WorldMarches = WorldMarchResponse.ReadList(json); Status = "connected"; }, false);
            if (revision != authenticationRevision || !Connected) yield break;
            WorldMarchStatus = Status;
            if (WorldMarches == null) Status = previousStatus;
        }
        public IEnumerator LoadWorldShield()
        {
            if (Busy || !Connected) yield break;
            yield return Request("GET", "/v1/world/shield", null, json => { WorldShield = WorldMarchResponse.ReadShield(json); Status = "connected"; }, false);
            WorldShieldStatus = Status;
        }
        public IEnumerator DispatchGather(WorldMapTarget target, string hero, WorldMarchTroops[] troops)
        {
            if (Busy || Uncertain || !AccountReady || target == null || target.Kind != "resource" || !BindMarchJournal() || MarchUncertain) yield break;
            yield return SubmitMarch(new WorldMarchOrder { commandId = Guid.NewGuid().ToString("D"), expectedVersion = Latest.estate.version, operation = "gather", targetId = target.Id,
                generation = target.Generation, heroId = hero, troops = troops });
        }
        public IEnumerator RecallWorldMarch(string id)
        {
            if (Busy || Uncertain || !AccountReady || !BindMarchJournal() || MarchUncertain) yield break;
            yield return SubmitMarch(new WorldMarchOrder { commandId=Guid.NewGuid().ToString("D"),expectedVersion=Latest.estate.version, operation="cancel",marchId=id });
        }
        IEnumerator SubmitMarch(WorldMarchOrder command)
        {
            var body = WorldMarchResponse.OrderJson(command);
            try { marchJournal.Write(body); marchPending = body; }
            catch (Exception) { Status = "pending-storage-error"; yield break; }
            yield return RetryWorldMarch();
        }
        public IEnumerator RetryWorldMarch()
        {
            if (Busy || !BindMarchJournal() || !MarchUncertain) yield break;
            var revision = authenticationRevision; WorldMarchResult = null;
            yield return Request("POST", WorldMarchResponse.Endpoint, marchPending, json =>
            {
                var reply = WorldMarchResponse.ReadReply(json);
                if (reply.Done && reply.March == null) throw new FormatException("Successful command lacks march.");
                WorldMarchResult = reply; Status = reply.Reason;
            }, true, ClearMarchPending);
            if (revision != authenticationRevision || MarchUncertain || !Connected) yield break;
            var resultStatus = Status;
            yield return LoadWorldMarches();
            if (revision != authenticationRevision || !Connected || Status != "connected") yield break;
            yield return Refresh();
            if (revision == authenticationRevision && Status == "connected") Status = resultStatus;
        }
    }
}
