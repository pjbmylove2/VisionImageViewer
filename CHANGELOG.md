# 변경 이력

버전은 [유의적 버전](https://semver.org/lang/ko/)을 따릅니다. 1.0.0 전까지는 공개 API가 바뀔 수 있습니다.

## 0.1.0 — 2026-11-11

첫 버전.

### 코어 (`VisionImageViewer`)
- `ImageViewer` WinForms 컨트롤. GDI 직접 렌더, .NET Framework 외 의존 없음.
- 입력: 원시 버퍼(`IntPtr`), `byte[]`, 평면 RGB, GDI+ `Bitmap`. 어느 스레드에서든 호출 가능.
- 픽셀 포맷: Mono8/10/12/16, Bgr24, Rgb24, Bgra32. 16bit는 원본 값 보존.
- 버퍼 풀로 프레임당 메모리 할당 없음. 표시 정책 `LatestWins`(기본) / `Queue`.
- 보기: 화면 맞춤 / 꽉 채움 / 1:1 / 지정 배율. 휠·`+`/`-`·우클릭 메뉴·드래그 이동, 자동 스크롤바.
- 검사 모드: 휠·키 확대와 파일 열기 잠금.
- 오버레이: 사각형, 타원, 선, 십자, 다각형, 글자, 화면 고정 라벨.
- 픽셀 좌표/값 이벤트, 통계(수신/표시/건너뜀, 복사·그리기 시간).
- 파일 열기(bmp, jpg, png, gif, 8bit tif, ico)·저장, 끌어놓기, 외부 디코더 훅.
- 축소 화질 `Fast` / `Smooth`, 픽셀 격자, 중심 십자선, 메뉴 문구 교체.

### 어댑터 (`VisionImageViewer.OpenCvSharp`)
- `viewer.SetImage(Mat)` 확장 메서드, OpenCV 파일 디코더(16bit tif, webp, pgm 등, 한글 경로).

### 도구
- 데모 앱(모의 카메라 → 전처리 → 라이브 / 검사 → 결과).
- 검증 프로그램 68항목, 성능 측정(`bench`), 장시간 메모리 확인(`soak`), 스레드 반복(`stress`).

### 알려진 제한
- Bayer 디모자이크 없음(카메라 SDK나 OpenCV로 변환 후 입력).
- ROI 드래그 편집 없음.
- 여러 장짜리 TIFF는 첫 장만 열림. 16bit 영상 저장은 8bit로 저장됨.
- MIL·HALCON·Cognex·eVision·EmguCV·Basler·Hikvision 연동 예제는 실행 검증하지 못함.
