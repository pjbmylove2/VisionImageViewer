# 타 라이브러리 연동

뷰어는 **첫 픽셀 주소, 가로, 세로, 줄 간격(stride), 픽셀 배치**만 있으면 어떤 영상이든 받습니다.
아래는 라이브러리별로 이 다섯 가지를 꺼내는 방법입니다.

| 구분 | 검증 상태 |
|---|---|
| OpenCvSharp | **이 저장소에서 빌드·실행 검증** (`tests/VisionImageViewer.Checks/AdapterChecks.cs`) |
| 그 외 전부 | **검증하지 못한 예제.** 작성 환경에 해당 SDK와 라이선스가 없어 빌드·실행을 해 보지 못했습니다. SDK 버전에 따라 이름이 다를 수 있으니 처음 붙일 때 SDK 문서와 대조하고, 색 순서와 위아래 방향을 눈으로 확인하세요. |

공통 주의:

- `SetImage`는 호출 중에만 버퍼를 읽습니다. 호출이 끝나면 버퍼를 해제하거나 재사용해도 됩니다.
- 콜백 스레드에서 바로 호출하면 됩니다. `Invoke`가 필요 없습니다.
- 색이 파랗게/붉게 뒤바뀌어 보이면 `Bgr24` ↔ `Rgb24`를 바꿉니다.
- 영상이 기울거나 줄무늬가 생기면 stride가 틀린 것입니다(패딩 포함 줄 바이트 수를 넘겨야 함).

## OpenCvSharp (검증됨)

`VisionImageViewer.OpenCvSharp.dll`을 참조합니다. OpenCvSharp4 4.13 계열로 빌드되어 있습니다.

```csharp
using VisionImageViewer;

viewer.SetImage(mat);                              // CV_8UC1, CV_8UC3(BGR), CV_8UC4, CV_16UC1
viewer.SetImage(mat, overlays);
viewer.SetImage(mat, ViewerPixelFormat.Mono12);    // 16bit Mat에 12bit 영상
viewer.SetImage(mat, ViewerPixelFormat.Rgb24);     // RGB 순서의 3채널 Mat
viewer.SetImage(new Mat(mat, roi));                // ROI로 잘라낸 Mat도 그대로

OpenCvFileDecoder.Register();                      // 프로그램 시작 시 한 번: 16bit tif, webp 등 열기
```

- 빈 Mat, 해제된 Mat, null은 무시하고 `false`를 반환합니다.
- `CV_32F` 등 지원하지 않는 형식은 `NotSupportedException`이 납니다. 먼저 `ConvertTo`나 `Normalize`로 8bit/16bit로 바꾸세요.
- 다른 버전의 OpenCvSharp4를 쓰는 프로젝트는 어댑터 프로젝트를 소스로 참조해 그 버전으로 다시 빌드하는 것이 안전합니다.
  어댑터를 쓰지 않고 직접 넘겨도 됩니다: `viewer.SetImage(mat.Data, mat.Cols, mat.Rows, (int)mat.Step(), ViewerPixelFormat.Mono8);`

## Basler pylon (.NET) — 미검증 예제

```csharp
private void OnImageGrabbed(object sender, ImageGrabbedEventArgs e)
{
    IGrabResult result = e.GrabResult;
    if (!result.GrabSucceeded) { return; }
    byte[] data = result.PixelData as byte[];
    // Mono8: 줄 간격 = 가로 + PaddingX
    viewer.SetImage(data, result.Width, result.Height, result.Width + result.PaddingX, ViewerPixelFormat.Mono8);
}
```

Bayer 카메라는 `PixelDataConverter`로 `BGR8packed`로 바꾼 뒤 `Bgr24`로 넘깁니다.

## Hikvision MVS (MvCamCtrl.NET) — 미검증 예제

```csharp
private void ImageCallback(IntPtr data, ref MyCamera.MV_FRAME_OUT_INFO_EX info, IntPtr user)
{
    if (info.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8)
    {
        viewer.SetImage(data, info.nWidth, info.nHeight, info.nWidth, ViewerPixelFormat.Mono8);
    }
}
```

컬러는 `MV_CC_ConvertPixelType_NET`으로 `BGR8_Packed`로 변환한 버퍼를 `Bgr24`, stride = 가로×3으로 넘깁니다.

## Matrox MIL — 미검증 예제

```csharp
MIL_INT address = 0, pitch = 0, width = 0, height = 0;
MIL.MbufInquire(buffer, MIL.M_HOST_ADDRESS, ref address);
MIL.MbufInquire(buffer, MIL.M_PITCH_BYTE, ref pitch);
MIL.MbufInquire(buffer, MIL.M_SIZE_X, ref width);
MIL.MbufInquire(buffer, MIL.M_SIZE_Y, ref height);
viewer.SetImage((IntPtr)address, (int)width, (int)height, (int)pitch, ViewerPixelFormat.Mono8);
```

- 버퍼가 호스트 메모리에 있어야 합니다(`M_HOST_ADDRESS`가 0이면 온보드 버퍼이므로 `MbufGet`으로 복사해서 넘김).
- MIL 컬러 버퍼의 기본은 평면(planar) 배치입니다. 밴드별 자식 버퍼(`MbufChildColor`의 `M_RED`/`M_GREEN`/`M_BLUE`)의 주소를 구해
  `viewer.SetImagePlanar(red, green, blue, width, height, pitch)`로 넘기거나, 버퍼를 `M_BGR24 + M_PACKED`로 할당해 `Bgr24`로 넘깁니다.

## MVTec HALCON (.NET) — 미검증 예제

```csharp
string type; int width, height;
IntPtr pointer = image.GetImagePointer1(out type, out width, out height);
// type "byte" → Mono8, "uint2" → Mono16. HALCON 영상은 줄 패딩이 없습니다.
viewer.SetImage(pointer, width, height, width, ViewerPixelFormat.Mono8);

// 3채널 컬러 (R, G, B 평면)
IntPtr r, g, b;
image.GetImagePointer3(out r, out g, out b, out type, out width, out height);
viewer.SetImagePlanar(r, g, b, width, height, width);
```

## Cognex VisionPro — 미검증 예제

```csharp
using (ICogImage8PixelMemory memory = image.Get8GreyPixelMemory(CogImageDataModeConstants.Read, 0, 0, image.Width, image.Height))
{
    viewer.SetImage(memory.Scan0, memory.Width, memory.Height, memory.Stride, ViewerPixelFormat.Mono8);
}
```

## Euresys Open eVision — 미검증 예제

```csharp
// EImageBW8
viewer.SetImage(image.GetImagePtr(), image.Width, image.Height, image.RowPitch, ViewerPixelFormat.Mono8);
// EImageC24 (B,G,R 순서)
viewer.SetImage(color.GetImagePtr(), color.Width, color.Height, color.RowPitch, ViewerPixelFormat.Bgr24);
```

## EmguCV — 미검증 예제

```csharp
viewer.SetImage(mat.DataPointer, mat.Cols, mat.Rows, mat.Step, ViewerPixelFormat.Bgr24);
```

## 새 라이브러리를 붙일 때 확인 순서

1. 8bit 흑백 한 장을 넣어 영상이 기울지 않는지 확인 (stride).
2. 왼쪽 위에만 표시가 있는 영상으로 위아래·좌우가 맞는지 확인 (아래→위 저장이면 음수 stride).
3. 컬러는 빨강/파랑 물체로 색 순서 확인.
4. 뷰어의 `PixelHover` 값이 라이브러리에서 읽은 픽셀 값과 같은지 확인.
