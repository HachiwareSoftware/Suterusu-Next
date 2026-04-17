using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Suterusu.Interop;
using Suterusu.Services;

namespace Suterusu.Hooks
{
    public enum SelectionState
    {
        Idle,
        WaitingFirstClick,
        WaitingSecondClick
    }

    public class MouseHook : IDisposable
    {
        private readonly ILogger _logger;
        private NativeMethods.LowLevelMouseProc _proc;
        private IntPtr _hookHandle = IntPtr.Zero;

        private SelectionState _state = SelectionState.Idle;
        private Point _firstPoint;

        public event EventHandler<Rectangle> SelectionComplete;
        public event EventHandler SelectionCancelled;

        public bool IsInstalled => _hookHandle != IntPtr.Zero;
        public SelectionState State => _state;

        public MouseHook(ILogger logger)
        {
            _logger = logger;
        }

        public void StartSelection()
        {
            if (_state != SelectionState.Idle)
                return;

            _state = SelectionState.WaitingFirstClick;
            _firstPoint = default(Point);
            _logger.Info("Selection mode started. Click first corner.");
        }

        public void CancelSelection()
        {
            if (_state == SelectionState.Idle)
                return;

            _logger.Info("Selection cancelled.");
            _state = SelectionState.Idle;
            _firstPoint = default(Point);
            SelectionCancelled?.Invoke(this, EventArgs.Empty);
        }

        public void Install()
        {
            if (IsInstalled)
                throw new InvalidOperationException("Hook already installed.");

            _proc = HookCallback;

            using (Process proc = Process.GetCurrentProcess())
            using (ProcessModule module = proc.MainModule)
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(module.ModuleName);
                _hookHandle = NativeMethods.SetWindowsHookExMouse(
                    NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
            }

            if (_hookHandle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"SetWindowsHookEx failed with Win32 error {err}.");
            }

            _logger.Info("Mouse hook installed.");
        }

        public void Uninstall()
        {
            if (!IsInstalled) return;

            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _logger.Info("Mouse hook uninstalled.");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isLeftButtonDown = (msg == NativeMethods.WM_LBUTTONDOWN);

                if (isLeftButtonDown)
                {
                    var mouseInfo = (NativeMethods.MSLLHOOKSTRUCT)
                        Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));

                    var point = new Point(mouseInfo.pt.X, mouseInfo.pt.Y);

                    if (_state == SelectionState.WaitingFirstClick)
                    {
                        _firstPoint = point;
                        _state = SelectionState.WaitingSecondClick;
                        _logger.Info($"First corner: {point.X},{point.Y}. Click second corner.");
                    }
                    else if (_state == SelectionState.WaitingSecondClick)
                    {
                        var rect = new Rectangle(
                            Math.Min(_firstPoint.X, point.X),
                            Math.Min(_firstPoint.Y, point.Y),
                            Math.Abs(point.X - _firstPoint.X),
                            Math.Abs(point.Y - _firstPoint.Y));

                        _logger.Info($"Selection complete: {rect}");
                        _state = SelectionState.Idle;
                        SelectionComplete?.Invoke(this, rect);
                    }

                    return (IntPtr)1;
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Uninstall();
        }
    }
}