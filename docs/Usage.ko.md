# 사용법

## 1. 프로젝트에 넣기

**DLL 참조**: `VisionImageViewer.dll`을 참조에 추가합니다. OpenCvSharp를 쓰면 `VisionImageViewer.OpenCvSharp.dll`도 추가합니다.

**소스 참조**: 이 저장소를 서브모듈로 넣고 `src/VisionImageViewer/VisionImageViewer.csproj`를 솔루션에 추가한 뒤 프로젝트 참조를 겁니다. 뷰어를 고치면서 쓸 때 편합니다.

**디자이너**: 소스 참조로 넣은 경우 빌드하면 도구 상자에 `ImageViewer`가 나타나 폼에 끌어다 놓을 수 있습니다.
DLL만 참조한 경우에는 도구 상자 우클릭 → 항목 선택 → 찾아보기에서 `VisionImageViewer.dll`을 지정합니다.
(디자이너에 끌어다 놓는 동작은 이 저장소에서 확인하지 않았습니다. 데모와 검증 프로그램은 코드로 만듭니다.)

```csharp
using VisionImageViewer;
```

## 2. 만들기

```csharp
// 기본값: 검정 배경, 화면 맞춤
var viewer = new ImageViewer();

// 배경색 등 초기 설정 지정
var viewer = new ImageViewer(new ImageViewerOptions
{
    BackColor = Color.FromArgb(40, 40, 40),
    ViewMode = ViewMode.Fit,
    InspectionMode = false
});
viewer.Dock = DockStyle.Fill;
panel.Controls.Add(viewer);
```

디자이너로 올린 경우에는 속성 창의 `BackColor`와 `Viewer` 분류 속성으로 같은 설정을 합니다.

## 3. 영상 넣기

`SetImage`는 **어느 스레드에서든** 호출할 수 있습니다. 뷰어가 영상을 자기 버퍼로 복사한 뒤 바로 돌아오므로,
호출이 끝나면 원본 버퍼를 해제하거나 다음 프레임에 재사용해도 됩니다.

```csharp
// 원시 버퍼 (카메라 SDK, 영상 라이브러리 공통)
viewer.SetImage(pointer, width, height, stride, ViewerPixelFormat.Mono8);

// 관리 배열 (Basler pylon의 PixelData 등)
viewer.SetImage(byteArray, width, height, stride, ViewerPixelFormat.Mono8);

// R, G, B 평면이 따로인 컬러 (MIL, HALCON 기본 배치)
viewer.SetImagePlanar(redPointer, greenPointer, bluePointer, width, height, stride);

// GDI+ Bitmap
viewer.SetImage(bitmap);

// OpenCvSharp Mat (어댑터 DLL 필요)
viewer.SetImage(mat);
viewer.SetImage(mat, ViewerPixelFormat.Mono12);   // 16bit Mat에 12bit 영상이 담긴 경우
```

| 픽셀 포맷 | 바이트/픽셀 | 설명 |
|---|---|---|
| `Mono8` | 1 | 8bit 흑백 |
| `Mono10`, `Mono12`, `Mono16` | 2 | 10/12/16bit 흑백. 화면에는 상위 8bit가 보이고 원본 값은 보존 |
| `Bgr24` | 3 | B,G,R 순서 (OpenCV, GDI 기본) |
| `Rgb24` | 3 | R,G,B 순서 |
| `Bgra32` | 4 | B,G,R,A 순서. 알파는 무시 |

- `stride`는 한 줄의 바이트 수(패딩 포함)입니다. 아래에서 위로 저장된 버퍼는 마지막 줄 주소와 음수 stride를 넘깁니다.
- Bayer 영상은 카메라 SDK의 변환기나 OpenCV로 먼저 BGR로 바꾼 뒤 넣습니다.
- 인자가 잘못되면(크기 0 이하, stride 부족, 배열이 작음) 예외가 납니다.

### 대표 흐름: 라이브 뷰어 + 검사 결과 뷰어

뷰어를 두 개 올리고 카메라 스레드에서 이렇게 씁니다.

```csharp
// 카메라 취득 스레드 (예: Basler 콜백)
using (Mat raw = ...)                            // 카메라 버퍼 → Mat
using (Mat pre = Preprocess(raw))                // OpenCvSharp 전처리
{
    liveViewer.SetImage(pre);                    // 라이브 뷰어
    InspectionResult r = Inspect(pre);           // 검사
    resultViewer.SetImage(pre, ToOverlays(r));   // 결과 뷰어: 영상 + 불량 표시
}                                                // 여기서 Mat을 해제해도 뷰어는 안전
```

`demo/VisionImageViewer.Demo/MainForm.cs`의 `CameraLoop`가 이 흐름을 그대로 구현한 예입니다.

## 4. 화면 조작

| 조작 | 동작 |
|---|---|
| 마우스 휠 | 마우스 위치를 기준으로 확대/축소 |
| `+` / `-` 키 | 확대/축소 (마우스가 뷰어 위에 있으면 자동으로 포커스를 가져옴) |
| 왼쪽 버튼 드래그 | 확대 상태에서 화면 이동 |
| 스크롤바 | 확대된 영상이 컨트롤보다 커지면 자동 표시 |
| 우클릭 | 메뉴: 화면 맞춤 / 꽉 채움 / 원본 크기 / 확대 / 축소 / 배율 / 열기 / 저장 / 픽셀 격자 / 십자선 |

코드에서 조작할 때는 **UI 스레드에서** 호출합니다.

```csharp
viewer.ZoomToFit();                 // 화면 맞춤
viewer.ZoomToActual();              // 1:1
viewer.Zoom = 2.0;                  // 200% (화면 중앙 기준)
viewer.ZoomAt(4.0, new Point(x, y));// 컨트롤 좌표 (x, y)를 기준으로 400%
viewer.ViewMode = ViewMode.Fill;    // 비율 무시하고 꽉 채움
```

### 검사 모드

```csharp
viewer.InspectionMode = true;
```

| | 일반 | 검사 모드 |
|---|---|---|
| 휠, `+`/`-` 확대 | 가능 | **잠금** |
| 파일 열기 (메뉴, 끌어놓기) | 가능 | **잠금** |
| 우클릭 메뉴의 보기 전환 | 가능 | 가능 |
| 드래그 이동, 스크롤바 | 가능 | 가능 |

우클릭 메뉴까지 막으려면 `viewer.ContextMenuStrip = null;`로 떼어 냅니다. 다시 붙일 때는 `viewer.ContextMenuStrip = viewer.ViewMenu;`.

## 5. 오버레이

좌표는 **이미지 픽셀 기준**이며 (0,0)은 첫 픽셀의 왼쪽 위 모서리입니다. 확대·스크롤해도 영상을 따라 움직입니다.
선 두께와 글자 크기는 화면 픽셀 기준이라 확대해도 변하지 않습니다.

```csharp
var overlays = new List<OverlayItem>
{
    OverlayItem.Rect(new RectangleF(120, 80, 60, 40), Color.Red, 2F),            // 사각형
    OverlayItem.Rect(new RectangleF(300, 200, 50, 50), Color.Red, 2F, 60),       // 안쪽을 반투명으로 채움
    OverlayItem.Ellipse(new RectangleF(10, 10, 30, 30), Color.Lime),             // 타원
    OverlayItem.Line(new PointF(0, 0), new PointF(100, 100), Color.Yellow),      // 선
    OverlayItem.Cross(new PointF(256, 256), 40, Color.Cyan),                     // 십자
    OverlayItem.Polygon(points, Color.Orange),                                   // 다각형
    OverlayItem.Text(new PointF(120, 122), "불량 #1", Color.Yellow),             // 이미지 위치에 붙는 글자
    OverlayItem.Label("NG", Color.Red, ContentAlignment.TopLeft)                 // 화면 모서리에 고정되는 글자
};
viewer.SetImage(pointer, width, height, stride, ViewerPixelFormat.Mono8, overlays);
```

- 오버레이는 프레임과 한 묶음입니다. 그 프레임이 그려질 때 같이 그려지므로 박스가 다른 프레임 위에 나오지 않습니다.
- 넘긴 뒤 목록을 고쳐도 이미 넘긴 프레임에는 영향이 없습니다.
- 직접 그리고 싶으면 `Paint` 이벤트에서 `viewer.ImageToClient(point)`로 좌표를 바꿔 그립니다.

## 6. 픽셀 좌표와 값

```csharp
viewer.PixelHover += (s, e) =>
{
    statusLabel.Text = e.IsInsideImage ? $"({e.X}, {e.Y}) = {e.Value}" : "";
};

PixelValue value;
if (viewer.TryGetPixel(100, 50, out value)) { /* value.V0 = 흑백 값 또는 R, V1 = G, V2 = B */ }
```

16bit 영상은 화면 표시와 무관하게 **원본 16bit 값**이 나옵니다.

## 7. 파일 열기/저장

```csharp
if (!viewer.LoadFile(path)) { MessageBox.Show(viewer.LastFileError); }
viewer.SaveFile(path, includeOverlays: false);   // 원본만
viewer.SaveFile(path, includeOverlays: true);    // 박스·글자 포함
using (Bitmap bitmap = viewer.CaptureBitmap(true)) { ... }

viewer.AllowDrop = true;                          // 파일을 끌어다 놓아 열기
```

| | 열기 | 저장 |
|---|---|---|
| 코어만 | bmp, jpg, png, gif, tif(8bit), ico | png, bmp, jpg, tif, gif |
| + OpenCvSharp 어댑터 (`OpenCvFileDecoder.Register()`) | 위 + 16bit tif, webp, jp2, pgm/ppm/pbm 등 OpenCV가 읽는 형식 | 동일 |

- 한글 경로를 지원합니다.
- 지원하지 않는 형식: 카메라 RAW(CR2 등), PSD, HEIC, SVG. 여러 장짜리 TIFF는 첫 장만 엽니다.
- 16bit 영상을 저장하면 화면에 보이는 것과 같은 8bit로 저장됩니다. 원본 16bit 저장은 호출하는 쪽의 영상 라이브러리로 하세요.
- 저장되는 것은 **지금 화면에 표시된 프레임**입니다.

## 8. 옵션

| 속성 | 기본값 | 설명 |
|---|---|---|
| `BackColor` | 검정 | 영상 바깥 배경색 |
| `ViewMode` | `Fit` | `Fit`(비율 유지 맞춤) / `Fill`(꽉 채움) / `Actual`(1:1) / `Custom`(지정 배율) |
| `MinZoom`, `MaxZoom` | 0.02, 64 | 배율 한계 |
| `ZoomStep` | 1.2 | 휠 한 칸, `+`/`-` 한 번의 배율 |
| `InspectionMode` | false | 휠·키 확대와 파일 열기 잠금 |
| `ZoomOutQuality` | `Fast` | 축소 화질. `Fast`는 픽셀을 건너뜀, `Smooth`는 평균(느림) |
| `DisplayPolicy` | `LatestWins` | `LatestWins`(최신 우선) / `Queue`(순서대로 모두) |
| `QueueCapacity` | 8 | `Queue`에서 쌓아 둘 최대 프레임 수 |
| `MaxDisplayFps` | 0 | 초당 최대 화면 갱신. 0이면 따로 제한하지 않음 |
| `FocusOnMouseEnter` | true | 마우스가 올라오면 포커스 가져오기 |
| `PanWithMouseDrag` | true | 드래그로 화면 이동 |
| `ShowPixelGrid` | false | 8배 이상 확대 시 픽셀 경계선 |
| `ShowCrosshair` | false | 이미지 중심 십자선 |
| `Strings` | 한국어 | 메뉴·안내 문구 |

### 축소 화질 주의

`Fast`는 축소할 때 픽셀을 건너뛰므로 **1픽셀짜리 선이나 점 결함이 축소 화면에서 안 보일 수 있습니다.**
결함 확인은 확대해서 하거나 `Smooth`를 쓰세요. `Smooth`는 원본 전체를 평균하므로 큰 영상에서 느립니다(고속 라이브에는 부적합). 16bit 영상에는 적용되지 않습니다.

### 표시 정책

- `LatestWins`: 화면이 그려지기 전에 새 프레임이 오면 이전 프레임을 건너뜁니다. 지연이 쌓이지 않고 마지막 프레임은 반드시 표시됩니다. 라이브에 적합합니다.
- `Queue`: 짧은 시간에 여러 장이 몰려 들어와도 순서대로 모두 보여 줍니다. 큐가 가득 차면 가장 오래된 것부터 버립니다. 프레임 크기 × `QueueCapacity`만큼 메모리를 더 씁니다.

## 9. 통계

```csharp
ViewerStatistics s = viewer.Statistics;   // 어느 스레드에서든 읽기 가능
// s.Received  받은 프레임 수
// s.Displayed 화면에 그린 프레임 수
// s.Skipped   더 새 프레임에 밀려 그리지 않은 수
// s.AverageCopyMs / s.AveragePaintMs  복사·그리기 평균 시간
viewer.ResetStatistics();
```

`Skipped`가 계속 늘면 입력이 화면 갱신보다 빠른 것입니다. 검사 파이프라인에는 영향이 없습니다.

## 10. 다국어

```csharp
viewer.Strings = new ViewerStrings
{
    Fit = "Fit to window", Fill = "Stretch", Actual = "Actual size (1:1)",
    ZoomIn = "Zoom in (+)", ZoomOut = "Zoom out (-)", Empty = "No image"
    // 나머지는 기본값
};
```

## 11. 주의 사항

- **UI 스레드에서만**: `Zoom`, `ViewMode`, `ZoomAt`, `TryGetPixel`, `CaptureBitmap`, `SaveFile`, `LoadFile`, `ImageSize`. (`SetImage`, `SetImagePlanar`, `ClearImage`, `Statistics`는 아무 스레드나 가능)
- **창이 최소화되거나 숨겨져 있으면** 그리지 않습니다. 그동안 받은 프레임은 최신 것 하나만 보관했다가 다시 보일 때 표시합니다.
- **메모리**: 뷰어 하나가 프레임 크기 × 3 정도를 씁니다(표시 중 / 대기 / 쓰는 중). 8192×4096 흑백이면 약 100MB입니다.
- **`SetImage` 직후에는 `ImageSize`·`TryGetPixel`이 이전 프레임 기준**입니다. 화면에 그려진 뒤(`FrameDisplayed` 이벤트) 바뀝니다.
- 뷰어를 `Dispose`한 뒤의 `SetImage` 호출은 조용히 무시됩니다. 카메라 스레드를 먼저 멈추지 않아도 예외가 나지 않습니다.
