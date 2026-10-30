using System;
using System.Linq;
using Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // Approved #563: parchment panels, blue primary actions, dark resource/navigation bars.
    public static partial class PortraitMetaPanels
    {
        public const string BarracksGlyphs = "병영 훈련 서버 연결 개발 계정 새 만들기 기존 토큰 입력 닫기 연결 중 결과 확인 다시 보내기 조회 상비 병력 총 인원 상한 준비 출전 부상 예약 포함 징집 창병 방패 수비병 명 곡물 목재 철재 기본 시간 훈련 뒤 충분 부족 활성 대기 취소 확인 실제 서버 상태 로컬 저장과 별개 이번 실행 동안만 보관 안 됨 병영 필요 선택 자원 모자람 공간 없음 재접속 인증 만료 응답 오류 취소 환불 대기만 가능 서버 주소 실행 옵션 적용 기다려 주세요 연결 실패 새로고침";
        static readonly Color BarracksDark = new Color32(20, 26, 31, 255);
        public static void BarracksConnect(UiShell ui, string endpoint, string status, bool busy, Action<string, bool> connect, Action back)
        {
            var root = Page(ui, "BarracksConnect", ""); ui.ApplySurface(root.GetComponent<Image>(), "ui.panel");
            var w = Width(ui); var body = Body(ui, root, 124, 78);
            ActionButton(ui, root, "Barracks.Back", "‹", back, 8, 60, 48, !busy);
            Caption(ui, root, "개발 서버 · 병영", 68, 60, w - 76, 42, 22, true);
            Caption(ui, body, "로컬 저장과 별개인 서버 계정입니다.\n토큰은 이번 실행 동안만 보관합니다.", 0, 0, w - 36, 60, 15, true);
            Caption(ui, body, endpoint, 0, 66, w - 36, 44, 14, true);
            Caption(ui, body, "기존 계정 토큰", 0, 120, w - 36, 28, 16, true);
            var fieldRect = Plate(ui, body, "Barracks.Token", "ui.panel", 0, 154, w - 36, 52);
            var field = fieldRect.gameObject.AddComponent<InputField>();
            var label = Caption(ui, fieldRect, "", 10, 4, w - 56, 44, 16, true);
            field.textComponent = label; field.targetGraphic = fieldRect.GetComponent<Image>();
            field.contentType = InputField.ContentType.Password; field.characterLimit = 64; field.lineType = InputField.LineType.SingleLine;
            field.interactable = !busy;
            ActionButton(ui, body, "Barracks.Connect", "연결", () => connect(field.text, false), 0, 222, w - 36, !busy, 48, "ui.button.primary");
            ActionButton(ui, body, "Barracks.Create", "새 서버 계정 만들기", () => connect(null, true), 0, 286, w - 36, !busy);
            Caption(ui, body, BarracksStatus(status), 0, 352, w - 36, 72, 15, true); End(body, 440);
        }
        public static void Barracks(UiShell ui, BarracksServerClient client, string selected, int count, bool confirming,
            Action<string> select, Action<int> quantity, Action confirm, Action submit, Action cancel, Action refresh, Action back)
        {
            var root = Page(ui, "Barracks", ""); ui.ApplySurface(root.GetComponent<Image>(), "ui.panel");
            float w = Width(ui), h = Height(ui), inner = w - 36;
            var reply = client.Latest; var state = reply.Training;
            var bar = ColorPlate(root, "Barracks.Resources", BarracksDark, 0, 0, w, 52);
            string[] ids = { "meta:grain", "meta:timber", "meta:iron" };
            for (int i = 0; i < ids.Length; i++)
            {
                float x = 12 + i * (w - 24) / ids.Length;
                MaterialArt(ui, bar, ids[i], "", x, 14, 24);
                Caption(ui, bar, (reply.resources.FirstOrDefault(r => r.id == ids[i])?.amount ?? 0).ToString(), x + 30, 8, 76, 36, 16);
            }
            ActionButton(ui, root, "Barracks.Back", "‹", back, 8, 60, 48, !client.Busy);
            Caption(ui, root, "병영 · 훈련", 68, 60, w - 76, 40, 22, true);
            Caption(ui, root, "병영 Lv " + state.barracksLevel + " · 서버 계정", 18, 106, w - 36, 28, 14, true);
            var body = Body(ui, root, 146, 130);
            var roster = Plate(ui, body, "Barracks.Roster", "ui.panel", 0, 0, inner, 142);
            Caption(ui, roster, "상비 병력", 14, 10, inner - 28, 28, 18, true);
            Caption(ui, roster, "총 인원 / 상한", 14, 44, inner / 2, 26, 16, true);
            Caption(ui, roster, state.occupied + " / " + state.capacity, inner * .66f, 44, inner * .3f, 26, 16, true);
            Caption(ui, roster, "준비 " + state.units.Sum(u => u.ready) + " · 출전 " + state.units.Sum(u => u.deployed)
                + " · 부상 " + state.units.Sum(u => u.wounded), 14, 78, inner - 28, 24, 15, true);
            Caption(ui, roster, "훈련 예약 " + state.units.Sum(u => u.reserved) + "명도 상한에 포함됩니다", 14, 110, inner - 28, 22, 13, true);
            float tabWidth = (inner - 8) / Math.Max(1, state.units.Length);
            for (int i = 0; i < state.units.Length; i++)
            {
                var unit = state.units[i];
                ActionButton(ui, body, "Barracks.Unit:" + unit.unitId, unit.name, () => select(unit.unitId), i * (tabWidth + 8), 154, tabWidth,
                    !client.Busy && !client.Uncertain, 48, unit.unitId == selected ? "ui.button.primary" : "ui.button.secondary");
            }
            var chosen = state.units.FirstOrDefault(u => u.unitId == selected) ?? state.units.FirstOrDefault();
            if (chosen == null) { End(body, 212); return; }
            var detail = Plate(ui, body, "Barracks.TrainingDetail", "ui.panel", 0, 214, inner, 214);
            Caption(ui, detail, chosen.name + " " + count + "명", 14, 10, inner - 28, 28, 18, true);
            string costs = string.Join(" · ", chosen.costPerUnit.Select(c => MaterialName(c.materialId) + " " + (long)c.amount * count));
            Caption(ui, detail, costs, 14, 49, inner - 28, 30, 16, true);
            Caption(ui, detail, "기본 훈련 시간  " + Duration((long)chosen.secondsPerUnit * count), 14, 84, inner - 28, 26, 15, true);
            Caption(ui, detail, "훈련 뒤 총 인원  " + ((long)state.occupied + count) + " / " + state.capacity, 14, 116, inner - 28, 24, 15, true);
            ActionButton(ui, detail, "Barracks.CountLess", "−", () => quantity(Math.Max(1, count - 1)), 14, 154, 48, count > 1 && !client.Busy && !client.Uncertain);
            Caption(ui, detail, count + "명", 72, 157, inner - 144, 42, 18, true, TextAnchor.MiddleCenter);
            ActionButton(ui, detail, "Barracks.CountMore", "+", () => quantity(count + 1), inner - 62, 154, 48,
                count < state.batchLimit && !client.Busy && !client.Uncertain);
            var queue = Plate(ui, body, "Barracks.Queue", "ui.panel", 0, 440, inner, 136);
            Caption(ui, queue, state.HasActive ? "활성 " + state.active.count + "명 · " + Duration(Math.Max(0, state.active.completesAtMilliseconds - state.serverUtcMilliseconds) / 1000) : "활성 훈련 없음", 14, 10, inner - 28, 28, 15, true);
            Caption(ui, queue, state.HasWaiting ? "대기 " + state.waiting.count + "명 · 앞 훈련 완료 후 시작" : "대기 0 / 1", 14, 44, inner - 28, 24, 14, true);
            if (state.HasWaiting) ActionButton(ui, queue, "Barracks.CancelWaiting", "대기 취소", cancel, 14, 78, inner - 28, !client.Busy && !client.Uncertain);
            var unavailable = !chosen.unlocked ? "병영 Lv " + chosen.requiredBarracksLevel + " 필요" : state.HasWaiting ? "대기열이 가득 찼습니다" :
                state.occupied + count > state.capacity ? "병력 상한을 넘습니다" : chosen.costPerUnit.Any(c => (long)c.amount * count > (reply.resources.FirstOrDefault(r => r.id == c.materialId)?.amount ?? 0)) ? "재료가 모자랍니다" : null;
            Caption(ui, body, unavailable ?? BarracksStatus(client.Status), 4, 588, inner - 8, 52, 14, true); End(body, 652);
            ActionButton(ui, root, "Barracks.Train", client.Uncertain ? "결과 확인 · 같은 요청 다시 보내기" : confirming ? "확인 · " + count + "명 훈련" : count + "명 훈련",
                client.Uncertain ? refresh : confirming ? submit : confirm, 20, h - 118, w - 40,
                !client.Busy && (client.Uncertain || unavailable == null), 48, "ui.button.primary");
            ActionButton(ui, root, "Barracks.Refresh", "서버 상태 새로고침", refresh, 20, h - 60, w - 40, !client.Busy && !client.Uncertain);
        }
        static string MaterialName(string id) => id == "meta:grain" ? "곡물" : id == "meta:timber" ? "목재" : id == "meta:iron" ? "철재" : id;
        static string Duration(long seconds) => (seconds / 60) + ":" + (seconds % 60).ToString("00");
        public static string BarracksStatus(string reason)
        {
            switch (reason)
            {
                case "pending": return "서버 확인 중 · 기다려 주세요";
                case "outcome-unknown": case "invalid-response": return "결과를 확인하지 못했습니다. 같은 요청으로 다시 조회합니다.";
                case "connection-failed": return "서버 연결 실패 · 서버 주소와 실행 상태를 확인하세요";
                case "unauthorized": return "계정 인증이 만료됐습니다. 다시 연결하세요";
                case "version-conflict": return "서버 상태가 바뀌었습니다. 새로고침 후 다시 선택하세요";
                case "invalid-token": return "64자리 계정 토큰을 입력하세요";
                case "not-connected": return "새 계정 또는 기존 계정으로 연결하세요";
                case "barracks-required": case "unit-locked": return "병영 완공 레벨이 부족합니다";
                case "insufficient-resources": return "재료가 모자랍니다";
                case "protected-reserve": return "기초 생산시설 건설용 자원은 훈련에 쓸 수 없습니다";
                case "queue-full": return "대기열이 가득 찼습니다";
                case "capacity": return "병력 상한을 넘습니다";
                case "training-started": return "훈련을 시작했습니다";
                case "training-queued": return "대기 훈련을 예약했습니다";
                case "training-cancelled": return "대기 훈련을 취소했습니다";
                case "accepted": case "initialized": case "connected": return "서버 상태 확인됨";
                default: return string.IsNullOrEmpty(reason) ? "" : "서버 상태 · " + reason;
            }
        }
    }
}
