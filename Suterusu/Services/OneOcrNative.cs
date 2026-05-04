using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Suterusu.Interop;

namespace Suterusu.Services
{
    internal sealed class OneOcrNative : IDisposable
    {
        private const string ModelKey = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";

        private readonly IntPtr _libraryHandle;
        private readonly CreateOcrInitOptionsDelegate _createOcrInitOptions;
        private readonly OcrInitOptionsSetUseModelDelayLoadDelegate _setUseModelDelayLoad;
        private readonly CreateOcrPipelineDelegate _createOcrPipeline;
        private readonly CreateOcrProcessOptionsDelegate _createOcrProcessOptions;
        private readonly OcrProcessOptionsSetMaxRecognitionLineCountDelegate _setMaxRecognitionLineCount;
        private readonly RunOcrPipelineDelegate _runOcrPipeline;
        private readonly GetOcrLineCountDelegate _getOcrLineCount;
        private readonly GetOcrLineDelegate _getOcrLine;
        private readonly GetOcrLineContentDelegate _getOcrLineContent;
        private readonly ReleaseHandleDelegate _releaseOcrInitOptions;
        private readonly ReleaseHandleDelegate _releaseOcrPipeline;
        private readonly ReleaseHandleDelegate _releaseOcrProcessOptions;
        private readonly ReleaseHandleDelegate _releaseOcrResult;

        public OneOcrNative(string runtimePath)
        {
            if (string.IsNullOrWhiteSpace(runtimePath))
                throw new ArgumentException("OneOCR runtime path is empty.", nameof(runtimePath));

            string dllPath = Path.Combine(runtimePath, OneOcrRuntimeLocator.OneOcrDllName);
            _libraryHandle = NativeMethods.LoadLibraryEx(dllPath, IntPtr.Zero, NativeMethods.LOAD_WITH_ALTERED_SEARCH_PATH);
            if (_libraryHandle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to load oneocr.dll. Win32 error: " + Marshal.GetLastWin32Error());

            try
            {
                _createOcrInitOptions = GetExport<CreateOcrInitOptionsDelegate>("CreateOcrInitOptions");
                _setUseModelDelayLoad = GetExport<OcrInitOptionsSetUseModelDelayLoadDelegate>("OcrInitOptionsSetUseModelDelayLoad");
                _createOcrPipeline = GetExport<CreateOcrPipelineDelegate>("CreateOcrPipeline");
                _createOcrProcessOptions = GetExport<CreateOcrProcessOptionsDelegate>("CreateOcrProcessOptions");
                _setMaxRecognitionLineCount = GetExport<OcrProcessOptionsSetMaxRecognitionLineCountDelegate>("OcrProcessOptionsSetMaxRecognitionLineCount");
                _runOcrPipeline = GetExport<RunOcrPipelineDelegate>("RunOcrPipeline");
                _getOcrLineCount = GetExport<GetOcrLineCountDelegate>("GetOcrLineCount");
                _getOcrLine = GetExport<GetOcrLineDelegate>("GetOcrLine");
                _getOcrLineContent = GetExport<GetOcrLineContentDelegate>("GetOcrLineContent");
                _releaseOcrInitOptions = GetExport<ReleaseHandleDelegate>("ReleaseOcrInitOptions");
                _releaseOcrPipeline = GetExport<ReleaseHandleDelegate>("ReleaseOcrPipeline");
                _releaseOcrProcessOptions = GetExport<ReleaseHandleDelegate>("ReleaseOcrProcessOptions");
                _releaseOcrResult = GetExport<ReleaseHandleDelegate>("ReleaseOcrResult");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public string Recognize(Bitmap bitmap, string modelPath)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("OneOCR model path is empty.", nameof(modelPath));

            long initOptions = 0;
            long pipeline = 0;
            long processOptions = 0;
            long result = 0;
            IntPtr modelPathBuffer = IntPtr.Zero;
            IntPtr keyBuffer = IntPtr.Zero;
            BitmapData bitmapData = null;

            try
            {
                ThrowIfFailed(_createOcrInitOptions(out initOptions), "CreateOcrInitOptions");
                ThrowIfFailed(_setUseModelDelayLoad(initOptions, 0), "OcrInitOptionsSetUseModelDelayLoad");

                modelPathBuffer = AllocUtf8(modelPath);
                keyBuffer = AllocUtf8(ModelKey);

                ThrowIfFailed(_createOcrPipeline(modelPathBuffer.ToInt64(), keyBuffer.ToInt64(), initOptions, out pipeline), "CreateOcrPipeline");
                ThrowIfFailed(_createOcrProcessOptions(out processOptions), "CreateOcrProcessOptions");
                ThrowIfFailed(_setMaxRecognitionLineCount(processOptions, 1000), "OcrProcessOptionsSetMaxRecognitionLineCount");

                var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                var image = new OneOcrImage
                {
                    Type = 3,
                    Cols = bitmap.Width,
                    Rows = bitmap.Height,
                    Unknown = 0,
                    Step = Math.Abs(bitmapData.Stride),
                    DataPtr = bitmapData.Scan0.ToInt64()
                };

                ThrowIfFailed(_runOcrPipeline(pipeline, ref image, processOptions, out result), "RunOcrPipeline");

                long lineCount;
                ThrowIfFailed(_getOcrLineCount(result, out lineCount), "GetOcrLineCount");

                var lines = new List<string>();
                for (long i = 0; i < lineCount; i++)
                {
                    long line;
                    ThrowIfFailed(_getOcrLine(result, i, out line), "GetOcrLine");
                    if (line == 0)
                        continue;

                    long contentPtr;
                    ThrowIfFailed(_getOcrLineContent(line, out contentPtr), "GetOcrLineContent");
                    string content = PtrToUtf8String(new IntPtr(contentPtr));
                    if (!string.IsNullOrWhiteSpace(content))
                        lines.Add(content.Trim());
                }

                return string.Join("\n", lines);
            }
            finally
            {
                if (bitmapData != null)
                    bitmap.UnlockBits(bitmapData);

                if (result != 0)
                    _releaseOcrResult(result);
                if (processOptions != 0)
                    _releaseOcrProcessOptions(processOptions);
                if (pipeline != 0)
                    _releaseOcrPipeline(pipeline);
                if (initOptions != 0)
                    _releaseOcrInitOptions(initOptions);

                if (modelPathBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(modelPathBuffer);
                if (keyBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(keyBuffer);
            }
        }

        public void Dispose()
        {
            if (_libraryHandle != IntPtr.Zero)
                NativeMethods.FreeLibrary(_libraryHandle);
        }

        private T GetExport<T>(string name) where T : class
        {
            IntPtr proc = NativeMethods.GetProcAddress(_libraryHandle, name);
            if (proc == IntPtr.Zero)
                throw new MissingMethodException("oneocr.dll", name);

            return Marshal.GetDelegateForFunctionPointer(proc, typeof(T)) as T;
        }

        private static void ThrowIfFailed(long result, string operation)
        {
            if (result != 0)
                throw new InvalidOperationException(operation + " failed with OneOCR status " + result + ".");
        }

        private static IntPtr AllocUtf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return ptr;
        }

        private static string PtrToUtf8String(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return string.Empty;

            int length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
                length++;

            if (length == 0)
                return string.Empty;

            byte[] bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OneOcrImage
        {
            public int Type;
            public int Cols;
            public int Rows;
            public int Unknown;
            public long Step;
            public long DataPtr;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long CreateOcrInitOptionsDelegate(out long options);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long OcrInitOptionsSetUseModelDelayLoadDelegate(long options, byte useModelDelayLoad);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long CreateOcrPipelineDelegate(long modelPath, long key, long options, out long pipeline);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long CreateOcrProcessOptionsDelegate(out long options);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long OcrProcessOptionsSetMaxRecognitionLineCountDelegate(long options, long maxLineCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long RunOcrPipelineDelegate(long pipeline, ref OneOcrImage image, long options, out long result);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long GetOcrLineCountDelegate(long result, out long lineCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long GetOcrLineDelegate(long result, long index, out long line);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long GetOcrLineContentDelegate(long line, out long contentPtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long ReleaseHandleDelegate(long handle);
    }
}
