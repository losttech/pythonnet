using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Python.Runtime
{
    [Serializable]
    internal class CLRObject : ManagedType
    {
        internal object inst;

        internal CLRObject(object ob, IntPtr tp)
        {
            System.Diagnostics.Debug.Assert(tp != IntPtr.Zero);
            IntPtr py = Runtime.PyType_GenericAlloc(tp, 0);

            long flags = Util.ReadCLong(tp, TypeOffset.tp_flags);
            if ((flags & TypeFlags.Subclass) != 0)
            {
                IntPtr dict = Marshal.ReadIntPtr(py, ObjectOffset.TypeDictOffset(tp));
                if (dict == IntPtr.Zero)
                {
                    dict = Runtime.PyDict_New();
                    Marshal.WriteIntPtr(py, ObjectOffset.TypeDictOffset(tp), dict);
                }
            }

            GCHandle gc = AllocGCHandle(TrackTypes.Wrapper);
            Marshal.WriteIntPtr(py, ObjectOffset.magic(tp), (IntPtr)gc);
            tpHandle = tp;
            pyHandle = py;
            inst = ob;

            // Fix the BaseException args (and __cause__ in case of Python 3)
            // slot if wrapping a CLR exception
            SetArgsAndCause(py);
        }

        protected CLRObject()
        {
        }

        static CLRObject GetInstance(object ob, IntPtr pyType)
        {
            return new CLRObject(ob, pyType);
        }


        static CLRObject GetInstance(object ob)
        {
            ClassBase cc = ClassManager.GetClass(ob.GetType());
            return GetInstance(ob, cc.tpHandle);
        }

        internal static NewReference GetInstHandle(object ob, BorrowedReference pyType)
        {
            CLRObject co = GetInstance(ob, pyType.DangerousGetAddress());
            return NewReference.DangerousFromPointer(co.pyHandle);
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

        internal static CLRObject Restore(object ob, IntPtr pyHandle, InterDomainContext context)
        {
            CLRObject co = new CLRObject()
            {
                inst = ob,
                pyHandle = pyHandle,
                tpHandle = Runtime.PyObject_TYPE(pyHandle)
            };
            Debug.Assert(co.tpHandle != IntPtr.Zero);
            co.Load(context);
            return co;
        }

        protected override void OnSave(InterDomainContext context)
        {
            base.OnSave(context);
            Runtime.XIncref(pyHandle);
        }

        protected override void OnLoad(InterDomainContext context)
        {
            base.OnLoad(context);
            GCHandle gc = AllocGCHandle(TrackTypes.Wrapper);
            Marshal.WriteIntPtr(pyHandle, ObjectOffset.magic(tpHandle), (IntPtr)gc);
        }

        /// <summary>
        /// Set the 'args' slot on a python exception object that wraps
        /// a CLR exception. This is needed for pickling CLR exceptions as
        /// BaseException_reduce will only check the slots, bypassing the
        /// __getattr__ implementation, and thus dereferencing a NULL
        /// pointer.
        /// </summary>
        /// <param name="ob">The python object wrapping </param>
        private static void SetArgsAndCause(IntPtr ob)
        {
            // e: A CLR Exception
            Exception e = ExceptionClassObject.ToException(ob);
            if (e == null)
            {
                return;
            }

            IntPtr args;
            if (!string.IsNullOrEmpty(e.Message))
            {
                args = Runtime.PyTuple_New(1);
                IntPtr msg = Runtime.PyUnicode_FromString(e.Message);
                Runtime.PyTuple_SetItem(args, 0, msg);
            }
            else
            {
                args = Runtime.PyTuple_New(0);
            }

            Marshal.WriteIntPtr(ob, ExceptionOffset.args, args);

            if (e.InnerException != null)
            {
                // Note: For an AggregateException, InnerException is only the first of the InnerExceptions.
                IntPtr cause = CLRObject.GetInstHandle(e.InnerException);
                Marshal.WriteIntPtr(ob, ExceptionOffset.cause, cause);
            }
        }
    }
}
