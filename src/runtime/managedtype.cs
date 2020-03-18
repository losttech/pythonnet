using System;
using System.Runtime.InteropServices;

namespace Python.Runtime
{
    /// <summary>
    /// Common base class for all objects that are implemented in managed
    /// code. It defines the common fields that associate CLR and Python
    /// objects and common utilities to convert between those identities.
    /// </summary>
    internal abstract class ManagedType
    {
        internal GCHandle gcHandle; // Native handle
        internal IntPtr pyHandle; // PyObject *
        internal IntPtr tpHandle; // PyType *
        internal BorrowedReference Type => new BorrowedReference(this.tpHandle);
        internal BorrowedReference Instance => new BorrowedReference(this.pyHandle);


        /// <summary>
        /// Given a Python object, return the associated managed object or null.
        /// </summary>
        internal static ManagedType GetManagedObject(BorrowedReference ob)
        {
            if (!ob.IsNull)
            {
                var tp = Runtime.PyObject_TYPE(ob);
                if (tp == Runtime.PyTypeType || tp == Runtime.PyCLRMetaType)
                {
                    tp = ob;
                }

                if (IsManagedType(tp))
                {
                    int gcHandleOffset = tp == ob
                        ? TypeOffset.magic()
                        : ObjectOffset.ReflectedObjectGCHandle(ob);
                    IntPtr op = Marshal.ReadIntPtr(ob.DangerousGetAddress(), gcHandleOffset);
                    if (op == IntPtr.Zero)
                    {
                        return null;
                    }
                    var gc = (GCHandle)op;
                    return (ManagedType)gc.Target;
                }
            }
            return null;
        }

        [Obsolete("Use GetManagedObject(BorrowedReference)")]
        internal static ManagedType GetManagedObject(IntPtr ob)
            => GetManagedObject(new BorrowedReference(ob));

        internal static T GetManagedObject<T>(BorrowedReference ob)
            where T : ExtensionType
            => (T)GetManagedObject(ob, ObjectOffset.GetDefaultGCHandleOffset());

        internal static ManagedType GetManagedObject(BorrowedReference ob, int gcHandleOffset)
        {
            if (ob.IsNull) throw new ArgumentNullException(nameof(ob));
            ObjectOffset.ClrGcHandleOffsetAssertSanity(gcHandleOffset);

            IntPtr gcHandleValue = Marshal.ReadIntPtr(ob.DangerousGetAddress(), gcHandleOffset);
            var gcHandle = (GCHandle)gcHandleValue;
            return (ManagedType)gcHandle.Target;
        }

        /// <summary>
        /// Checks if specified type is a CLR type
        /// </summary>
        internal static bool IsManagedType(IntPtr tp)
        {
            var flags = Util.ReadCLong(tp, TypeOffset.tp_flags);
            return (flags & TypeFlags.Managed) != 0;
        }

        /// <summary>Checks if specified type is a CLR type</summary>
        internal static bool IsManagedType(BorrowedReference type)
            => IsManagedType(type.DangerousGetAddress());
    }
}
