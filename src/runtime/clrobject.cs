using System;
using System.Runtime.InteropServices;

namespace Python.Runtime
{
    internal class CLRObject : ManagedType
    {
        internal object inst;

        internal CLRObject(object ob, BorrowedReference tp)
        {
            NewReference py = Runtime.PyType_GenericAlloc(tp, 0);

            var flags = (TypeFlags)Util.ReadCLong(tp.DangerousGetAddress(), TypeOffset.tp_flags);
            if ((flags & TypeFlags.Subclass) != 0)
            {
                IntPtr dict = Marshal.ReadIntPtr(py.DangerousGetAddress(), ObjectOffset.TypeDictOffset(tp));
                if (dict == IntPtr.Zero)
                {
                    dict = Runtime.PyDict_New();
                    Marshal.WriteIntPtr(py.DangerousGetAddress(), ObjectOffset.TypeDictOffset(tp), dict);
                }
            }

            // it is safe to "borrow" type pointer, because we also own a reference to an instance
            tpHandle = tp.DangerousGetAddress();
            pyHandle = py.DangerousMoveToPointer();
            GCHandle gc = GCHandle.Alloc(this);
            int gcHandleOffset = ObjectOffset.ReflectedObjectGCHandle(this.Instance);
            Marshal.WriteIntPtr(pyHandle, gcHandleOffset, (IntPtr)gc);
            gcHandle = gc;
            inst = ob;

            // Fix the BaseException args (and __cause__ in case of Python 3)
            // slot if wrapping a CLR exception
            Exceptions.SetArgsAndCause(pyHandle);
        }


        static CLRObject GetInstance(object ob, IntPtr pyType)
        {
            return new CLRObject(ob, new BorrowedReference(pyType));
        }


        static CLRObject GetInstance(object ob)
        {
            ClassBase cc = ClassManager.GetClass(ob.GetType());
            return GetInstance(ob, cc.tpHandle);
        }


        internal static IntPtr GetInstHandle(object ob, IntPtr pyType)
        {
            CLRObject co = GetInstance(ob, pyType);
            return co.pyHandle;
        }


        internal static IntPtr GetInstHandle(object ob, Type type)
        {
            ClassBase cc = ClassManager.GetClass(type);
            CLRObject co = GetInstance(ob, cc.tpHandle);
            return co.pyHandle;
        }


        internal static IntPtr GetInstHandle(object ob)
        {
            CLRObject co = GetInstance(ob);
            return co.pyHandle;
        }

        /// <summary>
        /// Creates <see cref="CLRObject"/> proxy for the given object,
        /// and returns a <see cref="NewReference"/> to it.
        /// </summary>
        internal static NewReference MakeNewReference(object obj)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));

            // TODO: CLRObject currently does not have Dispose or finalizer which might change in the future
            IntPtr handle = GetInstHandle(obj);
            DebugUtil.AssertRefcount(handle);
            return NewReference.DangerousFromPointer(handle);
        }
    }
}
