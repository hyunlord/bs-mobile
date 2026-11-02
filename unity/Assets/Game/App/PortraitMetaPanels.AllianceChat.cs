using System;
using Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public sealed class AllianceChatActions
    {
        public Action Refresh, Back, CloseMenu, BeginReport, Block;
        public Action<string> SelectMessage, Report, Unblock;
    }

    public static partial class PortraitMetaPanels
    {
        static readonly string[] AllianceReportReasons = { "harassment", "hate-threat", "spam-ad", "inappropriate-name", "other" };
        static readonly string[] AllianceReportLabels = { "괴롭힘", "혐오·위협", "도배·광고", "부적절한 이름", "기타" };

        public static void AllianceChat(UiShell ui, BarracksServerClient client, string selectedMessageId, bool choosingReason,
            AllianceChatActions actions)
        {
            var width = Width(ui); var root = Page(ui, "AllianceChat", "연맹 채팅");
            ActionButton(ui, root, "Alliance.Back", "영지로", actions.Back, 16, Height(ui) - 64, 112);
            ActionButton(ui, root, "Alliance.Refresh", client.Uncertain ? "같은 요청 확인" : "새로고침", actions.Refresh,
                width - 160, Height(ui) - 64, 144, !client.Busy);
            var body = Body(ui, root, 96, 82); var bodyWidth = width - 36; float y = 0;
            var current = client.Connected ? client.AllianceLatest : null;
            y += Label(ui, body, AllianceStatus(client), 0, y, bodyWidth);
            if (current == null)
            {
                Label(ui, body, "연맹 정보를 서버에서 확인해 주세요.", 0, y + 12, bodyWidth); End(body, y + 60); return;
            }
            if (current.alliance == null)
            {
                Label(ui, body, "가입한 연맹이 없습니다.", 0, y + 12, bodyWidth); End(body, y + 60); return;
            }
            y += Label(ui, body, current.alliance.name + " · " + current.alliance.memberCount + "/" + current.alliance.memberLimit,
                0, y + 8, bodyWidth, 18) + 16;
            if (current.chat.Length == 0) y += Label(ui, body, "표시할 메시지가 없습니다.", 0, y, bodyWidth) + 12;
            foreach (var message in current.chat)
            {
                var card = Plate(ui, body, "Alliance.Message:" + message.messageId, "ui.panel", 0, y, bodyWidth, 112);
                var author = string.IsNullOrEmpty(message.author) ? "삭제된 사용자" : message.author;
                var authorLabel = Caption(ui, card, author, 12, 8, bodyWidth - 76, 26, 11, true);
                authorLabel.supportRichText = false;
                var text = Caption(ui, card, message.text, 12, 40, bodyWidth - 24, 60, 13, true);
                text.supportRichText = false;
                var textHeight = Mathf.Max(40, text.preferredHeight + 8);
                text.rectTransform.sizeDelta = new Vector2(bodyWidth - 24, textHeight);
                var height = 48 + textHeight; card.sizeDelta = new Vector2(bodyWidth, height);
                var capturedId = message.messageId;
                ActionButton(ui, card, "Alliance.Menu:" + capturedId, "…", () => actions.SelectMessage?.Invoke(capturedId),
                    bodyWidth - 60, 4, 48, !client.Busy && !client.Uncertain);
                y += height + 12;
            }
            if (current.blocked.Length > 0)
            {
                y += Label(ui, body, "차단한 사용자", 0, y, bodyWidth, 18) + 8;
                foreach (var id in current.blocked)
                {
                    var target = id;
                    var caption = Label(ui, body, id, 0, y, bodyWidth - 104, 11);
                    ActionButton(ui, body, "Alliance.Unblock:" + id, "차단 해제", () => actions.Unblock?.Invoke(target),
                        bodyWidth - 100, y, 100, !client.Busy && !client.Uncertain);
                    y += Mathf.Max(48, caption) + 12;
                }
            }
            End(body, y);
            var selected = Array.Find(current.chat, message => message.messageId == selectedMessageId);
            if (selected != null) AllianceMessageMenu(ui, root, client, selected, choosingReason, actions);
        }

        static string AllianceStatus(BarracksServerClient client)
        {
            if (!client.Connected || client.Busy || client.Uncertain || client.Status == "connection-failed" || client.Status == "unauthorized")
                return BarracksStatus(client.Status);
            var result = client.AllianceResult;
            if (result == null) return "서버 상태 확인됨";
            if (result.done && (result.reason == "reported" || result.reason == "already-reported")) return "신고 접수 · " + result.reportId;
            switch (result.reason)
            {
                case "blocked": return "이 사용자의 채팅을 숨겼습니다. 연맹·도움·월드 관계는 바뀌지 않습니다.";
                case "unblocked": return "차단을 해제했습니다.";
                case "not-member": return "연맹 소속이 바뀌었습니다. 새로고침해 주세요.";
                case "unknown-message": case "not-visible": return "이 메시지에 대한 권한이나 보관 상태가 바뀌었습니다.";
                case "invalid-target": return "이 대상은 차단할 수 없습니다.";
                default: return "서버 결과 · " + result.reason;
            }
        }

        static void AllianceMessageMenu(UiShell ui, RectTransform root, BarracksServerClient client, AllianceChatMessage message,
            bool choosingReason, AllianceChatActions actions)
        {
            var width = Width(ui); var height = choosingReason ? 430 : 260;
            var shade = ColorPlate(root, "Alliance.MenuShade", new Color(0, 0, 0, .7f), 0, 0, width, Height(ui));
            shade.GetComponent<Image>().raycastTarget = true;
            var panel = Plate(ui, root, "Alliance.MessageMenu", "ui.panel", 16, Mathf.Max(16, (Height(ui) - height) / 2), width - 32, height);
            Caption(ui, panel, choosingReason ? "신고 사유 선택" : "메시지 메뉴", 16, 12, width - 64, 36, 20, true);
            var enabled = !client.Busy && !client.Uncertain;
            if (choosingReason)
            {
                for (var i = 0; i < AllianceReportReasons.Length; i++)
                {
                    var reason = AllianceReportReasons[i];
                    ActionButton(ui, panel, "Alliance.Report:" + reason, AllianceReportLabels[i], () => actions.Report?.Invoke(reason),
                        16, 60 + i * 56, width - 64, enabled);
                }
            }
            else
            {
                AllianceMenuAction(ui, panel, "Alliance.Report", "신고", "chat-report", actions.BeginReport, 60, width - 64, enabled);
                AllianceMenuAction(ui, panel, "Alliance.Block", "차단", "chat-block", actions.Block, 116, width - 64,
                    enabled && !string.IsNullOrEmpty(message.author));
                if (string.IsNullOrEmpty(message.author)) Caption(ui, panel, "작성자 정보가 없어 차단할 수 없습니다.", 16, 170, width - 64, 28, 11, true);
            }
            ActionButton(ui, panel, "Alliance.MenuClose", "닫기", actions.CloseMenu, 16, height - 60, width - 64);
        }
        static void AllianceMenuAction(UiShell ui, Transform panel, string name, string text, string role, Action action,
            float y, float width, bool enabled)
        {
            var button = ActionButton(ui, panel, name, text, action, 16, y, width, enabled);
            var icon = PortraitWidgets.Place(name + ".Icon", button.transform, 12, 12, 24, 24).gameObject.AddComponent<Image>();
            ChatMarchArt.Surface(icon, role);
        }
    }
}
