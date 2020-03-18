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
        [Obsolete("Use GetManagedObject(BorrowedReference)")]
        internal static ManagedType GetManagedObject(IntPtr ob)
        {
            if (ob != IntPtr.Zero)
            {
                IntPtr tp = Runtime.PyObject_TYPE(ob);
                if (tp == Runtime.PyTypeType || tp == Runtime.PyCLRMetaType)
                {
                    tp = ob;
                }

                var flags = Util.ReadCLong(tp, TypeOffset.tp_flags);
                if ((flags & TypeFlags.Managed) != 0)
                {
                    IntPtr op = tp == ob
                        ? Marshal.ReadIntPtr(tp, TypeOffset.magic())
                        : Marshal.ReadIntPtr(ob, ObjectOffset.magic(ob));
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

        internal static ManagedType GetManagedObject(BorrowedReference ob)
            => ob.IsNull ? null : GetManagedObject(ob.DangerousGetAddress());


        internal static ManagedType GetManagedObjectErr(IntPtr ob)
        {
            ManagedType result = GetManagedObject(ob);
            if (result == null)
            {
                Exceptions.SetError(Exceptions.TypeError, "invalid argument, expected CLR type");
            }
            return result;
        }
    }
}
