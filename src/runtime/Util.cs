using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Python.Runtime
{
    internal static class Util
    {
        internal static Int64 ReadCLong(IntPtr tp, int offset)
        {
            // On Windows, a C long is always 32 bits.
            if (Runtime.IsWindows || Runtime.Is32Bit)
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
            if (Runtime.IsWindows || Runtime.Is32Bit)
            {
                Marshal.WriteInt32(type, offset, (Int32)(flags & 0xffffffffL));
            }
            else
            {
                Marshal.WriteInt64(type, offset, flags);
            }
        }

        /// <summary>
        /// Walks the hierarchy of <paramref name="type"/> searching for the first
        /// attribute of type <typeparamref name="T"/> from the <paramref name="type"/> down to <see cref="object"/>.
        /// </summary>
        /// <typeparam name="T">Type of the attribute to search for</typeparam>
        /// <param name="type">The type potentially marked with the desired attribute</param>
        internal static T GetLatestAttribute<T>(Type type) where T : Attribute
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            while (type != null)
            {
                var attribute = (T)type.GetCustomAttributes(attributeType: typeof(T), inherit: false).SingleOrDefault();
                if (attribute != null)
                {
                    return attribute;
                }

                if (type == typeof(object))
                {
                    return null;
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}
