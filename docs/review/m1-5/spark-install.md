관문: 부분 — (a) 원본 정면의 전체 메시 생성은 exit0으로 완료했다. 실제 렌더는 진행 중이며 메시 품질·리깅·모션·사용자 영상 승인은 아직 없다. (b)는 보류한다.

# M1-5 Spark 설치 기록

확인일 2026-10-14. 추적 [#262](https://github.com/hyunlord/bs-mobile/issues/262). [라이선스와 제작 계약](../../research/m1-5-free-trellis.md).

## 실제 환경과 격리

DGX Spark / aarch64 / NVIDIA GB10(SM12.1) / 드라이버580.126.09 / CUDA toolkit13.0.88 / Python3.12.3. `/home/hyunlord/work/bs-mobile-m15-trellis-262`의 별도 source·extensions·.venv·logs 사용. CPU 컴파일 `nice -n15`, `MAX_JOBS=2`. 기존 GPU 작업은 변경하지 않았다. 공개 GitHub runner 연결 없음.

Microsoft SHA `75fbf0183001ed9876c8dbb35de6b68552ee08bd`, Eigen submodule `21e4582d1739107337a03460c81412981130373e`. 공식 setup.sh를 그대로 실행하지 않았다. x86용 Pillow-SIMD 대신 Pillow, CUDA12.4 기본 조합 대신 공식 ARM64 CUDA13 wheel을 사용했다.

| 검사 | 결과 | 한계 |
| --- | --- | --- |
| 독립 clone·submodule·venv | 성공 | 시스템 Python/다른 프로젝트 환경 변경 없음 |
| torch2.9.1+cu130 / torchvision0.24.1 | 설치·import 성공 | SM12.1 장치에 대해 wheel의 최대 아키텍처12.0 경고 발생 |
| CUDA 정수 tensor 연산 | 성공 | 0..15 제곱 후 synchronize·CPU 결과 일치. 전체 추론 성능/정확도 증거 아님 |
| nvdiffrast0.4.0 역사 호환성 평가 | 16×16 삼각형 covered32 | NVIDIA 비상업 조건 확인 후 자산 제작 경로에서 제외 |
| FlashAttention2.8.4 | ARM64 소스 빌드·dense FP16·varlen BF16/h128 CUDA 검사 통과 | 97/97 objects, 빌드165분54초; 전체 추론과 구분 |
| CuMesh MIT core·xatlas | `_cubvh` 제외 빌드·3정점/1면 GPU 커널 성공 | 전체 메시 추론·내보내기 증거 아님 |
| FlexGEMM·O-Voxel 등 나머지 확장 | 실행 담당자가 설치·기초 검사 통과 확인 | FlashAttention·전체 추론·생성 품질은 별도 |
| DINOv3 가중치 | HF 접근 승인 후 1,212,559,808바이트 다운로드 성공 | revision `ea8dc2863c51be0a264bab82070e3e8836b02d51`; 설정 파일 수집·로컬 DINOv3ViTModel 로딩 성공 |
| (a) 전체 메시 생성 | 2026-10-14 00:06 KST exit0, seed42·1024_cascade | 3,281,203정점·6,645,292면, simplify=false. 품질 승인이 아님 |
| 실제 렌더·손/검/얼굴 품질·Mixamo·굽기 | 렌더 진행 중, 품질 판정·리깅 없음 | 기본 RMBG-2.0 제외. (b) A-pose 입력은 보류 |

## 재현 명령

프로젝트 전용 원격 폴더 안에서만 실행한다. `uv` 실행 파일 위치는 호스트 환경에 맞춘다. 모델 가중치·인증값·FBX는 이 저장소에 넣지 않는다.

```sh
uv venv --python 3.12 .venv
uv pip install --python .venv/bin/python torch==2.9.1 torchvision==0.24.1 --index-url https://download.pytorch.org/whl/cu130
uv pip install --python .venv/bin/python ninja packaging wheel psutil
CUDA_HOME=/usr/local/cuda PATH=/usr/local/cuda/bin:$PATH MAX_JOBS=2 NVCC_THREADS=1 FLASH_ATTENTION_FORCE_BUILD=TRUE FLASH_ATTN_CUDA_ARCHS=121 nice -n 15 uv pip install --python .venv/bin/python --no-build-isolation ./extensions/flash-attention
```

nvdiffrast tag v0.4.0, FlashAttention SHA `1fc0221183e799363001394a12dd9e78e15f5dda`. 로그는 동일 폴더 `logs/torch-install.log`, `torch-smoke.log`, `torch-kernel.log`, `nvdiffrast-install.log`, `nvdiffrast-smoke.log`, `flash-install.log`에 남긴다. 임포트·커널·전체 추론을 합쳐 성공이라 부르지 않는다.

## 전처리·오프라인 준비 실측

- MIT BiRefNet revision `e2bf8e4460fc8fa32bba5ea4d94b3233d367b0e4`를 고정하고 safetensors·로컬 코드 검토 후 Mac CPU에서 실행 성공. 원화 RGB를 다시 그리지 않고 알파만 추가했다.
- 역사 표본: `heroes.png` SHA256 `332d3abbaab9627848369fab9391354a0853e4b2e250c26e3fab635a19093028`, 첫 A 기사 crop `(0,0,270,480)`, 결과 270×480 RGBA. 당시 검·망토·얼굴과 알파 미리보기를 확인했으나 현재 생성 입력으로 사용하지 않는다.
- 현재 원본: `docs/design/gdd/hero-concepts-v1/A-knight.png`, 1536×1024, SHA256 `b6f834a6cce6a73db14373cffd68b3cd90bff10c6aa4e3e13078b68e150cf9d5`. xyxy `(0,15,532,925)` → 532×910 정면 crop에 BiRefNet 알파 전처리 성공. 원본 RGB를 다시 그리지 않는다.
- BiRefNet 가중치 SHA256 `9ab37426bf4de0567af6b5d21b16151357149139362e6e8992021b8ce356a154`. 모델·원화·투명 PNG는 로컬 작업물이며 Git에 넣지 않는다.
- 공개 TRELLIS 가중치 8개를 받았고 safetensors 헤더·마지막 data offset·파일 크기 일치를 확인했다. `logs/public-weight-integrity.json`에 목록 보존. 이후 DINO 접근 승인을 받아 고정 revision의 가중치 1,212,559,808바이트도 다운로드했다. DINO 설정 파일 수집 및 로컬 DINOv3ViTModel(303,129,600 parameters) 로딩 성공. 로딩 검사와 아래의 전체 메시 생성 결과를 구분한다.
- 파일럿 설정은 로컬 DINO 경로·`rembg_model=null`을 사용한다. 로더는 null 배경 제거기를 허용하고 알파 없는 입력은 거부하도록 격리된 upstream checkout에 수정했다. 기본 RMBG-2.0 자동 다운로드는 실행하지 않는다.
- 생성 스크립트는 seed42·1024_cascade로 raw PLY와 정점 색을 산출했다. simplify=false이며 nvdiffrast/nvdiffrec/cubvh 및 공식 PBR/GLB 후처리는 호출하지 않았다. Blender4.5.14 LTS의 실제 메시 렌더는 진행 중이다.
- ARM64 선택적 moderngl/glcontext 설치는 `X11/Xlib.h` 부재로 실패했다. 메시 추론에서 필요하지 않은 선택 모듈로 제외했으며 시스템 패키지를 변경하지 않았다.

## 다음 경계

DINOv3 게임 사용 동의·HF 접근 승인은 완료됐다. 승인된 접근으로 다운로드했으며 gated 파일 우회는 하지 않았다. FlashAttention은 97/97 컴파일 및 CUDA 검사를 마쳤다. 이전 29/97·56/97 관측은 당시 진행 기록이다. 낮은 우선순위 실행과 로그를 유지하며 실패한 경합 시도의 증거도 삭제하거나 성공으로 바꾸지 않는다.

현재 (a) 원본 정면 메시만 생성·검토하고 (b) A-pose는 **보류**한다. (a)의 **실제 알파 입력·정면·측면·후면과 손·검·얼굴 확대를 먼저 제출하고 사용자 메시 승인 후 Mixamo**로 간다. 검은 손 본 별도 소품, 망토는 보조 본 또는 단순화 계획을 메시 검토에 포함한다. 승인 지연 때 허용했던 무료 기성 캐릭터 B 선행 시험은 역사 결정이며 접근 대기를 A 품질 실패로 세지 않는다. Mixamo 로그인·다운로드는 사용자 담당이고 FBX는 비공개 로컬에만 둔다. 이전 M1-4 실패와 PR261 초안을 유지한다.

## 2026-10-13 23시 후속 실행

- 최종 확장 검증 exit0(23:05:56 KST). CuMesh·O-Voxel·FlexGEMM·FlashAttention CUDA 검사와 제한 모듈을 막은 이미지 파이프라인 import 통과.
- (a) 첫 생성은 DINOv3 내부 API 경로에서 `AttributeError: DINOv3ViTModel has no attribute layer`로 실패. Transformers5.19.0은 encoder를 `model.model.layer` 아래에 둔다. 한 줄 경로 수정 뒤 같은 입력의512/1024 특징 추출과 기존 비학습 layer_norm 의미 일치 검사 통과. 모델 가중치·원화는 변경하지 않았다.
- 실패 로그·빈 출력 폴더는 `logs/failures/dino-api/`, `outputs/a-highres-42-dino-api-failure`로 보존했다. 같은 seed42·고해상도 입력으로 재시작했으며 메시 산출·품질은 아직 판정하지 않는다.

사용자 후속 지시: (b)는 승인 대기가 아니라 **보류**. (a)만 생성·검토한다. A-pose 그림과 전처리 시도는 역사 작업물로 남기고 3D 생성에 사용하지 않는다.

아래도 동일 (a) 입력의 실패·수정 기록으로 보존한다.

- CPU 로드 뒤 가중치 파일 캐시 때문에 CUDA 가용19.55→2.66GB. 프로젝트 소유 safetensors만 `POSIX_FADV_DONTNEED` 처리해17.41GB를 확보했고 전체 모델 유지 상태의 DINO512 실행 통과. 시스템 전체 캐시·다른 프로세스·dtype는 변경하지 않았다.
- Triton 번들 PTXAS12.8의 `sm_121a` 미지원 오류는 `TRITON_PTXAS_PATH=/usr/local/cuda/bin/ptxas`(13.0.88)로 해결. 실제 Spark FP16 희소 합성곱 결과 일치 통과. 아키텍처를 다른 장치로 속이지 않았다.
- 두 실패는 `logs/failures/dino-cuda-oom`, `logs/failures/triton-ptxas`와 각 빈 출력 폴더로 보존한다. 각 부분 검사 통과를 메시 생성 성공으로 확대하지 않는다.

## 2026-10-14 00:06 (a) 전체 생성 결과

- 캐시 반환 전에 진단용 `CUDA mem_get_info`를 호출하는 순서에서도 초기화 OOM이 발생했다. 프로젝트 소유 safetensors 캐시를 먼저 반환한 뒤 조회하도록 순서를 고쳤다. 실패 기록은 `logs/failures/cache-probe-init-oom`에 보존한다. 시스템 전체 캐시나 다른 작업을 정리한 것이 아니다.
- 같은 (a) RGBA 입력 SHA256 `b90ed14717bfe9e08f829700a203419a94ecb936cbf51f0a6d4a1e06ba50f56d`, seed42, `1024_cascade`로 전체 추론 exit0을 확인했다.
- raw PLY는 정점 색 포함 약133MiB, 정점3,281,203개·면6,645,292개다. `simplify=false`로 산출한 원시 메시이며 게임용 최적화·리깅 가능성·손/검/얼굴 품질을 증명하지 않는다.
- 실제 렌더는 진행 중이다. 정면·측면·후면·부위 확대 결과와 사용자 메시 판정은 별도로 추가한다. (b) 생성이나 Mixamo 리깅은 수행하지 않았다.

## (a) 실제 메시 시각 검토 — 2026-10-14

원본 PLY SHA256 `8b6e32f2ddf57a3f9b92f9e39ea7bc3bd44a741f11476ada61c2a6af08faac93`. Blender4.5.14 LTS에서 원본 정점 색·기하 그대로 정면/측면/뒤 및 얼굴/손/검을 렌더해 직접 확인했다. 감소·스무딩·리토폴로지·리깅·텍스처 보정은 하지 않았다. 표본 선정은 사용자 요청의 세 방향과 세 부위이며, [검토 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m1-5-mesh-a-seed42)에 입력 알파와 검토 시트를 둔다. 원시 고밀도 메시는 공개 자산으로 업로드하지 않는다.

- 전체 기사 실루엣·파란 망토·상아색 옷·검은 식별된다. 원화에 없는 후면은 생성 추정이며 정본 디자인 승인을 뜻하지 않는다.
- 얼굴 확대에서 홍채·동공이 선명하지 않고 눈 주변이 뭉개져 보인다. 손가락도 일부 덩어리로 보인다.
- 망토는 팔을 가리고 측후면에 찢김/구멍처럼 보이는 부분이 있다. 렌더 관찰이며 위상 검사를 대신하지 않는다.
- 검은 형상이 보이나 손과 별도 연결 성분인지 미확인. 승인 후 별도 소품 분리와 손 본 부착을 검토해야 한다.
- 망토는 승인 후 몸통과 분리·단순화하고 보조 본으로 흔들림을 주는 안을 권고한다. 원본 메시에서 아직 수행하지 않았다.

관문: **부분 — (a) 생성·실제 렌더 제출 완료, 사용자 메시 품질 승인 대기.** (b) 보류, Mixamo 미실행. PR261·263 초안과 모든 보류 항목을 유지한다. 이번 문서 CI는 게임·애니메이션 품질을 검증하지 않는다.
