using OpenCvSharp;

namespace VisionImageViewer
{
    /// <summary>
    /// OpenCV로 이미지 파일을 해석하는 디코더입니다. 기본(GDI+) 디코더가 못 여는
    /// 16bit tif, webp, jp2, pgm/ppm 등을 열 수 있게 됩니다.
    /// 프로그램 시작 시 한 번 <see cref="Register"/>를 호출하면 모든 뷰어에 적용됩니다.
    /// </summary>
    public static class OpenCvFileDecoder
    {
        /// <summary>모든 뷰어의 기본 파일 디코더로 등록합니다.</summary>
        public static void Register()
        {
            ImageViewer.DefaultFileDecoder = Decode;
        }

        /// <summary>
        /// 파일 내용을 해석해 뷰어에 넣습니다. 파일을 바이트로 읽은 뒤 해석하므로 한글 경로도 문제없습니다.
        /// OpenCV가 해석하지 못하면 false를 반환해 뷰어가 기본 디코더로 다시 시도하게 합니다.
        /// </summary>
        public static bool Decode(string path, byte[] fileBytes, ImageViewer target)
        {
            try
            {
                using (Mat image = Cv2.ImDecode(fileBytes, ImreadModes.Unchanged))
                {
                    if (image == null || image.Empty())
                    {
                        return false;
                    }
                    int channels = image.Channels();
                    if (channels != 1 && channels != 3 && channels != 4)
                    {
                        return false;
                    }
                    int depth = image.Depth();
                    if (depth == MatType.CV_8U || (depth == MatType.CV_16U && channels == 1))
                    {
                        return target.SetImage(image);
                    }

                    // 그 밖의 형식(16bit 컬러, 부호 있는 정수, 실수)은 화면에 보이도록 8bit로 바꿉니다.
                    using (Mat display = new Mat())
                    {
                        if (depth == MatType.CV_16U)
                        {
                            image.ConvertTo(display, MatType.CV_8U, 1.0 / 257.0);
                        }
                        else
                        {
                            Cv2.Normalize(image, display, 0, 255, NormTypes.MinMax, MatType.CV_8U);
                        }
                        return target.SetImage(display);
                    }
                }
            }
            catch (OpenCVException)
            {
                return false;
            }
        }
    }
}
