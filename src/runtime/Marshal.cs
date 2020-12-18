//#if DEBUG
using System;
using System.Diagnostics;

using Python.Runtime.Native;

using M = System.Runtime.InteropServices.Marshal;

namespace Python.Runtime
{
    static class Marshal
    {
        public static IntPtr ReadIntPtr(IntPtr pyObj) => M.ReadIntPtr(pyObj);
        public static IntPtr ReadIntPtr(IntPtr pyObj, int offset) => M.ReadIntPtr(pyObj, offset);
        public static void WriteIntPtr(IntPtr pyObj, int offset, IntPtr value)
        {
            Debug.Assert(offset >= 0);
            var type = Runtime.PyObject_TYPE(pyObj);
            int size = ReadInt32(type, TypeOffset.tp_basicsize);
            Debug.Assert(offset + IntPtr.Size <= size);
            M.WriteIntPtr(pyObj, offset, value);
        }
        public static void WriteIntPtr(IntPtr ptr, IntPtr value) => M.WriteIntPtr(ptr, value);

        public static long ReadInt64(IntPtr pyObj, int offset) => M.ReadInt64(pyObj, offset);
        public static void WriteInt64(IntPtr pyObj, int offset, long value) => M.WriteInt64(pyObj, offset, value);

        public static int ReadInt32(IntPtr pyObj, int offset) => M.ReadInt32(pyObj, offset);
        public static void WriteInt32(IntPtr ptr, int offset, int value) => M.WriteInt32(ptr, offset, value);
        public static void WriteInt32(IntPtr ptr, int value) => M.WriteInt32(ptr, value);

        public static short ReadInt16(IntPtr ptr, int offset) => M.ReadInt16(ptr, offset);

        public static byte ReadByte(IntPtr addr) => M.ReadByte(addr);
        public static void WriteByte(IntPtr pyObj, int offset, byte value) => M.WriteByte(pyObj, offset, value);

        public static object PtrToStructure(IntPtr ptr, Type structureType) => M.PtrToStructure(ptr, structureType);

        public static int SizeOf(Type type) => M.SizeOf(type);

        public static void Copy(IntPtr source, byte[] dest, int startIndex, int length)
            => M.Copy(source, dest, startIndex, length);
        public static void Copy(IntPtr source, char[] dest, int startIndex, int length)
            => M.Copy(source, dest, startIndex, length);
        public static void Copy(IntPtr source, IntPtr[] dest, int startIndex, int length)
            => M.Copy(source, dest, startIndex, length);
        public static void Copy(byte[] source, int startIndex, IntPtr dest, int length)
            => M.Copy(source, startIndex, dest, length);

        public static IntPtr GetFunctionPointerForDelegate(Delegate @delegate) => M.GetFunctionPointerForDelegate(@delegate);
        public static Delegate GetDelegateForFunctionPointer(IntPtr function, Type type) => M.GetDelegateForFunctionPointer(function, type);

        public static IntPtr AllocHGlobal(int size) => M.AllocHGlobal(size);
        public static void FreeHGlobal(IntPtr mem) => M.FreeHGlobal(mem);

        public static IntPtr StringToHGlobalAnsi(string value) => M.StringToHGlobalAnsi(value);
        public static string PtrToStringAnsi(IntPtr ptr) => M.PtrToStringAnsi(ptr);
    }
}
//#endif
