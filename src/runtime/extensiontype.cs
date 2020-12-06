using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Python.Runtime
{
    /// <summary>
    /// Base class for extensions whose instances *share* a single Python
    /// type object, such as the types that represent CLR methods, fields,
    /// etc. Instances implemented by this class do not support sub-typing.
    /// </summary>
    internal abstract class ExtensionType : ManagedType
    {
        public ExtensionType()
        {
            // Create a new PyObject whose type is a generated type that is
            // implemented by the particular concrete ExtensionType subclass.
            // The Python instance object is related to an instance of a
            // particular concrete subclass with a hidden CLR gchandle.

            BorrowedReference tp = TypeManager.GetTypeHandle(GetType());

            //int rc = (int)Marshal.ReadIntPtr(tp, TypeOffset.ob_refcnt);
            //if (rc > 1050)
            //{
            //    DebugUtil.Print("tp is: ", tp);
            //    DebugUtil.DumpType(tp);
            //}

            using var py = Runtime.PyType_GenericAlloc(tp, 0);

            GCHandle gc = GCHandle.Alloc(this);
            Marshal.WriteIntPtr(py.DangerousGetAddress(), ObjectOffset.GetDefaultGCHandleOffset(), (IntPtr)gc);

            // It is safe to store the reference to the type without incref,
            // because we also hold an instance of that type.
            tpHandle = tp.DangerousGetAddress();
            pyHandle = py.DangerousMoveToPointer();
            gcHandle = gc;

            // We have to support gc because the type machinery makes it very
            // hard not to - but we really don't have a need for it in most
            // concrete extension types, so untrack the object to save calls
            // from Python into the managed runtime that are pure overhead.
            Runtime.PyObject_GC_UnTrack(pyHandle);
        }


        internal static T GetManagedObject<T>(BorrowedReference ob)
            where T : ExtensionType
            => (T)GetManagedObject(ob, ObjectOffset.GetDefaultGCHandleOffset());
        [Obsolete]
        internal static new ExtensionType GetManagedObject(IntPtr ob)
            => (ExtensionType)GetManagedObject(new BorrowedReference(ob), ObjectOffset.GetDefaultGCHandleOffset());
        [Obsolete]
        internal static new ExtensionType GetManagedObject(BorrowedReference ob)
            => (ExtensionType)GetManagedObject(ob, ObjectOffset.GetDefaultGCHandleOffset());

        internal static bool IsExtensionType(BorrowedReference tp)
        {
            if (!IsManagedType(tp)) return false;
            var metaType = Runtime.PyObject_TYPE(tp);
            return metaType == Runtime.PyTypeType;
        }

        /// <summary>
        /// Common finalization code to support custom tp_deallocs.
        /// </summary>
        public static void FinalizeObject(ExtensionType self)
        {
            Debug.Assert(self.pyHandle != IntPtr.Zero);
            Runtime.PyObject_GC_Del(self.pyHandle);
            self.pyHandle = IntPtr.Zero;

            self.tpHandle = IntPtr.Zero;

            self.gcHandle.Free();
        }


        /// <summary>
        /// Type __setattr__ implementation.
        /// </summary>
        public static int tp_setattro(IntPtr ob, IntPtr key, IntPtr val)
        {
            var message = "type does not support setting attributes";
            if (val == IntPtr.Zero)
            {
                message = "readonly attribute";
            }
            Exceptions.SetError(Exceptions.TypeError, message);
            return -1;
        }


        /// <summary>
        /// Default dealloc implementation.
        /// </summary>
        public static void tp_dealloc(IntPtr ob)
        {
            // Clean up a Python instance of this extension type. This
            // frees the allocated Python object and decrefs the type.
            var self = GetManagedObject<ExtensionType>(new BorrowedReference(ob));
            FinalizeObject(self);
        }
    }
}
