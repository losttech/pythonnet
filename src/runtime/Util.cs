using System;
using System.Runtime.InteropServices;

namespace Python.Runtime
{
    internal class Util
    {
        // On Windows, a C long is always 32 bits.
        static readonly bool clong32bit = Runtime.IsWindows || Runtime.Is32Bit;

        internal static Int64 ReadCLong(IntPtr tp, int offset)
        {
            if (clong32bit)
            {
                return Marshal.ReadInt32(tp, offset);
            }
            else
            {
                return Marshal.ReadInt64(tp, offset);
            }
        }

        internal static void WriteCLong(IntPtr type, int offset, Int64 flags)
        {
            if (clong32bit)
            {
                Marshal.WriteInt32(type, offset, (Int32)(flags & 0xffffffffL));
            }
            else
            {
                Marshal.WriteInt64(type, offset, flags);
            }
        }

        internal static unsafe Int32 ReadInt32Aligned(IntPtr ptr, int byteOffset)
        {
            byte* address = (byte*)ptr + byteOffset;
            return *((int*)address);
        }

        internal static unsafe Int64 ReadInt64Aligned(IntPtr ptr, int byteOffset)
        {
            byte* address = (byte*)ptr + byteOffset;
            return *((long*)address);
        }

        internal static unsafe Int64 ReadCLongAligned(IntPtr ptr, int byteOffset)
        {
            return clong32bit ? ReadInt32Aligned(ptr, byteOffset) : ReadInt64Aligned(ptr, byteOffset);
        }

        internal static unsafe IntPtr ReadIntPtrAligned(IntPtr ptr, int byteOffset)
        {
            byte* address = (byte*)ptr + byteOffset;
            return *((IntPtr*)address);
        }

        internal static unsafe void WriteIntPtrAligned(IntPtr ptr, int byteOffset, IntPtr value)
        {
            byte* address = (byte*)ptr + byteOffset;
            *((IntPtr*)address) = value;
        }
    }
}
