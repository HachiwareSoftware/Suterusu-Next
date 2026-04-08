using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Suterusu.Configuration;
using Suterusu.Interop;
using Suterusu.Services;

namespace Suterusu.Notifications
{
    /// <summary>
    /// Shows a small topmost, non-activating overlay dot (green = success, red = failure)
    /// that blinks from transparent to color for a configurable number of times.
    /// </summary>
    public class CircleDotNotificationService : INotificationService, IDisposable
    {
        private readonly ILogger _logger = new NLogLogger("Suterusu.Notification.CircleDot");
        private readonly int _blinkCount;
        private readonly int _blinkDurationMs;

        private const int DotSize      = 14;
        private const int MarginRight  = 20;
        private const int MarginBottom = 20;
        private const int BlinkTimerMs = 20;

        private readonly BlockingCollection<(Color color, AppConfig config)> _queue
            = new BlockingCollection<(Color color, AppConfig config)>();
        private readonly System.Threading.Thread _staThread;

        public CircleDotNotificationService(int blinkCount, int blinkDurationMs)
        {
            _blinkCount = blinkCount >= 1 ? blinkCount : 3;
            _blinkDurationMs = blinkDurationMs >= 200 ? blinkDurationMs : 600;

            _staThread = new System.Threading.Thread(ProcessQueue);
            _staThread.SetApartmentState(System.Threading.ApartmentState.STA);
            _staThread.IsBackground = true;
            _staThread.Name = "CircleDot-STA";
            _staThread.Start();
        }

        public void NotifySuccess() => _queue.TryAdd((Color.LimeGreen, null));
        public void NotifyFailure() => _queue.TryAdd((Color.Crimson, null));

        public void Dispose()
        {
            _queue.CompleteAdding();
        }

        private void ProcessQueue()
        {
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    try { DoShowDot(item.color, _blinkCount, _blinkDurationMs); }
                    catch (Exception ex) { _logger.Error("CircleDot overlay error.", ex); }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("CircleDot STA thread exiting with error.", ex);
            }
        }

        private static void DoShowDot(Color color, int blinkCount, int blinkDurationMs)
        {
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            int x = screen.Right  - DotSize - MarginRight;
            int y = screen.Bottom - DotSize - MarginBottom;

            // Each blink = one transparent→color→transparent cycle.
            int cycleMs     = blinkDurationMs / blinkCount;
            int halfCycleMs = cycleMs / 2;

            using (var form = new OverlayForm(color, x, y, DotSize))
            {
                var closeTimer = new Timer { Interval = blinkDurationMs + 50 };
                closeTimer.Tick += (s, e) =>
                {
                    closeTimer.Stop();
                    form.SetAlpha(0);
                    form.Close();
                };
                closeTimer.Start();

                var sw         = Stopwatch.StartNew();
                var blinkTimer = new Timer { Interval = BlinkTimerMs };

                blinkTimer.Tick += (s, e) =>
                {
                    long elapsedMs = sw.ElapsedMilliseconds;
                    if (elapsedMs >= blinkDurationMs)
                    {
                        form.SetAlpha(0);
                        return;
                    }

                    // Position within the current blink cycle (ms)
                    long posInCycle = elapsedMs % cycleMs;

                    int alpha;
                    if (posInCycle < halfCycleMs)
                        alpha = (int)((double)posInCycle / halfCycleMs * 255);
                    else
                        alpha = (int)((double)(cycleMs - posInCycle) / halfCycleMs * 255);

                    form.SetAlpha(alpha);
                };
                blinkTimer.Start();

                System.Windows.Forms.Application.Run(form);

                blinkTimer.Stop();
                blinkTimer.Dispose();
                closeTimer.Dispose();
            }
        }

        private sealed class OverlayForm : Form
        {
            private readonly Color _dotColor;
            private int _alpha = 0;

            public OverlayForm(Color dotColor, int x, int y, int size)
            {
                _dotColor = dotColor;

                FormBorderStyle = FormBorderStyle.None;
                BackColor       = Color.Magenta;
                TransparencyKey = Color.Magenta;
                StartPosition   = FormStartPosition.Manual;
                Location        = new Point(x, y);
                Size            = new Size(size, size);
                ShowInTaskbar   = false;
                TopMost         = true;

                SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            }

            public void SetAlpha(int alpha)
            {
                _alpha = Math.Max(0, Math.Min(255, alpha));
                Invalidate();
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                int exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
                NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, exStyle);

                NativeMethods.SetWindowPos(
                    Handle,
                    new IntPtr(NativeMethods.HWND_TOPMOST),
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.FromArgb(_alpha, _dotColor)))
                {
                    const int margin = 1;
                    e.Graphics.FillEllipse(brush,
                        margin, margin,
                        Width  - margin * 2,
                        Height - margin * 2);
                }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
                    return cp;
                }
            }
        }
    }
}
