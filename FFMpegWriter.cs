using System;
using System.Drawing;
using System.IO;
using NReco.VideoConverter;

namespace ScreenCaptureWPF
{
    public class FFMpegWriter : IDisposable
    {
        private readonly FFMpegConverter ffMpeg;
        private readonly string tempDirectory;
        private readonly string inputFile;
        private readonly string outputFilePath;
        private int frameNumber;
        private bool isDisposed = false;
        private int frameRate;

        public FFMpegWriter(string outputFilePath, int width, int height, int frameRate)
        {
            ffMpeg = new FFMpegConverter();
            tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            inputFile = Path.Combine(tempDirectory, "frame_%04d.bmp");
            this.outputFilePath = outputFilePath;
            this.frameRate = frameRate;
        }

        public void WriteVideoFrame(Bitmap frame)
        {
            string filePath = Path.Combine(tempDirectory, $"frame_{frameNumber:D4}.bmp");
            frame.Save(filePath, System.Drawing.Imaging.ImageFormat.Bmp);
            frameNumber++;
        }

        public void FinishWriting()
        {
            try
            {
                // Ensure paths are correctly formatted with quotes
                string tempInputFile = Path.Combine(tempDirectory, "frame_%04d.bmp");
                string ffmpegArgs = $"-y -r {frameRate} -i \"{tempInputFile}\" -vcodec libx264 -pix_fmt yuv420p \"{outputFilePath}\"";
                ffMpeg.Invoke(ffmpegArgs);
            }
            catch (FFMpegException ex)
            {
                Console.WriteLine($"FFMpeg error: {ex.Message}\nFFMpeg Args: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during final video writing: {ex.Message}");
                throw;
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        public void Dispose()
        {
            if (!isDisposed)
            {
                FinishWriting();
                isDisposed = true;
            }
        }
    }
}
