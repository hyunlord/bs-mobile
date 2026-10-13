관문: 부분 — 무료 경로의 배경 제거·ARM64 CUDA 기반 검사를 마쳤고 FlashAttention은 컴파일 중이며 메시·모션·영상 승인은 아직 없다.

# M1-5 Spark 설치 기록

확인일 2026-10-13. 추적 [#262](https://github.com/hyunlord/bs-mobile/issues/262). [라이선스와 제작 계약](../../research/m1-5-free-trellis.md).

## 실제 환경과 격리

DGX Spark / aarch64 / NVIDIA GB10(SM12.1) / 드라이버580.126.09 / CUDA toolkit13.0.88 / Python3.12.3. `/home/hyunlord/work/bs-mobile-m15-trellis-262`의 별도 source·extensions·.venv·logs 사용. CPU 컴파일 `nice -n15`, `MAX_JOBS=2`. 기존 GPU 작업은 변경하지 않았다. 공개 GitHub runner 연결 없음.

Microsoft SHA `75fbf0183001ed9876c8dbb35de6b68552ee08bd`, Eigen submodule `21e4582d1739107337a03460c81412981130373e`. 공식 setup.sh를 그대로 실행하지 않았다. x86용 Pillow-SIMD 대신 Pillow, CUDA12.4 기본 조합 대신 공식 ARM64 CUDA13 wheel을 사용했다.

| 검사 | 결과 | 한계 |
| --- | --- | --- |
| 독립 clone·submodule·venv | 성공 | 시스템 Python/다른 프로젝트 환경 변경 없음 |
| torch2.9.1+cu130 / torchvision0.24.1 | 설치·import 성공 | SM12.1 장치에 대해 wheel의 최대 아키텍처12.0 경고 발생 |
| CUDA 정수 tensor 연산 | 성공 | 0..15 제곱 후 synchronize·CPU 결과 일치. 전체 추론 성능/정확도 증거 아님 |
| nvdiffrast0.4.0 역사 호환성 평가 | 16×16 삼각형 covered32 | NVIDIA 비상업 조건 확인 후 자산 제작 경로에서 제외 |
| FlashAttention | 소스 빌드 검사 중 | 별도 결과 추가 전 호환 성공 주장 금지 |
| CuMesh MIT core·xatlas | `_cubvh` 제외 빌드·3정점/1면 GPU 커널 성공 | FlexGEMM/O-Voxel 설치와 전체 추론은 별도 대기 |
| 모델 생성·손/검/얼굴 품질·Mixamo·굽기 | 미실행 | DINOv3 사용자 게임 사용 동의 완료, HF 접근 승인 대기; 기본 RMBG-2.0 제외 |

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
- 승인 원본 `heroes.png` SHA256 `332d3abbaab9627848369fab9391354a0853e4b2e250c26e3fab635a19093028`, 첫 A 기사 crop `(0,0,270,480)`, 결과 270×480 RGBA. 검·망토·얼굴과 알파 미리보기를 직접 확인했다.
- BiRefNet 가중치 SHA256 `9ab37426bf4de0567af6b5d21b16151357149139362e6e8992021b8ce356a154`. 모델·원화·투명 PNG는 로컬 작업물이며 Git에 넣지 않는다.
- 공개 TRELLIS 가중치 8개를 받았고 safetensors 헤더·마지막 data offset·파일 크기 일치를 확인했다. DINOv3 파일은 없음. `logs/public-weight-integrity.json`에 목록 보존.
- 파일럿 설정은 로컬 DINO 경로·`rembg_model=null`을 사용한다. 로더는 null 배경 제거기를 허용하고 알파 없는 입력은 거부하도록 격리된 upstream checkout에 수정했다. 기본 RMBG-2.0 자동 다운로드는 실행하지 않는다.
- 생성 준비 스크립트는 seed42·1개 raw PLY·정점 색을 목표로 한다. 아직 실행하지 않았다. nvdiffrast/nvdiffrec/cubvh 및 공식 PBR/GLB 후처리는 호출하지 않는다. Blender4.5.14 LTS로 실제 메시를 렌더할 예정이다.
- ARM64 선택적 moderngl/glcontext 설치는 `X11/Xlib.h` 부재로 실패했다. 메시 추론에서 필요하지 않은 선택 모듈로 제외했으며 시스템 패키지를 변경하지 않았다.

## 다음 경계

DINOv3 게임 사용 동의는 완료됐고 HF 접근 승인은 대기한다. gated 파일 우회는 하지 않는다. FlashAttention은 2026-10-13 21시대 기준 97개 object 중 약29개가 컴파일돼 진행 중이며 성공이나 실패로 확정하지 않는다. 낮은 우선순위의 원격 빌드와 로그를 유지한다.

메시가 생성되면 **정면·측면·후면과 손·검·얼굴 확대를 먼저 제출하고 사용자 승인 후 Mixamo**로 간다. 오늘 HF 승인이 오지 않으면 B 무료 기성 캐릭터의 파이프라인을 먼저 시험할 수 있으나, 접근 대기는 A 품질 실패가 아니다. Mixamo 로그인·다운로드는 사용자 담당이고 FBX는 비공개 로컬에만 둔다. 이전 M1-4 실패와 PR261 초안을 유지한다.
