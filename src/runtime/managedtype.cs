using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;

namespace Python.Runtime
{
    /// <summary>
    /// Common base class for all objects that are implemented in managed
    /// code. It defines the common fields that associate CLR and Python
    /// objects and common utilities to convert between those identities.
    /// </summary>
    [Serializable]
    internal abstract class ManagedType
    {
        internal enum TrackTypes
        {
            Untrack,
            Extension,
            Wrapper,
        }

        [NonSerialized]
        internal GCHandle gcHandle; // Native handle

        internal IntPtr pyHandle; // PyObject *
        internal IntPtr tpHandle; // PyType *

        internal BorrowedReference ObjectReference => new BorrowedReference(this.pyHandle);

        private static readonly Dictionary<ManagedType, TrackTypes> _managedObjs = new Dictionary<ManagedType, TrackTypes>();

        internal void IncrRefCount()
        {
            Runtime.XIncref(this.pyHandle);
        }

        internal void DecrRefCount()
        {
            Runtime.XDecref(this.pyHandle);
        }

        internal long RefCount
        {
            get
            {
                var gs = Runtime.PyGILState_Ensure();
                try
                {
                    return Runtime.Refcount(this.pyHandle);
                }
                finally
                {
                    Runtime.PyGILState_Release(gs);
                }
            }
        }

        internal GCHandle AllocGCHandle(TrackTypes track = TrackTypes.Untrack)
        {
            this.gcHandle = GCHandle.Alloc(this);
            if (track != TrackTypes.Untrack)
            {
                _managedObjs.Add(this, track);
            }
            return this.gcHandle;
        }

        internal void FreeGCHandle()
        {
            _managedObjs.Remove(this);
            if (this.gcHandle.IsAllocated)
            {
                this.gcHandle.Free();
                this.gcHandle = default;
            }
        }

        internal static ManagedType GetManagedObject(BorrowedReference ob)
            => GetManagedObject(ob.DangerousGetAddress());
        /// <summary>
        /// Given a Python object, return the associated managed object or null.
        /// </summary>
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
                        : Marshal.ReadIntPtr(ob, ObjectOffset.magic(tp));
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

        /// <summary>
        /// Given a Python object, return the associated managed object type or null.
        /// </summary>
        internal static ManagedType GetManagedObjectType(IntPtr ob)
        {
            if (ob != IntPtr.Zero)
            {
                IntPtr tp = Runtime.PyObject_TYPE(ob);
                var flags = Util.ReadCLong(tp, TypeOffset.tp_flags);
                if ((flags & TypeFlags.Managed) != 0)
                {
                    tp = Marshal.ReadIntPtr(tp, TypeOffset.magic());
                    var gc = (GCHandle)tp;
                    return (ManagedType)gc.Target;
                }
            }
            return null;
        }


        internal static ManagedType GetManagedObjectErr(IntPtr ob)
        {
            ManagedType result = GetManagedObject(ob);
            if (result == null)
            {
                Exceptions.SetError(Exceptions.TypeError, "invalid argument, expected CLR type");
            }
            return result;
        }


        internal static bool IsManagedType(BorrowedReference ob)
            => IsManagedType(ob.DangerousGetAddressOrNull());
        internal static bool IsManagedType(IntPtr ob)
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
                    return true;
                }
            }
            return false;
        }

        public bool IsTypeObject()
        {
            return this.pyHandle == this.tpHandle;
        }

        internal static IDictionary<ManagedType, TrackTypes> GetManagedObjects()
        {
            return _managedObjs;
        }

        internal static void ClearTrackedObjects()
        {
            _managedObjs.Clear();
        }

        internal static int PyVisit(IntPtr ob, IntPtr visit, IntPtr arg)
        {
            if (ob == IntPtr.Zero)
            {
                return 0;
            }
            var visitFunc = NativeCall.GetDelegate<Interop.ObjObjFunc>(visit);
            return visitFunc(ob, arg);
        }

        /// <summary>
        /// Wrapper for calling tp_clear
        /// </summary>
        internal void CallTypeClear()
        {
            if (this.tpHandle == IntPtr.Zero || this.pyHandle == IntPtr.Zero)
            {
                return;
            }
            var clearPtr = Marshal.ReadIntPtr(this.tpHandle, TypeOffset.tp_clear);
            if (clearPtr == IntPtr.Zero)
            {
                return;
            }
            var clearFunc = NativeCall.GetDelegate<Interop.InquiryFunc>(clearPtr);
            clearFunc(this.pyHandle);
        }

        /// <summary>
        /// Wrapper for calling tp_traverse
        /// </summary>
        internal void CallTypeTraverse(Interop.ObjObjFunc visitproc, IntPtr arg)
        {
            if (this.tpHandle == IntPtr.Zero || this.pyHandle == IntPtr.Zero)
            {
                return;
            }
            var traversePtr = Marshal.ReadIntPtr(this.tpHandle, TypeOffset.tp_traverse);
            if (traversePtr == IntPtr.Zero)
            {
                return;
            }
            var traverseFunc = NativeCall.GetDelegate<Interop.ObjObjArgFunc>(traversePtr);

            var visiPtr = Marshal.GetFunctionPointerForDelegate(visitproc);
            traverseFunc(this.pyHandle, visiPtr, arg);
        }

        protected void TypeClear()
        {
            ClearObjectDict(this.pyHandle);
        }

        internal void Save(InterDomainContext context)
        {
            this.OnSave(context);
        }

        internal void Load(InterDomainContext context)
        {
            this.OnLoad(context);
        }

        protected virtual void OnSave(InterDomainContext context) { }
        protected virtual void OnLoad(InterDomainContext context) { }

        protected static void ClearObjectDict(IntPtr ob)
        {
            IntPtr dict = GetObjectDict(ob);
            if (dict == IntPtr.Zero)
            {
                return;
            }
            SetObjectDict(ob, IntPtr.Zero);
            Runtime.XDecref(dict);
        }

        protected static IntPtr GetObjectDict(IntPtr ob)
        {
            IntPtr type = Runtime.PyObject_TYPE(ob);
            return Marshal.ReadIntPtr(ob, ObjectOffset.TypeDictOffset(type));
        }

        protected static void SetObjectDict(IntPtr ob, IntPtr value)
        {
            IntPtr type = Runtime.PyObject_TYPE(ob);
            Marshal.WriteIntPtr(ob, ObjectOffset.TypeDictOffset(type), value);
        }

        internal static Type[] PythonArgsToTypeArray(IntPtr arg)
        {
            return PythonArgsToTypeArray(arg, false);
        }

        internal static Type[] PythonArgsToTypeArray(IntPtr arg, bool mangleObjects)
        {
            // Given a PyObject * that is either a single type object or a
            // tuple of (managed or unmanaged) type objects, return a Type[]
            // containing the CLR Type objects that map to those types.
            IntPtr args = arg;
            var free = false;

            if (!Runtime.PyTuple_Check(arg))
            {
                args = Runtime.PyTuple_New((long)1);
                Runtime.XIncref(arg);
                Runtime.PyTuple_SetItem(args, (long)0, arg);
                free = true;
            }

            var n = Runtime.PyTuple_Size(args);
            var types = new Type[n];
            Type t = null;

            for (var i = 0; i < n; i++)
            {
                IntPtr op = Runtime.PyTuple_GetItem(args, (long)i);
                if (mangleObjects && (!Runtime.PyType_Check(op)))
                {
                    op = Runtime.PyObject_TYPE(op);
                }
                ManagedType mt = ManagedType.GetManagedObject(op);

                if (mt is ClassBase)
                {
                    MaybeType _type = ((ClassBase)mt).type;
                    t = _type.Valid ?  _type.Value : null;
                }
                else if (mt is CLRObject)
                {
                    object inst = ((CLRObject)mt).inst;
                    if (inst is Type)
                    {
                        t = inst as Type;
                    }
                }
                else
                {
                    t = Converter.GetTypeByAlias(op);
                }

                if (t == null)
                {
                    types = null;
                    break;
                }
                types[i] = t;
            }
            if (free)
            {
                Runtime.XDecref(args);
            }
            return types;
        }
    }
}
