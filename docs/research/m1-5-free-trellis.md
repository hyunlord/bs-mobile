# M1-5 무료 3D 제작 경로와 라이선스 경계

확인일: 2026-10-13 · 추적: [#262](https://github.com/hyunlord/bs-mobile/issues/262). 이 문서는 사용자 결정과 공식 출처 조사다. 설치·생성·리깅 성공 또는 사용자 영상 승인을 의미하지 않는다. 실제 설치 결과는 별도 검토 기록으로 남긴다.

## 결정과 진행 순서

예산은 **0원**이며 유료 기사 후보 조사를 중단한다. M1-4 AI 생성 2D 프레임 방식은 비율·디테일 불일치, 중간 동작 부족, 피격 전신 빨강 약 1초 때문에 실패했다. 기존 결과는 실패 기록으로 보존하고 PR261은 초안으로 둔다.

| 순서 | 제작 경로 | 다음 단계로 넘어가는 조건 |
| --- | --- | --- |
| 무료 A | 승인된 A 기사 원화 → Microsoft TRELLIS.2 메시 → 사용자 메시 승인 → Mixamo 자동 리깅·모션 → 2D 굽기 | 정면·측면·후면과 손·검·얼굴 확대를 먼저 제출. 품질 실패 시 증거를 남기고 B 비교 |
| 무료 B | Mixamo 무료 기성 기사형 캐릭터 → 같은 모션·렌더 조건 | A 메시 품질 실패 또는 2026-10-13 안에 DINO 접근 승인 미도착 시 파이프라인을 먼저 시험할 수 있음. 접근 대기는 A 품질 실패로 세지 않음 |
| 조건부 유료 | US$100 이하 후보 3개 조사 | 현재 미착수. 후보 제시 후 사용자 선택·승인 필요, 결제는 사용자 직접 |

직접 만든 메시로 대체하지 않는다. Hunyuan3D는 사용하지 않는다. DGX Spark ARM64의 기존 작업·환경을 보존한 별도 환경에서 낮은 우선순위로 설치를 시도한다. 막히면 정확한 컴파일·의존성 지점을 기록하고 설치 성공으로 포장하지 않는다. 공개 저장소 DGX 러너 연결·운영 SDK 추가는 하지 않는다.

최신 사용자 결정: DINOv3 약관 검토·게임 제작 사용 동의와 **HF 접근 승인이 모두 완료**됐다. 고정 revision `ea8dc2863c51be0a264bab82070e3e8836b02d51`의 가중치 1,212,559,808바이트 다운로드·설정 수집·로컬 로딩을 완료했다. 2026-10-14 00:06 KST에 원본 정면 (a)의 전체 메시 생성 exit0을 확인했으며 렌더·품질 판정은 별도다. 이전 승인 대기·무료 기성 캐릭터 B 선행 시험 조건은 당시 결정으로 보존한다. **A·B 둘 다 품질 실패해야 유료 조사 가능**이라는 조건은 유지한다.

### 현재 입력과 두 시제품 경로

- (a) 저장소의 원본 `docs/design/gdd/hero-concepts-v1/A-knight.png`(1536×1024, SHA256 `b6f834a6cce6a73db14373cffd68b3cd90bff10c6aa4e3e13078b68e150cf9d5`) 정면을 xyxy `(0,15,532,925)`로 잘라 532×910으로 사용했다. MIT BiRefNet 알파 입력 SHA256은 `b90ed14717bfe9e08f829700a203419a94ecb936cbf51f0a6d4a1e06ba50f56d`다. seed42·1024_cascade·simplify=false로 3,281,203정점·6,645,292면의 정점 색 raw PLY 약133MiB를 생성했다. 실제 렌더 진행 중이며 품질 승인·리깅 결과는 없다.
- (b) A-pose 입력은 최신 사용자 지시로 **보류**한다. 미리보기와 전처리는 역사로 보존하고 메시 생성에 사용하지 않는다. 현재 (a)만 진행한다.
- (a)의 실제 알파 입력과 메시 정면·측면·후면, 손·검·얼굴 확대를 제출한다. 검은 손 본에 붙일 별도 소품으로 취급하고, 망토는 별도 보조 본 또는 단순화 계획을 메시 검토 때 함께 제시한다. 사용자 메시 승인 전에 Mixamo로 넘기지 않는다.
- 이전 `heroes.png` 270×480 crop은 역사 전처리 표본이며 현재 생성 입력으로 사용하지 않는다. 설정·확장 설치와 실패한 경합 기록은 [Spark 실행 기록](../review/m1-5/spark-install.md)으로 구분한다. 이미지 자산·FBX를 이 문서 변경으로 추가하지 않는다.

## 사용자 Mixamo 단계

1. A 메시를 만들면 **정면·측면·후면과 손·검·얼굴 확대**를 먼저 제출해 사용자 승인을 받는다. 원화 승인·설치 성공·자동 비율 검사는 메시 승인이 아니다. 중립 자세·연결된 신체·단위·원점·불필요한 장면 객체도 확인한다.
2. **A 메시 사용자 승인 뒤** 업로드 파일이 준비된 시점에 Mixamo 작업을 요청한다. B 선행 시험은 위의 별도 조건을 따른다. 로그인·계정 동의·업로드 후 자동 리깅 확인·모션 다운로드는 사용자가 직접 처리한다. 계정 비밀번호나 토큰은 요청·기록하지 않는다.
3. 첫 시제품은 **걷기·베기·피격 3동작, 한 방향**이다. 사용자에게 필요한 파일 이름과 모션 선택·다운로드 설정을 구체적으로 안내하며, 원본 FBX는 로컬에서만 취급한다. **FBX 원본은 Git/Release에 올리지 않고 구운 스프라이트만 게임에 포함해 전달한다.** PNG로 굽는 것만으로 독립 에셋 재배포권이 생기지 않는다. 공개 검토는 영상·시트로 하고, 독립 atlas 공개는 [Adobe Content Files 조건 §3.6](https://www.adobe.com/legal/terms.html)을 별도로 확인한다.
4. 원화색(파란 망토·흰 겉옷·금발), 툰/셀 셰이딩·외곽선, 보폭과 이동 속도를 맞춰 2배 영상과 0.05초 간격 확대 시트를 제출한다. 피격은 흰 번쩍임 2프레임과 0.2–0.3초 움찔이며 전신 색칠은 금지다.
5. 매 프레임 머리/전신 비율 흔들림 ±3% 이내를 자동 검사하되, **사용자 영상 승인만 통과 판정**이다. 승인 후에만 나머지 동작과 8방향 실제 렌더·동작당 12–16프레임으로 확장한다. 회전·반전으로 방향을 대체하지 않으며 8방향 일정 입력·원 그리기 입력을 검증한다.

Adobe 공식 FAQ는 Adobe ID로 무료 이용, 캐릭터·애니메이션의 게임 포함 상업 프로젝트 사용을 허용한다. 자동 리깅은 이족 인간형이며 큰 소품·비대칭 자세·분리된 신체가 실패 원인이 될 수 있다. 이는 생성한 기사 메시의 리깅 성공을 보장하지 않는다. [Mixamo 공식 FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html).

## 코드와 모델을 따로 확인한 라이선스

Microsoft 공식 코드 SHA: `75fbf0183001ed9876c8dbb35de6b68552ee08bd`. 본체 모델 revision: `af44b45f2e35a493886929c6d786e563ec68364d`.

| 구성 | 확인된 조건 | 이번 경로 |
| --- | --- | --- |
| Microsoft TRELLIS.2 코드·본체 모델 | MIT. 사용·수정·상업 배포 가능, 저작권·허가 고지 유지 | 원본 SHA와 라이선스 보존 |
| `microsoft/TRELLIS-image-large` sparse decoder | HF 모델 카드 MIT | 필요한 decoder만 고정 |
| `facebook/dinov3-vitl16-pretrain-lvd1689m` | MIT가 아닌 DINOv3 별도 라이선스. Royalty-free 사용권과 용도·재배포 조건, HF gated 접근 | 사용자 동의·접근 승인, 고정 가중치·설정·로컬 로딩 완료. (a) 전체 추론 성공과 품질 승인은 별도 |
| 기본 `briaai/RMBG-2.0` | 비상업 라이선스, 상업 이용 별도 계약 | 예산 0 게임 제작에서 제외 |
| 대체 `ZhengPeng7/BiRefNet` | 공식 HF 카드 MIT. 고정 Python 소스 정적 검토 완료 | 동일 revision·safetensors·로컬 전용 전처리 성공. 메시 생성 품질은 별도 |
| nvdiffrast v0.4.0·nvdiffrec renderutils | 각각 LICENSE.txt §3.3 비상업 연구·평가 제한 | **상업 게임 자산 제작에서 제외**. 이름의 “1-Way Commercial”은 일반 사용자 상업 허가가 아님 |
| CuMesh의 `third_party/cubvh` | 최상위 MIT 외에 LICENSE_NVIDIA §3.3 비상업 제한 코드 포함 | **cubvh 제외**. MIT `_C`·xatlas 분리 설치·기초 검사 결과와 전체 추론/내보내기를 구별 |

원문: [Microsoft MIT](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/LICENSE), [모델 카드](https://huggingface.co/microsoft/TRELLIS.2-4B/blob/af44b45f2e35a493886929c6d786e563ec68364d/README.md), [sparse decoder 카드](https://huggingface.co/microsoft/TRELLIS-image-large/tree/25e0d31ffbebe4b5a97464dd851910efc3002d96), [DINOv3 라이선스](https://github.com/facebookresearch/dinov3/blob/6876159a11b4df116f30f667f8c9888617df0751/LICENSE.md), [DINOv3 접근](https://huggingface.co/facebook/dinov3-vitl16-pretrain-lvd1689m), [RMBG 조건](https://huggingface.co/briaai/RMBG-2.0), [BiRefNet 카드](https://huggingface.co/ZhengPeng7/BiRefNet/blob/e2bf8e4460fc8fa32bba5ea4d94b3233d367b0e4/README.md), [nvdiffrast](https://github.com/NVlabs/nvdiffrast/blob/v0.4.0/LICENSE.txt), [nvdiffrec](https://github.com/JeffreyXiang/nvdiffrec/blob/renderutils/LICENSE.txt).

본체 MIT는 모든 의존 모델이 MIT라는 뜻이 아니다. 기본 [pipeline.json](https://huggingface.co/microsoft/TRELLIS.2-4B/blob/af44b45f2e35a493886929c6d786e563ec68364d/pipeline.json)은 DINOv3와 RMBG-2.0을 지정한다. [로더](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/trellis2/pipelines/trellis2_image_to_3d.py#L104-L105)가 rembg를 즉시 생성하므로 투명 RGBA 입력만으로 RMBG 다운로드를 피하지 못한다. 검토한 MIT 대체 모델을 지정하거나 rembg 미생성 로더를 명시적으로 사용해야 한다. [BiRefNet 래퍼](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/trellis2/pipelines/rembg/BiRefNet.py)는 `trust_remote_code=True`이므로 원격 Python 코드도 실행 전에 검토한다. DINOv2로의 단순 교체는 학습 조건을 바꾸므로 동등한 대체로 취급하지 않는다.

BiRefNet 검토 대상은 HF revision `e2bf8e4460fc8fa32bba5ea4d94b3233d367b0e4`의 `birefnet.py`·`BiRefNet_config.py`·`config.json`이다. `bb_pretrained=false`가 별도 백본 `.pth`/pickle 로딩을 차단한다. 실제 백본 Swin-L 코드는 MIT, timm과 비활성 PVT 코드는 Apache2.0이다. 고정 코드에서 모듈 선택 `eval`은 내부 상수만 받지만 임의 설정 문자열에 노출하지 않는다. 네트워크 URL 입력이 있는 `handler.py`는 사용하지 않는다. `use_safetensors=True`, `local_files_only=True`, HF 오프라인 설정과 동일 revision의 로컬 모델만 사용한다. 정적 검토는 추론 성공이나 전체 학습 데이터 권리 검증을 의미하지 않는다. [고정 코드](https://huggingface.co/ZhengPeng7/BiRefNet/blob/e2bf8e4460fc8fa32bba5ea4d94b3233d367b0e4/birefnet.py), [고정 설정](https://huggingface.co/ZhengPeng7/BiRefNet/blob/e2bf8e4460fc8fa32bba5ea4d94b3233d367b0e4/config.json).

CuMesh는 [submodule 지정](https://github.com/JeffreyXiang/CuMesh/blob/main/.gitmodules)과 [cubvh LICENSE_NVIDIA](https://github.com/JeffreyXiang/cubvh/blob/trellis.2/LICENSE_NVIDIA)를 따로 확인한다. 제한 구성 요소의 과거 설치·import·커널 검사가 있더라도 **호환성 평가 기록**으로만 보존하며, 제작용 사용 허가나 승인된 자산 생산으로 표현하지 않는다. 제외 후 경로의 실제 설치·내보내기 검증은 별도 결과가 필요하다.

## Spark 설치 제약과 저장 공간

[Microsoft README·설치](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/README.md)는 Linux, GPU 메모리 24GB 이상, A100/H100 검증만 명시한다. Spark 설치 성공이나 H100과 같은 생성 시간을 보장하지 않는다. [기본 setup.sh](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/setup.sh)는 Python3.10·Torch2.6/cu124·FlashAttention2.7.3과 x86용 Pillow-SIMD를 설치한다. Spark에 그대로 실행하지 않는다.

- [NVIDIA 표](https://developer.nvidia.com/cuda/gpus)의 GB10은 SM12.1이다. [Spark 공식 스택](https://docs.nvidia.com/dgx/dgx-spark-porting-guide/porting/software-requirements.html)의 ARM64·CUDA13.0·통합 메모리와 현장 환경을 구별한다.
- README가 허용하는 `--new-env` 생략·Torch/CUDA 수동 구성 경로에서 ARM64 CUDA 동작을 먼저 확인한다. [Pillow-SIMD](https://github.com/uploadcare/pillow-simd)는 x86 SSE4/AVX2용이므로 일반 Pillow 대체가 필요하다.
- [공식 FlashAttention setup](https://github.com/Dao-AILab/flash-attention/blob/1fc0221183e799363001394a12dd9e78e15f5dda/setup.py)은 `FLASH_ATTENTION_FORCE_BUILD=TRUE`, `FLASH_ATTN_CUDA_ARCHS=121`, CUDA≥12.9의 소스 빌드 경로를 가진다. 실제 compile/import/attention 검사 전에는 성공으로 보고하지 않는다.
- CuMesh·FlexGEMM·O-Voxel은 소스·submodule SHA와 ABI를 고정해 검토하고, nvdiffrast·nvdiffrec·cubvh는 제작용 경로에서 제외한다. MIT `_C`·xatlas 분리와 제한 모듈의 간접 import 제거도 실행 검증 전에는 완료로 보고하지 않는다. 공식 setup도 공용 `/tmp/extensions`, sudo와 일부 고정되지 않은 의존 HEAD를 사용하므로 통째로 실행하지 않는다. 검토하지 않은 제3자 설치 스크립트는 실행하지 않는다.
- `ATTN_BACKEND=sdpa`는 전체 우회책이 아니다. [sparse config](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/trellis2/modules/sparse/config.py)는 xformers/flash_attn/flash_attn_3만 허용한다.

HF 공식 API `?blobs=true` 파일 메타데이터 합계이며 가중치를 내려받아 측정한 값이 아니다.

| 필요한 가중치 | 바이트 |
| --- | ---: |
| 기본 pipeline의 TRELLIS.2 본체 7파일(encoder 제외) | 14,819,870,530 |
| sparse decoder | 147,591,972 |
| DINOv3 ViT-L | 1,212,559,808 |
| 선택 대안 BiRefNet | 444,473,596 |
| 합계 | **16,624,495,906 / 약 15.48GiB** |

이는 환경·컴파일 임시 파일·캐시 중복·생성 결과를 제외한 하한이다. 전체 설치 요구 공간으로 표시하지 않는다. #126·#98·#224·1b 보류, SDK 미도입, Charter & Kin 변경 금지를 유지한다.
