# 모바일 ASTC4 적용 (#487)

상태: 구현·정적검사, Unity 검증 대기. 2026-10-28. 사용자가 #465 실제 ASTC4 품질시험 후 모바일 적용을 승인했다. 이 PR은 승인된 최대3개 atlas만 대상으로 하며 그 밖의 텍스처·FX는 그대로다.

## 변경과 경계

Android/iOS override = ASTC_4x4, 4096px 상한, mip 없음. 원본PNG·Default/Standalone(import Uncompressed)·필터·pivot·기본Mac표시·Core·저장·리플레이는 변경하지 않는다. `ArtPreparation`의 원본alpha-padding 검사는 source PNG 임시RGBA에서 실행해 모바일 압축손실과 원판결함을 혼동하지 않도록 한다. 기존zero-padding과역사16허용규칙은 그대로다.

Unity공식 [TextureImporterFormat 소스](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/AssetPipeline/TextureImporterEnums.cs)에서 ASTC_4x4=48이며 [platform API](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/textureimporter/getplatformtexturesettings)는 iPhone 이름을 지원한다. 기존6000.6 직렬화 플랫폼이름 iOS는 그대로 두고 EditMode에서 iPhone API override조회로 확인한다.

## 예상 메모리 (MiB)

| Atlas | 치수 | 기존 RGBA/Mac 유지 | Android/iOS ASTC4 | 예상 감소 |
|---|---|---:|---:|---:|
| hero-knight | 2564×2164 | 21.16583 | 5.29146 | 75% |
| fallow-enemy-rigs | 2048×2304 | 18 | 4.5 | 75% |
| estate-decor | 2048×2428 | 18.96875 | 4.74219 | 75% |
| 합계 | | 58.13458 | 14.53365 | 43.60094MiB |

ASTC4 산식은 ceil(width/4)×ceil(height/4)×16 bytes, mip 없음. 전체94PNG RGBA 예상361.526936MiB를 단순대입하면317.925999MiB이나 모든자산동시로드/실행메모리/모바일RAM의 증거가 아니다. ASTC 미지원기기 fallback·드라이버할당·빌드크기·기기성능은 NOT_MEASURED. Fold7 보류 유지. APK/iOS빌드·플랫폼전환·실기실행은 이번 검증에 포함하지 않는다.

## PR 변경 기록 검사

`tools/pr-policy.mjs`가 실제 PR merge-base diff의 runtime PNG/PNG.meta만 확인한다. 변경 경로마다 같은 PR의 `docs/metrics/texture-memory-deltas/*.json`에 정확히1개 before/after 기록이 필요하다. 과거미변경그림을 소급차단하지 않는다. 모든값은 ESTIMATE이며 파일identity(sha256/size)·플랫폼·mip·바이트산식과 현재PNG/메타를 확인한다. PNG가같으면 이전메타까지자동검증한다. 이전LFS원판이CI에없을수있으므로 PNG가바뀐경우 before치수는base oid/size에연결된작성자추정이며 자동실측으로표현하지 않는다. 새전역메모리하드상한은 없다.

## 시각 근거

세 PNG의 SHA256은 실제trial source7817fd56과 동일하다. [승인시험 Release](https://github.com/hyunlord/bs-mobile/releases/tag/astc-texture-trial-461-20261028)의 실제전투1x/3x·걷기네위상·최신영지24작업칸알파COMPOSITE를 재사용한다. 역사전투source d257dc7와 최신estate7817fd56을 구분한다. 얇은FX선은 압축대상이 아니며 같은장면의 변경없음대조군으로만 제시한다. FX 압축품질검증이라고 주장하지 않는다. 제품모바일적용후실기화면은 NOT_RUN.

## 검증

Node: 기록누락·stale identity·틀린산식 음성대조, 실제git PR diff통합, Mac보존/모바일계산. Unity: 각3atlas Android/iPhone ASTC4override조회 및 MacRGBA유지, 원본padding alpha1실패/0통과, 두프로필예정. 결과는 실행 후 갱신한다.

PR head와 CI merge checkout의 PNG가 다르면 merge 그림을 PR 그림으로 오인하지 않는다. head blob 또는 로컬 LFS oid를 읽고, 그 oid가 없을 때만 해당 PNG 경로 하나를 `git lfs fetch --include`로 받는다. checkout은 바꾸지 않는다.
