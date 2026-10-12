using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Profiling;
#endif
namespace Game.View
{
    public static class FontProvider
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly ProfilerMarker FontSupportProbeMarker = new ProfilerMarker("SowSiege.View.FontSupportProbe");
        static readonly ProfilerMarker FontRequestCharactersMarker = new ProfilerMarker("SowSiege.View.FontRequestCharacters");
        static readonly ProfilerMarker FontNewCharactersMarker = new ProfilerMarker("SowSiege.View.FontNewCharacters");
        static readonly ProfilerMarker FontTextureRebuiltMarker = new ProfilerMarker("SowSiege.View.FontTextureRebuilt");
        static readonly Dictionary<Font, HashSet<char>> RequestedCharactersByFont = new Dictionary<Font, HashSet<char>>();
        static FontProvider() { Font.textureRebuilt += OnFontTextureRebuilt; }
#endif
        public const string Labels = "씨앗과 공성 시작 설정 돌아가기 이동 방향 가까운 적 자동 조준 한 손으로 화면을 끌어 이동하세요 공격과 도구는 자동으로 작동합니다 봄 여름 가을 겨울 남음 체력 경험치 레벨 성장 선택 현재 보유 없음 무기 도구 사람 땅 장신구 진화 다시 뽑기 금지 고정 해제 선택하면 계속됩니다 생존 사망 한 해 완료 결산 사냥 수확 세금 피해 비중 다시 하기 나가기 오류 준비 중 잠시 멈춤 계속 일반 희귀 영웅 조건 가능 완료 처치 이번 판 빌드 보스 격파 미격파 소리 음량 진동 화면 흔들림 피해 숫자 켜짐 꺼짐 익은 밭 백성 징집 함께 키우세요 0123456789% → +";
        public static Font Create(IEnumerable<string> contentNames)
        {
            var text = Labels + string.Concat(contentNames);
            // Full face names avoid selecting a Thin face from family enumeration.
            var regular = Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans KR Regular", "NotoSansKR-Regular", "Apple SD Gothic Neo" }, GamePalette.BodySize);
            if (Supports(regular, text)) return regular;
            if (regular != null) UnityEngine.Object.Destroy(regular);
            var names = Font.GetOSInstalledFontNames();
            var mac = Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer;
            var candidates = names.OrderBy(n => n.Contains("Noto Sans KR") ? 0 : n.Contains("Noto Sans CJK KR") ? 1 : mac && n == "Apple SD Gothic Neo" ? 2 : n == "AppleGothic" ? 3 : 4).ToArray();
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (FontSupportProbeMarker.Auto()) RequestCharactersInTexture(font, text);
#else
            font.RequestCharactersInTexture(text, GamePalette.BodySize);
#endif
            return text.Where(c => !char.IsWhiteSpace(c)).Distinct().All(font.HasCharacter);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static void RequestCharactersInTexture(Font font, string text)
        {
            var hasNewCharacters = TrackNewCharacters(font, text);
            using (FontRequestCharactersMarker.Auto())
            {
                if (hasNewCharacters) using (FontNewCharactersMarker.Auto()) font.RequestCharactersInTexture(text, GamePalette.BodySize);
                else font.RequestCharactersInTexture(text, GamePalette.BodySize);
            }
        }
        static void OnFontTextureRebuilt(Font font)
        {
            using (FontTextureRebuiltMarker.Auto()) { }
        }
        static bool TrackNewCharacters(Font font, string text)
        {
            if (!RequestedCharactersByFont.TryGetValue(font, out var requested)) RequestedCharactersByFont[font] = requested = new HashSet<char>();
            var added = false;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c)) continue;
                added |= requested.Add(c);
            }
            return added;
        }
#endif
    }
}
