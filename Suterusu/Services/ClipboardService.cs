using System;
using System.Runtime.InteropServices;
using System.Threading;
using Suterusu.Interop;
using Suterusu.Models;

namespace Suterusu.Services
{
    /// <summary>
    /// Safe clipboard read/write using Win32 APIs with retry for lock contention.
    /// </summary>
    public class ClipboardService
    {
        private readonly ILogger _logger;
        private const int RetryCount      = 5;
        private const int RetryDelayMs    = 50;

        public ClipboardService(ILogger logger)
        {
            _logger = logger;
        }

        public ClipboardReadResult TryReadText()
        {
            return ExecuteWithClipboardRetry(() =>
            {
                if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
                    return ClipboardReadResult.Fail("No unicode text on clipboard.");

                if (!NativeMethods.OpenClipboard(IntPtr.Zero))
                    return null; // signal retry

                try
                {
                    IntPtr hData = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
                    if (hData == IntPtr.Zero)
                        return ClipboardReadResult.Fail("GetClipboardData returned null.");

                    IntPtr pData = NativeMethods.GlobalLock(hData);
                    if (pData == IntPtr.Zero)
                        return ClipboardReadResult.Fail("GlobalLock failed.");

                    try
                    {
                        string text = Marshal.PtrToStringUni(pData);
                        return string.IsNullOrEmpty(text)
                            ? ClipboardReadResult.Fail("Clipboard text is empty.")
                            : ClipboardReadResult.Ok(text);
                    }
                    finally
                    {
                        NativeMethods.GlobalUnlock(hData);
                    }
                }
                finally
                {
                    NativeMethods.CloseClipboard();
                }
            });
        }

        public ClipboardWriteResult TryWriteText(string text)
        {
            if (text == null)
                return ClipboardWriteResult.Fail("Text is null.");

            return ExecuteWithClipboardRetry(() =>
            {
                if (!NativeMethods.OpenClipboard(IntPtr.Zero))
                    return null; // signal retry

                try
                {
                    NativeMethods.EmptyClipboard();

                    // Allocate global memory for the string (+1 for null terminator, *2 for Unicode)
                    int    byteCount = (text.Length + 1) * 2;
                    IntPtr hMem      = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)byteCount);

                    if (hMem == IntPtr.Zero)
                        return ClipboardWriteResult.Fail("GlobalAlloc failed.");

                    IntPtr pMem = NativeMethods.GlobalLock(hMem);
                    if (pMem == IntPtr.Zero)
                    {
                        // Can't free hMem safely here; just fail
                        return ClipboardWriteResult.Fail("GlobalLock failed.");
                    }

                    try
                    {
                        Marshal.Copy(text.ToCharArray(), 0, pMem, text.Length);
                        // Null terminator
                        Marshal.WriteInt16(pMem, text.Length * 2, 0);
                    }
                    finally
                    {
                        NativeMethods.GlobalUnlock(hMem);
                    }

                    IntPtr result = NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hMem);
                    if (result == IntPtr.Zero)
                        return ClipboardWriteResult.Fail("SetClipboardData failed.");

                    return ClipboardWriteResult.Ok();
                }
                finally
                {
                    NativeMethods.CloseClipboard();
                }
            });
        }

        private T ExecuteWithClipboardRetry<T>(Func<T> action) where T : class
        {
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    T result = action();
                    if (result != null) return result;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Clipboard operation failed (attempt {i + 1}): {ex.Message}");
                }

                Thread.Sleep(RetryDelayMs);
            }

            _logger.Error($"Clipboard operation failed after {RetryCount} attempts.");
            // Return a failure result - callers expect ClipboardReadResult or ClipboardWriteResult
            // We use dynamic dispatch via overloading — callers cast properly
            return CreateFailResult<T>("Clipboard locked after retries.");
        }

        private static T CreateFailResult<T>(string error) where T : class
        {
            if (typeof(T) == typeof(ClipboardReadResult))
                return ClipboardReadResult.Fail(error) as T;
            if (typeof(T) == typeof(ClipboardWriteResult))
                return ClipboardWriteResult.Fail(error) as T;
            return null;
        }
    }
}
