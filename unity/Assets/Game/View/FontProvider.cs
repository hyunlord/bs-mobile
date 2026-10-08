using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Game.View
{
    public static class FontProvider
    {
        public const string Labels = "씨앗과 공성 시작 설정 돌아가기 이동 방향 가까운 적 자동 조준 한 손으로 화면을 끌어 이동하세요 공격과 도구는 자동으로 작동합니다 봄 여름 가을 겨울 남음 체력 경험치 레벨 성장 선택 현재 보유 없음 무기 도구 사람 땅 장신구 진화 다시 뽑기 금지 고정 해제 선택하면 계속됩니다 생존 사망 한 해 완료 결산 사냥 수확 세금 피해 비중 다시 하기 나가기 오류 준비 중 잠시 멈춤 계속 희귀도는 선택 후 결정됩니다";
        public static Font Create(IEnumerable<string> contentNames)
        {
            var text = Labels + string.Concat(contentNames);
            var names = Font.GetOSInstalledFontNames();
            var mac = Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer;
            var candidates = names.OrderBy(n => mac && n == "Apple SD Gothic Neo" ? 0 : n.Contains("Noto Sans CJK KR") ? 1 : n.Contains("Noto Sans KR") ? 2 : n == "AppleGothic" ? 3 : 4).ToArray();
            var font = Font.CreateDynamicFontFromOSFont(candidates, GamePalette.BodySize);
            if (Supports(font, text)) return font;
            if (font != null) UnityEngine.Object.Destroy(font);
            foreach (var path in Font.GetPathsToOSFonts().Where(p => p.IndexOf("Noto", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("Gothic", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var fallback = new Font(path);
                if (Supports(fallback, text)) return fallback;
                UnityEngine.Object.Destroy(fallback);
            }
            throw new InvalidOperationException("Installed OS fonts cannot display required Korean glyphs.");
        }
        public static bool Supports(Font font, string text)
        {
            if (font == null) return false;
            font.RequestCharactersInTexture(text, GamePalette.BodySize);
            return text.Where(c => !char.IsWhiteSpace(c)).Distinct().All(font.HasCharacter);
        }
    }
}
