using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;
using Suterusu.Interop;
using Suterusu.Services;

namespace Suterusu.Notifications
{
    /// <summary>
    /// Shows a small topmost, non-activating overlay dot (green = success, red = failure)
    /// that pulses with a smooth sine-wave fade for a configurable duration, then auto-hides.
    /// The sine animation always completes exactly two full cycles within the display period.
    /// <para>
    /// All overlay forms run on a single long-lived STA background thread so that
    /// <see cref="System.Windows.Forms.Application.Run(Form)"/> is always called on the
    /// same stable thread context. Spawning a new STA thread per notification causes
    /// .NET Framework 4.8 WinForms to silently skip message-pump initialization on the
    /// second and subsequent threads, making the form invisible.
    /// </para>
    /// </summary>
    public class CircleDotNotificationService : INotificationService, IDisposable
    {
        private readonly ILogger _logger = new NLogLogger("Suterusu.Notification.CircleDot");
        private readonly int _pulseDurationMs;

        private const int DotSize      = 14;
        private const int MarginRight  = 20;
        private const int MarginBottom = 20;
        private const int PulseTimerMs = 40;   // ~25 fps — smooth without being heavy

        // Single dedicated STA thread that owns all WinForms message loops.
        private readonly BlockingCollection<Color> _queue
            = new BlockingCollection<Color>();
        private readonly System.Threading.Thread _staThread;

        public CircleDotNotificationService(int pulseDurationMs)
        {
            // Guard: clamp internally so the service is safe even if called with an
            // un-normalized config value (e.g. 0 from a freshly-constructed test config).
            _pulseDurationMs = pulseDurationMs >= 200 ? pulseDurationMs : 800;

            _staThread = new System.Threading.Thread(ProcessQueue);
            _staThread.SetApartmentState(System.Threading.ApartmentState.STA);
            _staThread.IsBackground = true;
            _staThread.Name = "CircleDot-STA";
            _staThread.Start();
        }

        public void NotifySuccess() => _queue.TryAdd(Color.LimeGreen);
        public void NotifyFailure() => _queue.TryAdd(Color.Crimson);

        public void Dispose()
        {
            // Signal the STA thread to drain and exit.
            _queue.CompleteAdding();
        }

        private void ProcessQueue()
        {
            try
            {
                foreach (Color color in _queue.GetConsumingEnumerable())
                {
                    try { DoShowDot(color, _pulseDurationMs); }
                    catch (Exception ex) { _logger.Error("CircleDot overlay error.", ex); }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("CircleDot STA thread exiting with error.", ex);
            }
        }

        private static void DoShowDot(Color color, int pulseDurationMs)
        {
            // Position in lower-right corner of the primary screen.
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            int x = screen.Right  - DotSize - MarginRight;
            int y = screen.Bottom - DotSize - MarginBottom;

            // Phase increment per timer tick so that exactly 2 full sine cycles (4π)
            // fill the total display duration.
            int    totalTicks     = Math.Max(1, pulseDurationMs / PulseTimerMs);
            double phaseIncrement = (4.0 * Math.PI) / totalTicks;

            using (var form = new OverlayForm(color, x, y, DotSize))
            {
                // Auto-close timer.
                var closeTimer = new Timer { Interval = pulseDurationMs };
                closeTimer.Tick += (s, e) =>
                {
                    closeTimer.Stop();
                    form.Close();
                };
                closeTimer.Start();

                // Pulse animation timer — advances the sine phase each tick.
                var pulseTimer = new Timer { Interval = PulseTimerMs };
                pulseTimer.Tick += (s, e) => form.AdvancePulse(phaseIncrement);
                pulseTimer.Start();

                System.Windows.Forms.Application.Run(form);

                pulseTimer.Stop();
                pulseTimer.Dispose();
                closeTimer.Dispose();
            }
        }

        // -----------------------------------------------------------------------
        // Inner overlay form
        // -----------------------------------------------------------------------
        private sealed class OverlayForm : Form
        {
            private readonly Color _dotColor;
            private double _pulsePhase;          // current sine phase in radians
            private int    _pulseAlpha = 255;    // 40–255, driven by sine wave

            public OverlayForm(Color dotColor, int x, int y, int size)
            {
                _dotColor = dotColor;

                FormBorderStyle = FormBorderStyle.None;
                BackColor       = Color.Magenta;    // transparent key colour
                TransparencyKey = Color.Magenta;
                StartPosition   = FormStartPosition.Manual;
                Location        = new Point(x, y);
                Size            = new Size(size, size);
                ShowInTaskbar   = false;
                TopMost         = true;

                SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            }

            /// <summary>
            /// Advances the sine phase by <paramref name="increment"/> radians and repaints.
            /// Maps sin ∈ [−1, 1] to alpha ∈ [40, 255] so the dot never goes fully invisible.
            /// </summary>
            public void AdvancePulse(double increment)
            {
                _pulsePhase += increment;
                _pulseAlpha  = (int)(((Math.Sin(_pulsePhase) + 1.0) / 2.0) * 215.0) + 40;
                Invalidate();
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW — prevents focus steal and taskbar entry.
                int exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
                NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, exStyle);

                // Force topmost via SetWindowPos so it survives other topmost windows.
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
                using (var brush = new SolidBrush(Color.FromArgb(_pulseAlpha, _dotColor)))
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
