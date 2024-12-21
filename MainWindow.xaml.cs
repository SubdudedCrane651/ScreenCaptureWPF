using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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

        public MainWindow()
        {
            InitializeComponent();
            InitializeLabelTimer();
            SpeedComboBox.SelectedIndex = 1; // Set default to Normal (15 fps)
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
            // Get selected frame rate from ComboBox
            Dispatcher.Invoke(() => {
                ComboBoxItem selectedSpeedItem = (ComboBoxItem)SpeedComboBox.SelectedItem;
                frameRate = int.Parse(selectedSpeedItem.Tag.ToString());
            });

            isRecording = true;
            recordingThread = new Thread(RecordScreen);
            recordingThread.Start();
            RecordingLabel.Content = "Recording";
            labelTimer.Start();
        }

        private void StopRecording()
        {
            isRecording = false;
            recordingThread.Join();
            RecordingLabel.Content = "Not Recording";
            labelTimer.Stop();
        }

        private void RecordScreen()
        {
            var screenBounds = GetScreenBounds();
            int width = screenBounds.Width;
            int height = screenBounds.Height;

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = Path.Combine(baseDirectory, "desktop_capture.mp4");

            using (var ffMpegWriter = new FFMpegWriter(filePath, width, height, frameRate))
            {
                while (isRecording)
                {
                    using (var bitmap = new Bitmap(width, height))
                    {
                        using (var g = Graphics.FromImage(bitmap))
                        {
                            g.CopyFromScreen(screenBounds.Left, screenBounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                        }

                        ffMpegWriter.WriteVideoFrame(bitmap);
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
