using System;
using System.IO;
using Suterusu.Interop;

namespace Suterusu.Bootstrap
{
    public static class ConsoleManager
    {
        public static void AllocDebugConsole()
        {
            NativeMethods.AllocConsole();
            // Redirect standard streams to the new console
            try
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }
            catch { /* best-effort */ }
        }

        public static void FreeHeadlessConsole()
        {
            try { NativeMethods.FreeConsole(); }
            catch { /* best-effort */ }
        }
    }
}
