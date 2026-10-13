관문: 부분 — 무료 경로의 ARM64 기반 설치를 확인 중이며 메시·모션·영상 승인은 아직 없다.

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
| nvdiffrast0.4.0 | 소스 빌드·GPU raster 성공 | 16×16 삼각형 결과 (1,16,16,4), covered32. 최종 툰 렌더 품질 검증 아님 |
| FlashAttention | 소스 빌드 검사 중 | 별도 결과 추가 전 호환 성공 주장 금지 |
| CuMesh/FlexGEMM/O-Voxel/nvdiffrec 전체 추론 | 미검증 | 기반 검사와 구분 |
| 모델 생성·손/검/얼굴 품질·Mixamo·굽기 | 미실행 | DINOv3 사용자 접근 동의 대기; 기본 RMBG-2.0 제외 |

## 재현 명령

프로젝트 전용 원격 폴더 안에서만 실행한다. `uv` 실행 파일 위치는 호스트 환경에 맞춘다. 모델 가중치·인증값·FBX는 이 저장소에 넣지 않는다.

```sh
uv venv --python 3.12 .venv
uv pip install --python .venv/bin/python torch==2.9.1 torchvision==0.24.1 --index-url https://download.pytorch.org/whl/cu130
uv pip install --python .venv/bin/python ninja packaging wheel psutil
CUDA_HOME=/usr/local/cuda PATH=/usr/local/cuda/bin:$PATH MAX_JOBS=2 TORCH_CUDA_ARCH_LIST=12.1 nice -n 15 uv pip install --python .venv/bin/python --no-build-isolation ./extensions/nvdiffrast
CUDA_HOME=/usr/local/cuda PATH=/usr/local/cuda/bin:$PATH MAX_JOBS=2 NVCC_THREADS=1 FLASH_ATTENTION_FORCE_BUILD=TRUE FLASH_ATTN_CUDA_ARCHS=121 nice -n 15 uv pip install --python .venv/bin/python --no-build-isolation ./extensions/flash-attention
```

nvdiffrast tag v0.4.0, FlashAttention SHA `1fc0221183e799363001394a12dd9e78e15f5dda`. 로그는 동일 폴더 `logs/torch-install.log`, `torch-smoke.log`, `torch-kernel.log`, `nvdiffrast-install.log`, `nvdiffrast-smoke.log`, `flash-install.log`에 남긴다. 임포트·커널·전체 추론을 합쳐 성공이라 부르지 않는다.

## 다음 경계

DINOv3 접근 약관 동의는 사용자가 직접 한다. 현재는 이를 대신 동의하거나 gated 파일을 우회해 받지 않는다. 설치 실패는 A 메시 품질 실패가 아니므로 아직 무료 B나 유료 후보로 넘어가지 않는다. 승인 원화 기반 메시가 준비된 뒤에만 Mixamo 사용자 로그인/리깅/다운로드를 요청한다. 새 영상은 생성하지 않았으며 이전 M1-4 영상의 실패 판정을 유지한다.
