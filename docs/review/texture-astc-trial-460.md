# ASTC 압축 격리 시험 — #460 / #461

- 날짜: 2026-10-28. 상태: **실제 ASTC 압축·GPU decode 확인 / 사용자 품질 판정 대기**.
- 기준 main: `437d62a4c8f25700979f22cbf0877f648b978aaa`.
- 범위: 임시 텍스처 사본을 실제 ASTC 4×4·6×6으로 압축하고 같은 실제 입력 리플레이 장면에서 비교한다. 적용안·예산은 제안만 한다.
- 원본 PNG, importer `.meta`, Prepare의 Uncompressed 계약, Mac 기본 표시, Core·리플레이는 바꾸지 않는다.

## 대상과 증거 구분

| registry ID | 원본 크기 | 성격 |
| --- | --- | --- |
| hero-knight | 2564×2164 | 현재 영웅 시트 |
| fallow-enemy-rigs | 2048×2304 | 기준 main 적 시트; #455의 확장 atlas와 혼동 금지 |
| feel-hero-b-layered | 2048×2048 | legacy 참조; 실제 전투 사용을 주장하지 않고 별도 셀 비교 |

리소스 registry 참조 크기와 실제 화면 사용 여부는 다르다. 원자료·PNG는 로컬 artifacts에 두고, 선정한 비교 화면과 작은 정본 측정표만 납품한다.

## 실행 계약

공유 Mac 잠금·시간 제한을 먼저 따른다. Unity 슬롯 승인 전에는 실행하지 않는다. 기존 `RecordedWaveRangeExportsActualTickFrames`에 다음 환경변수만 추가한다.

- `ASTC_TRIAL_MODE=rgba`, `4`, `6`: 모드마다 새 프로세스/신선한 출력 경로.
- `ASTC_TRIAL_SMALL_PROBE=1`: 최초 실행에만 사용. 16×16 실제 ASTC 4×4·6×6 encode/GPU decode를 먼저 확인하고 atlas는 교체하지 않는다. 이 실행의 전투 화면은 압축 비교가 아니다.
- `ASTC_TRIAL_OUTPUT`: atlas 원본 GPU readback, 실제 ASTC blocks, GPU decode PNG, 형식/바이트 보고서 디렉터리. 빈 폴더 필수.
- `ASTC_TRIAL_INVENTORY_PATHS`: 선택. 감사 목록의 `Assets/...png` 경로를 한 줄씩 적은 파일. 첫 실행에만 지정하여 실제 Editor format·크기·mipmap·readable·Profiler runtime bytes를 기록한다. 모바일 실측이 아니다.
- 기존 `WAVE_QA_REPLAY`, `WAVE_QA_START_TICK`, `WAVE_QA_END_TICK`, `WAVE_QA_SEQUENCE_OUTPUT`, `WAVE_QA_RENDERER_COMMIT`도 명시한다. 세 모드는 동일 입력·범위·카메라·소스 커밋을 사용한다.

기존 30Hz 실제 입력 캡처는 체크포인트와 전체 종료 해시를 검사한다. 비교는 동일 frame 번호의 정지 화면을 사용한다. UI 없는 전투 1배와 최근접 3배 확대, 네 걷기 위상을 사전 선정한다. 이것은 성능 측정이나 모바일 실행이 아니다.

압축 전에 `SystemInfo.SupportsTextureFormat`을 확인한다. 지원하지 않으면 실패하며 원본 대체·비슷한 손실 모사로 통과시키지 않는다. `EditorUtility.CompressTexture`가 실패하면 원본을 그대로 둘 수 있으므로 요청 형식과 실제 `texture.format`의 동일성을 확인한다. mipmap은 하나이며 바이트 수는 `ceil(w/block) × ceil(h/block) × 16`과 실제 raw payload를 비교한다.

registry의 배열 참조만 일시 교체하고 finally에서 복원·cache invalidate한다. AssetDatabase save/import, 플랫폼 전환, importer 수정은 하지 않는다. 기본 환경변수가 없으면 아무 압축도 수행하지 않는다.

## 품질·메모리 기록

```sh
node --test tools/test-astc-trial.mjs
node tools/summarize-astc-trial.mjs artifacts/TRIAL unity/Assets/Art/wave1a-manifest.json
```

알파 MAE/최대 오차, 투명 원본에 생긴 알파, 불투명 픽셀 손실, 역할 주변 패딩의 비영 알파를 기록한다. 압축 후 패딩이 손상되어도 원본의 정확한 투명 패딩 검사를 완화하지 않는다. RGB 평균만으로 가는 선·저알파 효과 통과를 판정하지 않는다. 선택 셀은 풀밭과 어두운 배경 위 1배·3배로 비교한다. legacy atlas는 비활성 셀 비교로 명시한다.

`rgba`는 GPU readback 왕복 대조군이다. 대조군은 atlas RGBA 왕복 모든 채널 바이트 동일성을 강제한다. 원래 게임 캡처와 이 대조군이 달라지면 색공간·알파 왕복 문제부터 보고하고 ASTC 비교를 중단한다. decoded PNG는 다시 RGBA이므로 파일 크기·메모리 절감의 증거가 아니다. 압축 payload, Editor texture memory, 모바일 지원/미지원 해제 비용을 각각 구별한다.

## 결과

- Node 수치 검사: 3/3 통과.
- Unity6000.6.4f1 / Apple M4 Max / Metal, 소스 `d257dc7`: 16px RGBA·ASTC4·ASTC6 probe 각 요청 형식과 raw bytes 확인.
- 전체 3atlas × rgba/4/6 실제 압축·GPU decode 통과. 원본 전투→rgba 왕복 24장 PNG 바이트 동일. 각 리플레이 실행 체크포인트·종료 해시 확인.
- 두 프로필 전체: first-playable EditMode 865 / PlayMode 55, wave-1a 875 / 87 통과, 실패 0. 원명령 exit1은 sidecar/catalog 없는 skip2 때문이며 그대로 보존. 기존 생성 도구로 두 입력을 만든 보완 검사 각각1/1 통과 후 결합 skip 0 unexplained / 1 listed. 추적 파일 변화 0, 소유 잠금 해제. 모바일 결과: **미실행**.
- 모바일 ASTC 적용 및 예산 변경: **미승인·미적용**.

## 실제 압축 기록 (2026-10-28)

| atlas | RGBA payload MiB | ASTC4 MiB | ASTC6 MiB | alpha MAE 4 / 6 | alpha 최대 오차 4 / 6 | 패딩 비영 alpha 4 / 6 |
| --- | ---: | ---: | ---: | --- | --- | --- |
| hero-knight | 21.1658 | 5.2915 | 2.3576 | 0.1291 / 0.2738 | 72 / 252 | 0 / 102 |
| fallow-enemy-rigs | 18 | 4.5 | 2.0039 | 0.1639 / 0.4528 | 41 / 122 | 0 / 0 |
| feel-hero-b-layered | 16 | 4 | 1.7847 | 0.1863 / 0.3336 | 73 / 203 | 0 / 0 |

원본·rgba 왕복은 alpha 오차와 패딩 누출 모두 0이다. 위 alpha 수치는 0–255 채널 단위이고 패딩은 역할별 중복 샘플이 있을 수 있다. ASTC6 영웅 패딩의 비영 102샘플은 최대 alpha **1/255**인 미세 누출이며, atlas 전체의 최대 alpha 오차 **252**와 다른 지표다. 경계 누출을 0으로 덮지 않는다. 압축 후 알파 손실은 사용자 확대 검토 대상이며 자동 품질 통과가 아니다.

제안: 영웅·적 시트는 ASTC4 후보부터 검토하고, ASTC6 영웅 시트는 누출 해결/사용자 확인 전 권하지 않는다. legacy atlas는 전투 품질 대표로 삼지 않는다. 세 atlas의 raw payload만 55.1658→13.7915/6.1462MiB이며, Mac 실제 적용이나 모바일 실행 메모리 절감이 아니다. importer 설정 및 예산 관문은 변경하지 않았다.

로컬 증거: `artifacts/astc-trial/probe-r1`, `full-original-r1`, `full-rgba-r1`, `full-4-r1`, `full-6-r1`, `metrics-*.json`. 실제 전투는 tick2850–2873, 원본/각모드 동일. 별도 셀 시트는 COMPOSITE로 분리한다. ffmpeg drawtext 미지원으로 같은 배치의 제목 합성 2건이 실패했으며 그 경로를 중단했다. 원본 캡처·압축 결과는 정상 보존한다.

## 근거

설치된 Unity 6000.6.4f1 공식 문서에서 확인했다. AndroidPlayer(SDK/NDK/JDK)와 iOSSupport는 설치되어 있다. astcenc CLI는 PATH에 없고 Unity 내장 dylib에는 encode/decode 심볼이 있지만 직접 ABI 호출은 이번 경로가 아니다.

- [EditorUtility.CompressTexture](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/EditorUtility.CompressTexture.html)
- [플랫폼별 압축 형식과 미지원 시 RGBA 해제](https://docs.unity3d.com/6000.6/Documentation/Manual/texture-choose-format-by-platform.html)
- [형식별 payload 비트 수](https://docs.unity3d.com/6000.6/Documentation/Manual/texture-formats-reference.html)

## 표본 검토와 공개 납품

독립 검토2건: 실제 전투 frame0 원본/rgba/4/6과 기존 셀 시트8장을 확인했다. 선정 표본에서 새 사각·명백한 halo·실루엣 단절·다리 누락은 보이지 않았으며 ASTC6 확대 얼굴·천·갑옷·날개가 더 부드럽고 ASTC4가 원본에 가깝다는 관찰이다. 사용자 품질 승인이나 전체 연속 걷기 통과는 아니다.

셀 시트의 1x는 native atlas crop COMPOSITE이지 390px 게임 화면1배가 아니다. 3x는 첫 접지 위상만이다. 실제 전투 개별 캡처는720×1560이며 비교 합성보드/반복 영상은 drawtext 실패 후 미완료로 남긴다. 새로운 이미지 생성·대체 후처리는 하지 않았다.

선정한4role×1x/3x와 실제 전투 frame0 원본/ASTC4/ASTC6, 작은 수치표만 [검토 Release](https://github.com/hyunlord/bs-mobile/releases/tag/astc-texture-trial-461-20261028)에 공개한다. 전체 성공 원자료는 로컬 보존한다. 수치 정본은 [CSV](texture-astc-trial-460.csv)이며 동일 atlas/manifest와 summarize 명령으로 재생성한다.

## #431 병합 후 현재 최대3개 보완

main `2074f57478f56707d91094a3fa6412bd033468db`에서 estate-decor가18.1875MiB로 커졌다. 현재 최대3개는 hero-knight21.1658 / estate-decor18.1875 / fallow-enemy-rigs18MiB다. 앞의437d62a 전투3개 시험은 역사 결과로 보존하며 legacy hero-b는 보조 표본이다. 감사#460에서 기존 hero/enemy 원본·meta 바이트 동일성을 확인했으므로 압축 수치를 재사용하지만, 다른 시점의 전체 Editor 객체 메모리를 합치지는 않는다.

source `7c1c3b0`에서 `ASTC_TRIAL_TARGET=estate-decor`를 명시하여 고정 경로 `Assets/Resources/EstateArt/estate-decor.png`만 읽었다. 다른 값·빈값은 전체 시험에서 거부한다(16px probe-only early return은 대상 선택 전이므로 전모드 failclosed를 주장하지 않는다). 미지정 기본3개와 순서는 그대로다. 영지 atlas는 전투 registry에 끼우지 않았다. rgba/4/6 실제 압축·GPU decode 각1pass이며, RGBA 전체 채널 왕복오차0이다. 실제 영지 renderer 캡처는 **NOT_RUN**이다.

| 대상2048×2328 | RGBA | ASTC4 | ASTC6 |
| --- | ---: | ---: | ---: |
| raw payload bytes | 19070976 | 4767744 | 2123136 |
| alpha MAE | 0 | 0.347283 | 0.744500 |
| alpha 최대 오차 | 0 | 106 | 143 |
| 원본투명→비영alpha 픽셀 | 0 | 9408 | 18805 |
| 역할별 padding | NOT_MEASURED | NOT_MEASURED | NOT_MEASURED |

estate-art는 전투 role/frame padding 계약이 아니므로 0샘플을 통과로 표기하지 않는다. Release에는 새 합성 대신 전체 원본/ASTC4decode/ASTC6decode PNG3장과 정본CSV 추가3행만 올렸다. 현재 최대3개 payload 합계57.3533→14.3383/6.3863MiB는 블록 크기 비교이며 모바일/GPU 실행메모리가 아니다. 원본·제품 importer는 변경하지 않았다.

최종7c1c3b0 재검증: first-playable EditMode893/PlayMode55, wave-1a903/88, 실패0. 원명령exit1/입력없는공통skip2 기록을 보존하고 기존생성도구 보완검사각1pass 후0unexplained/1listed로 확인했다. 추적파일변화0·소유잠금해제. Node수치검사4/4, boundariesPASS.

추가 독립 시각 관찰: estate 전체atlas 원본/4/6의 축소 표본에서 새사각·윤곽단절·후광증가가 눈에 띄지 않았다. 상단빨강/노랑영역·줄무늬·주민어두운패딩은 original에도 있으며 압축신규결함으로 판정하지 않는다. 실제알파합성과 미세질감 우열은 이전체atlas 표시만으로 검증할 수 없다.
