using System;
using System.Collections;
using UnityEngine;

namespace Game.App
{
    [Serializable] public sealed class AllianceInfoView
    {
        public string id, name, mode, notice, status, leader;
        public int memberCount, memberLimit;
    }
    [Serializable] public sealed class AllianceChatMessage
    {
        public string messageId, author, text;
        public long atMs;
    }
    [Serializable] public sealed class AllianceChatView
    {
        public string role;
        public AllianceInfoView alliance;
        public AllianceChatMessage[] chat;
        public string[] blocked;
    }
    [Serializable] public sealed class AllianceCommandReply
    {
        public bool done;
        public string reason, reportId;
        public long accountVersion;
        public AllianceChatView alliance;
    }

    public sealed partial class BarracksServerClient
    {
        [Serializable] sealed class AllianceReportPayload { public string messageId, reason, note; }
        [Serializable] sealed class AllianceBlockPayload { public string accountId; }
        public AllianceChatView AllianceLatest { get; private set; }
        public AllianceCommandReply AllianceResult { get; private set; }
        public void PrepareAlliance() => RegisterCommandReplyReader("alliance", AcceptAllianceCommand);
        partial void ClearDomainAuthentication()
        {
            AllianceLatest = null; AllianceResult = null;
        }

        public IEnumerator RefreshAlliance()
        {
            PrepareAlliance();
            if (Busy || Uncertain || !Connected) yield break;
            yield return Refresh();
            if (Connected && Latest?.estate != null)
                yield return Request("GET", "/v1/alliance", null, json =>
                {
                    AllianceLatest = ParseAllianceView(json); Status = "connected";
                }, false);
        }
        public IEnumerator ReportAllianceMessage(string messageId, string reason, string note)
        {
            PrepareAlliance();
            if (Busy || Uncertain || !Connected) yield break;
            if (!Guid.TryParse(messageId, out _)) throw new ArgumentException("A server message ID is required.", nameof(messageId));
            AllianceResult = null;
            yield return SubmitAccountCommand("alliance", "report", JsonUtility.ToJson(new AllianceReportPayload
                { messageId = messageId, reason = reason, note = note ?? "" }));
            if (!Uncertain && Connected) yield return Refresh();
        }
        public IEnumerator BlockAllianceAuthor(string accountId, bool blocked)
        {
            PrepareAlliance();
            if (Busy || Uncertain || !Connected) yield break;
            if (!Guid.TryParse(accountId, out _)) throw new ArgumentException("A server author ID is required.", nameof(accountId));
            AllianceResult = null;
            yield return SubmitAccountCommand("alliance", blocked ? "block" : "unblock",
                JsonUtility.ToJson(new AllianceBlockPayload { accountId = accountId }));
            if (!Uncertain && Connected) yield return Refresh();
        }
        public static AllianceChatView ParseAllianceView(string json)
        {
            var view = JsonUtility.FromJson<AllianceChatView>(json);
            ValidateAllianceView(view); return view;
        }
        static void ValidateAllianceView(AllianceChatView view)
        {
            if (view == null || string.IsNullOrEmpty(view.role) || view.chat == null || view.blocked == null)
                throw new FormatException("Incomplete alliance view.");
            if (view.role != "none" && (view.alliance == null || !Guid.TryParse(view.alliance.id, out _)))
                throw new FormatException("Missing alliance identity.");
            foreach (var message in view.chat)
                if (message == null || !Guid.TryParse(message.messageId, out _) || message.text == null
                    || (!string.IsNullOrEmpty(message.author) && !Guid.TryParse(message.author, out _)))
                    throw new FormatException("Invalid alliance message.");
            foreach (var account in view.blocked)
                if (!Guid.TryParse(account, out _)) throw new FormatException("Invalid blocked account.");
        }
        void AcceptAllianceCommand(string json)
        {
            var reply = JsonUtility.FromJson<AllianceCommandReply>(json);
            if (reply == null || string.IsNullOrEmpty(reply.reason) || reply.accountVersion <= 0)
                throw new FormatException("Incomplete alliance command reply.");
            ValidateAllianceView(reply.alliance);
            if (reply.done && (reply.reason == "reported" || reply.reason == "already-reported") && !Guid.TryParse(reply.reportId, out _))
                throw new FormatException("Missing report receipt.");
            AllianceResult = reply; AllianceLatest = reply.alliance;
            if (Latest == null) Latest = new BarracksReply { estate = new BarracksEstate { version = reply.accountVersion } };
            else if (Latest.estate != null) Latest.estate.version = Math.Max(Latest.estate.version, reply.accountVersion);
            Status = reply.reason;
        }
    }
}
