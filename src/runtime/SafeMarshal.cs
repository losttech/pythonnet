using System;
using System.Runtime.InteropServices;

namespace Python.Runtime {
    internal static class SafeMarshal {
        static unsafe void CheckPtr(IntPtr ptr) {
            //IntPtr* usedPools = (IntPtr*)PyMalloc_GetUsedPools();
            //if (usedPools == null) return;
            //for (int poolIndex = 0; poolIndex < 128; poolIndex++) {
            //    long diff = (long)ptr - (long)usedPools[poolIndex];
            //    if (diff <= IntPtr.Size && diff >= 0) {
            //        string msg = ptr.ToString("X") + "-" + usedPools[poolIndex].ToString("X");
            //        Console.WriteLine(msg);
            //        Trace.WriteLine(msg);
            //        Debugger.Break();
            //        throw new ArgumentException();
            //    }
            //}
        }

        public static void WriteIntPtr(IntPtr ptr, int offset, IntPtr value) {
            CheckPtr(ptr);
            CheckPtr(ptr + offset);
            Marshal.WriteIntPtr(ptr, offset, value);
        }

        public static void WriteIntPtr(IntPtr ptr, IntPtr value) {
            CheckPtr(ptr);
            Marshal.WriteIntPtr(ptr, value);
        }

        public static void WriteInt32(IntPtr ptr, int offset, int value) {
            CheckPtr(ptr);
            CheckPtr(ptr + offset);
            Marshal.WriteInt32(ptr, offset, value);
        }

        public static void WriteInt64(IntPtr ptr, int offset, long value) {
            CheckPtr(ptr);
            CheckPtr(ptr + offset);
            Marshal.WriteInt64(ptr, offset, value);
        }

        public static void WriteByte(IntPtr ptr, int offset, byte value) {
            CheckPtr(ptr);
            CheckPtr(ptr + offset);
            Marshal.WriteByte(ptr, offset, value);
        }

        //[DllImport(Runtime._PythonDll, CallingConvention = CallingConvention.Cdecl)]
        //internal static extern IntPtr PyMalloc_GetUsedPools();
    }
}
