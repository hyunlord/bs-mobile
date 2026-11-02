using System;
using Game.View;
using UnityEngine;

namespace Game.App
{
    public static class AccountAccessPanels
    {
        public const string LossNotice = "Apple·Google 계정에 연결하지 않은 게스트는 앱 삭제·기기 분실·기기 변경 시 진행을 복구할 수 없습니다.";
        public const string DeletionNotice = "삭제를 요청하면 바로 로그아웃하고 연맹에서 나갑니다. 30일 유예 동안 같은 기기에서 ‘계정 복구’를 누르면 삭제를 취소할 수 있습니다. 연맹 소속은 복구되지 않습니다. 유예가 끝나면 영구 삭제됩니다.";
        static RectTransform Root(UiShell ui, string title)
        {
            var root = UiShell.Rect("Account." + title, ui.Content); UiShell.Stretch(root); ui.Surface(root, "ui.panel");
            PortraitWidgets.Label(ui, root, title, 20, 24, ui.SafeRoot.rect.width - 40, 48, PortraitTokens.Title);
            return root;
        }
        public static void Notice(UiShell ui, bool linking, Action proceed, Action back)
        {
            var root = Root(ui, linking ? "계정 연결" : "진행을 지켜 주세요"); var width = ui.SafeRoot.rect.width - 40;
            PortraitWidgets.Label(ui, root, LossNotice, 20, 100, width, 144);
            if (linking) PortraitWidgets.Label(ui, root, "Apple·Google 연결은 아직 제공되지 않습니다. 연결이 완료된 것으로 표시하거나 진행 복구를 보장하지 않습니다.", 20, 270, width, 120);
            PortraitWidgets.Button(ui, root, linking ? "돌아가기" : "안내를 확인하고 계속", proceed, 20, ui.SafeRoot.rect.height - 136, width, 48);
            if (!linking) PortraitWidgets.Button(ui, root, "취소", back, 20, ui.SafeRoot.rect.height - 76, width, 48);
        }
        public static void Settings(UiShell ui, DeviceAuthClient auth, string configurationStatus, Action connect, Action link, Action deletion, Action restore, Action back)
        {
            var root = Root(ui, "계정 설정"); var width = ui.SafeRoot.rect.width - 40;
            PortraitWidgets.Label(ui, root, LossNotice, 20, 86, width, 112);
            var status = auth == null ? configurationStatus : Status(auth.Status);
            if (auth?.Restored == true) status = "계정을 복구했습니다. 이전 연맹 소속은 복구되지 않았습니다.";
            PortraitWidgets.Label(ui, root, status, 20, 210, width, 88);
            var y = 314f;
            if (auth?.DeletionPending == true)
            {
                var purge = string.IsNullOrEmpty(auth.PurgeAt) ? "삭제 처리 결과와 영구 삭제 시각은 아직 확인되지 않았습니다."
                    : "영구 삭제 예정: " + DateTimeOffset.Parse(auth.PurgeAt, System.Globalization.CultureInfo.InvariantCulture).ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz");
                PortraitWidgets.Label(ui, root, purge + "\n같은 기기에서 복구를 선택해야 삭제가 취소됩니다. 연맹은 복구되지 않습니다.", 20, y, width, 120); y += 132;
                PortraitWidgets.Button(ui, root, "계정 복구", restore, 20, y, width, 48, !auth.Busy); y += 60;
            }
            else
            {
                PortraitWidgets.Button(ui, root, auth?.HasDeviceAccount == true ? "이 기기로 로그인" : "게스트로 연결", connect, 20, y, width, 48,
                    auth != null && !auth.Busy && !auth.CreationPending && !auth.Connected); y += 60;
                PortraitWidgets.Button(ui, root, "계정 연결 안내", link, 20, y, width); y += 60;
                PortraitWidgets.Button(ui, root, "계정 삭제", deletion, 20, y, width, 48, auth?.Connected == true && !auth.Busy); y += 60;
            }
            PortraitWidgets.Button(ui, root, "돌아가기", back, 20, ui.SafeRoot.rect.height - 76, width);
        }
        public static void ConfirmDeletion(UiShell ui, Action confirm, Action cancel)
        {
            var root = Root(ui, "계정을 삭제하시겠어요?"); var width = ui.SafeRoot.rect.width - 40;
            PortraitWidgets.Label(ui, root, DeletionNotice, 20, 104, width, 236);
            PortraitWidgets.Label(ui, root, "이 기기 키를 잃거나 앱을 지우면 유예 중에도 복구할 수 없습니다.", 20, 356, width, 96);
            PortraitWidgets.Button(ui, root, "삭제 요청", confirm, 20, ui.SafeRoot.rect.height - 136, width);
            PortraitWidgets.Button(ui, root, "취소", cancel, 20, ui.SafeRoot.rect.height - 76, width);
        }
        public static string Status(string code)
        {
            switch (code)
            {
                case "connected": return "이 기기의 게스트 계정에 연결됐습니다.";
                case "connecting": return "연결 중입니다.";
                case "deleting": return "삭제를 요청하고 있습니다.";
                case "deletion-pending": return "삭제 유예 중입니다.";
                case "deletion-unknown": return "삭제 응답을 확인하지 못했습니다. 자동으로 로그인하거나 삭제를 취소하지 않습니다.";
                case "creation-unknown": return "계정 생성 응답을 확인하지 못했습니다. 중복 생성을 막기 위해 다시 생성하지 않습니다.";
                case "guest-disabled": return "현재 서버에서 새 게스트 생성을 받을 수 없습니다.";
                case "secure-storage-unavailable": return "기기의 보안 저장소를 사용할 수 없어 연결하지 않았습니다.";
                case "invalid-response-or-storage": return "서버 응답 또는 보안 저장을 확인하지 못했습니다.";
                case "invalid-challenge-or-key": return "기기 인증 요청을 확인하지 못했습니다.";
                case "sign-in-failed": return "로그인하지 못했습니다. 연결 상태와 계정 유예 상태를 확인해 주세요.";
                default: return "이 기기의 온라인 계정에 연결되지 않았습니다.";
            }
        }
    }
}
