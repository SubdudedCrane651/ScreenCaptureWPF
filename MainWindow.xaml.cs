using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NHotkey;
using NHotkey.Wpf;
using NReco.VideoConverter;

namespace ScreenCaptureWPF
{
    public partial class MainWindow : Window
    {
        private Thread recordingThread;
        private bool isRecording = false;
        private DispatcherTimer labelTimer;
        private int dotCount = 0;
        private int frameRate;

        private WasapiLoopbackCapture waveIn;
        private WaveFileWriter waveWriter;
        private string audioFilePath;

        public MainWindow()
        {
            InitializeComponent();
            InitializeLabelTimer();
            SpeedComboBox.SelectedIndex = 0; // Set default to Normal (30 fps)
            SyncModeComboBox.SelectedIndex = 2; // Set default to Slow Video
            SlowdownComboBox.SelectedIndex = 2; //Set to 40%

            // Register the global hotkey (Ctrl+Shift+S to stop recording)
            HotkeyManager.Current.AddOrReplace("StopRecording", Key.S, ModifierKeys.Control | ModifierKeys.Shift, OnStopRecordingHotkey);
            HotkeyManager.Current.AddOrReplace("StartRecording", Key.R, ModifierKeys.Control | ModifierKeys.Shift, OnStartRecordingHotkey);
        }

        private void InitializeLabelTimer()
        {
            labelTimer = new DispatcherTimer();
            labelTimer.Interval = TimeSpan.FromSeconds(0.5);
            labelTimer.Tick += LabelTimer_Tick;
        }

        private void LabelTimer_Tick(object sender, EventArgs e)
        {
            dotCount = (dotCount + 1) % 4;
            RecordingLabel.Content = "Recording" + new string('.', dotCount);
        }

        private void StartRecording()
        {
            // Get selected frame rate and sync mode from ComboBoxes
            Dispatcher.Invoke(() => {
                ComboBoxItem selectedSpeedItem = (ComboBoxItem)SpeedComboBox.SelectedItem;
                frameRate = int.Parse(selectedSpeedItem.Tag.ToString());

                ComboBoxItem selectedSyncModeItem = (ComboBoxItem)SyncModeComboBox.SelectedItem;
                string syncMode = selectedSyncModeItem.Tag.ToString();
            });

            // Initialize audio recording using WASAPI Loopback
            audioFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio_capture.wav");
            waveIn = new WasapiLoopbackCapture();
            waveWriter = new WaveFileWriter(audioFilePath, waveIn.WaveFormat);
            waveIn.DataAvailable += WaveIn_DataAvailable;
            waveIn.RecordingStopped += WaveIn_RecordingStopped;
            waveIn.StartRecording();
            Console.WriteLine("Audio recording started");

            isRecording = true;
            recordingThread = new Thread(RecordScreen);
            recordingThread.Start();
            RecordingLabel.Content = "Recording";
            labelTimer.Start();
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (isRecording)
            {
                waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }

        private void WaveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            waveWriter?.Dispose();
            waveIn?.Dispose();
            Console.WriteLine("Audio recording stopped");
        }

        private void StopRecording()
        {
            isRecording = false;

            // Wait for the recording thread to finish
            if (recordingThread != null && recordingThread.IsAlive)
            {
                recordingThread.Join();
            }
            Console.WriteLine("Screen recording stopped");

            // Stop audio recording
            if (waveIn != null)
            {
                waveIn.StopRecording();
            }

            // Combine audio and video
            CombineAudioAndVideo();

            RecordingLabel.Content = "Not Recording";
            labelTimer.Stop();
        }

        private void OnStopRecordingHotkey(object sender, HotkeyEventArgs e)
        {
            StopRecording();
            e.Handled = true;
        }

        private void RecordScreen()
        {
            var screenBounds = GetScreenBounds();
            int width = screenBounds.Width;
            int height = screenBounds.Height;

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string videoFilePath = Path.Combine(baseDirectory, "video_capture.mp4");

            using (var ffMpegWriter = new FFMpegWriter(videoFilePath, width, height, frameRate))
            {
                while (isRecording)
                {
                    using (var bitmap = new Bitmap(width, height))
                    {
                        try
                        {
                            using (var g = Graphics.FromImage(bitmap))
                            {
                                g.CopyFromScreen(screenBounds.Left, screenBounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                            }

                            ffMpegWriter.WriteVideoFrame(bitmap);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error during screen capture: " + ex.Message);
                        }
                    }

                    Thread.Sleep(1000 / frameRate);
                }
            }
        }

        private Rectangle GetScreenBounds()
        {
            return new Rectangle(
                (int)SystemParameters.VirtualScreenLeft,
                (int)SystemParameters.VirtualScreenTop,
                (int)SystemParameters.VirtualScreenWidth,
                (int)SystemParameters.VirtualScreenHeight);
        }

        private async void CombineAudioAndVideo()
        {
            try
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string videoFilePath = Path.Combine(baseDirectory, "video_capture.mp4");
                string finalOutputPath = Path.Combine(baseDirectory, "final_output.mp4");

                // Delete the existing final_output.mp4 file if it exists
                if (File.Exists(finalOutputPath))
                {
                    File.Delete(finalOutputPath);
                    Console.WriteLine("Deleted existing final_output.mp4 file");
                }

                string syncMode = Dispatcher.Invoke(() => { ComboBoxItem selectedSyncModeItem = (ComboBoxItem)SyncModeComboBox.SelectedItem;
                    return selectedSyncModeItem.Tag.ToString(); }); 
                double slowdownFactor = Dispatcher.Invoke(() => { ComboBoxItem selectedSlowdownItem = (ComboBoxItem)SlowdownComboBox.SelectedItem;
                    return double.Parse(selectedSlowdownItem.Tag.ToString()); });

                string ffmpegArgs = $"-i \"{videoFilePath}\" -i \"{audioFilePath}\" -filter:v \"setpts={slowdownFactor}*PTS\" -r {frameRate} -c:a aac -strict experimental -async 1 \"{finalOutputPath}\"";

                // Log FFmpeg arguments
                Console.WriteLine("FFmpeg arguments: " + ffmpegArgs);

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                try
                {
                    var process = new Process
                    {
                        StartInfo = processStartInfo,
                        EnableRaisingEvents = true
                    };

                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            Console.WriteLine("FFmpeg Output: " + args.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            Console.WriteLine("FFmpeg Error: " + args.Data);
                        }
                    };

                    process.Exited += (sender, args) =>
                    {
                        Console.WriteLine("FFmpeg process exited with code " + process.ExitCode);
                        process.Dispose();
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await Task.Run(() => process.WaitForExit());

                    Console.WriteLine("Audio and video combined successfully");

                    // Show message box and play beep sound
                    SystemSounds.Beep.Play();
                    MessageBox.Show("Audio and video combination complete!", "Process Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error starting FFmpeg process: " + ex.Message);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error combining audio and video: " + ex.Message);
            }
        }

        private void OnStartRecordingHotkey(object sender, HotkeyEventArgs e)
        {
            StartRecording();
            e.Handled = true;
        }


        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartRecording();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopRecording();
        }
    }
}
