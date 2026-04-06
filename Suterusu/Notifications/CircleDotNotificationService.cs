using System;
using System.Drawing;
using System.Windows.Forms;
using Suterusu.Interop;
using Suterusu.Services;

namespace Suterusu.Notifications
{
    /// <summary>
    /// Shows a small topmost, non-activating overlay dot (green = success, red = failure)
    /// for a brief period, then auto-hides.
    /// </summary>
    public class CircleDotNotificationService : INotificationService
    {
        private readonly ILogger _logger = new NLogLogger("Suterusu.Notification.CircleDot");
        private const int DotSize     = 24;
        private const int DisplayMs   = 1500;
        private const int MarginRight = 20;
        private const int MarginBottom = 20;

        public void NotifySuccess() => ShowDot(Color.LimeGreen);
        public void NotifyFailure() => ShowDot(Color.Crimson);

        private void ShowDot(Color color)
        {
            try
            {
                // Run on a dedicated STA thread so the WinForms overlay message loop works
                var thread = new System.Threading.Thread(() =>
                {
                    try { DoShowDot(color); }
                    catch (Exception ex) { _logger.Error("CircleDot overlay error.", ex); }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to start CircleDot thread.", ex);
            }
        }

        private static void DoShowDot(Color color)
        {
            // Position in lower-right corner of the primary screen
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            int x = screen.Right  - DotSize - MarginRight;
            int y = screen.Bottom - DotSize - MarginBottom;

            using (var form = new OverlayForm(color, x, y, DotSize))
            {
                // Auto-close after DisplayMs
                var timer = new Timer { Interval = DisplayMs };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    form.Close();
                };
                timer.Start();
                System.Windows.Forms.Application.Run(form);
                timer.Dispose();
            }
        }

        // -----------------------------------------------------------------------
        // Inner overlay form
        // -----------------------------------------------------------------------
        private sealed class OverlayForm : Form
        {
            private readonly Color _dotColor;

            public OverlayForm(Color dotColor, int x, int y, int size)
            {
                _dotColor = dotColor;

                FormBorderStyle = FormBorderStyle.None;
                BackColor       = Color.Magenta;      // transparent key color
                TransparencyKey = Color.Magenta;
                StartPosition   = FormStartPosition.Manual;
                Location        = new Point(x, y);
                Size            = new Size(size, size);
                ShowInTaskbar   = false;
                TopMost         = true;

                // Make it non-activating via extended window styles
                SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // Set WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW so it doesn't steal focus
                int exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
                NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, exStyle);

                // Force topmost via SetWindowPos
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
                using (var brush = new SolidBrush(_dotColor))
                {
                    int margin = 1;
                    e.Graphics.FillEllipse(brush,
                        margin, margin,
                        Width  - margin * 2,
                        Height - margin * 2);
                }
            }

            // Prevent the window from ever gaining activation
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
