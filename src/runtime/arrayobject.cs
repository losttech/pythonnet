using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Python.Runtime
{
    /// <summary>
    /// Implements a Python type for managed arrays. This type is essentially
    /// the same as a ClassObject, except that it provides sequence semantics
    /// to support natural array usage (indexing) from Python.
    /// </summary>
    internal class ArrayObject : ClassBase
    {
        internal ArrayObject(Type tp) : base(tp)
        {
        }

        internal override bool CanSubclass()
        {
            return false;
        }

        public static IntPtr tp_new(IntPtr tp, IntPtr args, IntPtr kw)
        {
            var self = GetManagedObject(tp) as ArrayObject;
            if (Runtime.PyTuple_Size(args) != 1)
            {
                return Exceptions.RaiseTypeError("array expects 1 argument");
            }
            IntPtr op = Runtime.PyTuple_GetItem(args, 0);
            object result;

            if (!Converter.ToManaged(op, self.type, out result, true))
            {
                return IntPtr.Zero;
            }
            return CLRObject.GetInstHandle(result, tp);
        }


        /// <summary>
        /// Implements __getitem__ for array types.
        /// </summary>
        public static IntPtr mp_subscript(IntPtr ob, IntPtr idx)
        {
            var obj = (CLRObject)GetManagedObject(ob);
            var items = obj.inst as Array;
            Type itemType = obj.inst.GetType().GetElementType();
            int rank = items.Rank;
            int index;
            object value;

            // Note that CLR 1.0 only supports int indexes - methods to
            // support long indices were introduced in 1.1. We could
            // support long indices automatically, but given that long
            // indices are not backward compatible and a relative edge
            // case, we won't bother for now.

            // Single-dimensional arrays are the most common case and are
            // cheaper to deal with than multi-dimensional, so check first.

            if (rank == 1)
            {
                index = Runtime.PyInt_AsLong(idx);

                if (Exceptions.ErrorOccurred())
                {
                    return Exceptions.RaiseTypeError("invalid index value");
                }

                if (index < 0)
                {
                    index = items.Length + index;
                }

                try
                {
                    value = items.GetValue(index);
                }
                catch (IndexOutOfRangeException)
                {
                    Exceptions.SetError(Exceptions.IndexError, "array index out of range");
                    return IntPtr.Zero;
                }

                return Converter.ToPython(value, itemType);
            }

            // Multi-dimensional arrays can be indexed a la: list[1, 2, 3].

            if (!Runtime.PyTuple_Check(idx))
            {
                Exceptions.SetError(Exceptions.TypeError, "invalid index value");
                return IntPtr.Zero;
            }

            var count = Runtime.PyTuple_Size(idx);

            var args = new int[count];

            for (var i = 0; i < count; i++)
            {
                IntPtr op = Runtime.PyTuple_GetItem(idx, i);
                index = Runtime.PyInt_AsLong(op);

                if (Exceptions.ErrorOccurred())
                {
                    return Exceptions.RaiseTypeError("invalid index value");
                }

                if (index < 0)
                {
                    index = items.GetLength(i) + index;
                }

                args.SetValue(index, i);
            }

            try
            {
                value = items.GetValue(args);
            }
            catch (IndexOutOfRangeException)
            {
                Exceptions.SetError(Exceptions.IndexError, "array index out of range");
                return IntPtr.Zero;
            }

            return Converter.ToPython(value, itemType);
        }


        /// <summary>
        /// Implements __setitem__ for array types.
        /// </summary>
        public static int mp_ass_subscript(IntPtr ob, IntPtr idx, IntPtr v)
        {
            var obj = (CLRObject)GetManagedObject(ob);
            var items = obj.inst as Array;
            Type itemType = obj.inst.GetType().GetElementType();
            int rank = items.Rank;
            int index;
            object value;

            if (items.IsReadOnly)
            {
                Exceptions.RaiseTypeError("array is read-only");
                return -1;
            }

            if (!Converter.ToManaged(v, itemType, out value, true))
            {
                return -1;
            }

            if (rank == 1)
            {
                index = Runtime.PyInt_AsLong(idx);

                if (Exceptions.ErrorOccurred())
                {
                    Exceptions.RaiseTypeError("invalid index value");
                    return -1;
                }

                if (index < 0)
                {
                    index = items.Length + index;
                }

                try
                {
                    items.SetValue(value, index);
                }
                catch (IndexOutOfRangeException)
                {
                    Exceptions.SetError(Exceptions.IndexError, "array index out of range");
                    return -1;
                }

                return 0;
            }

            if (!Runtime.PyTuple_Check(idx))
            {
                Exceptions.RaiseTypeError("invalid index value");
                return -1;
            }

            var count = Runtime.PyTuple_Size(idx);
            var args = new int[count];

            for (var i = 0; i < count; i++)
            {
                IntPtr op = Runtime.PyTuple_GetItem(idx, i);
                index = Runtime.PyInt_AsLong(op);

                if (Exceptions.ErrorOccurred())
                {
                    Exceptions.RaiseTypeError("invalid index value");
                    return -1;
                }

                if (index < 0)
                {
                    index = items.GetLength(i) + index;
                }

                args.SetValue(index, i);
            }

            try
            {
                items.SetValue(value, args);
            }
            catch (IndexOutOfRangeException)
            {
                Exceptions.SetError(Exceptions.IndexError, "array index out of range");
                return -1;
            }

            return 0;
        }


        /// <summary>
        /// Implements __contains__ for array types.
        /// </summary>
        public static int sq_contains(IntPtr ob, IntPtr v)
        {
            var obj = (CLRObject)GetManagedObject(ob);
            Type itemType = obj.inst.GetType().GetElementType();
            var items = obj.inst as IList;
            object value;

            if (!Converter.ToManaged(v, itemType, out value, false))
            {
                return 0;
            }

            if (items.Contains(value))
            {
                return 1;
            }

            return 0;
        }

        #region Buffer protocol
        static int GetBuffer(BorrowedReference obj, out Py_buffer buffer, PyBUF flags)
        {
            buffer = default;

            if (flags == PyBUF.SIMPLE)
            {
                Exceptions.SetError(Exceptions.BufferError, "SIMPLE not implemented");
                return -1;
            }
            if ((flags & PyBUF.F_CONTIGUOUS) == PyBUF.F_CONTIGUOUS)
            {
                Exceptions.SetError(Exceptions.BufferError, "only C-contiguous supported");
                return -1;
            }
            var self = (Array)((CLRObject)GetManagedObject(obj)).inst;
            Type itemType = self.GetType().GetElementType();

            bool formatRequested = (flags & PyBUF.FORMATS) != 0;
            string format = formatRequested ? GetFormat(itemType) : null;
            if (formatRequested && format is null)
            {
                Exceptions.SetError(Exceptions.BufferError, "unsupported element type: " + itemType.Name);
                return -1;
            }
            var gcHandle = GCHandle.Alloc(self, GCHandleType.Pinned);

            int itemSize = Marshal.SizeOf(itemType);
            IntPtr[] shape = GetShape(self);
            IntPtr[] strides = GetStrides(shape, itemSize);
            buffer = new Py_buffer
            {
                buf = gcHandle.AddrOfPinnedObject(),
                obj = Runtime.SelfIncRef(obj.DangerousGetAddress()),
                len = (IntPtr)(self.LongLength*itemSize),
                itemsize = (IntPtr)itemSize,
                _readonly = false,
                ndim = self.Rank,
                format = format,
                shape = ToUnmanaged(shape),
                strides = (flags & PyBUF.STRIDES) == PyBUF.STRIDES ? ToUnmanaged(strides) : IntPtr.Zero,
                suboffsets = IntPtr.Zero,
                _internal = (IntPtr)gcHandle,
            };

            return 0;
        }
        static void ReleaseBuffer(BorrowedReference obj, ref Py_buffer buffer)
        {
            if (buffer._internal == IntPtr.Zero) return;

            UnmanagedFree(ref buffer.shape);
            UnmanagedFree(ref buffer.strides);
            UnmanagedFree(ref buffer.suboffsets);

            var gcHandle = (GCHandle)buffer._internal;
            gcHandle.Free();
            buffer._internal = IntPtr.Zero;
        }

        static IntPtr[] GetStrides(IntPtr[] shape, long itemSize)
        {
            var result = new IntPtr[shape.Length];
            result[shape.Length - 1] = new IntPtr(itemSize);
            for (int dim = shape.Length - 2; dim >= 0; dim--)
            {
                itemSize *= shape[dim + 1].ToInt64();
                result[dim] = new IntPtr(itemSize);
            }
            return result;
        }
        static IntPtr[] GetShape(Array array)
        {
            var result = new IntPtr[array.Rank];
            for (int i = 0; i < result.Length; i++)
                result[i] = (IntPtr)array.GetLongLength(i);
            return result;
        }

        static void UnmanagedFree(ref IntPtr address)
        {
            if (address == IntPtr.Zero) return;

            Marshal.FreeHGlobal(address);
            address = IntPtr.Zero;
        }
        static unsafe IntPtr ToUnmanaged<T>(T[] array) where T : unmanaged
        {
            IntPtr result = Marshal.AllocHGlobal(checked(Marshal.SizeOf(typeof(T)) * array.Length));
            fixed (T* ptr = array)
            {
                var @out = (T*)result;
                for (int i = 0; i < array.Length; i++)
                    @out[i] = ptr[i];
            }
            return result;
        }

        static readonly Dictionary<Type, string> ItemFormats = new Dictionary<Type, string>
        {
            [typeof(byte)] = "B",
            [typeof(sbyte)] = "b",

            [typeof(bool)] = "?",

            [typeof(short)] = "h",
            [typeof(ushort)] = "H",
            [typeof(int)] = "i",
            [typeof(uint)] = "I",
            [typeof(long)] = "q",
            [typeof(ulong)] = "Q",

            [typeof(IntPtr)] = "n",
            [typeof(UIntPtr)] = "N",

            // TODO: half = "e"
            [typeof(float)] = "f",
            [typeof(double)] = "d",
        };

        static string GetFormat(Type elementType)
            => ItemFormats.TryGetValue(elementType, out string result) ? result : null;

        static readonly GetBufferProc getBufferProc = GetBuffer;
        static readonly ReleaseBufferProc releaseBufferProc = ReleaseBuffer;
        static readonly IntPtr BufferProcsAddress = AllocateBufferProcs();
        static IntPtr AllocateBufferProcs()
        {
            var procs = new PyBufferProcs
            {
                Get = Marshal.GetFunctionPointerForDelegate(getBufferProc),
                Release = Marshal.GetFunctionPointerForDelegate(releaseBufferProc),
            };
            IntPtr result = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PyBufferProcs)));
            Marshal.StructureToPtr(procs, result, fDeleteOld: false);
            return result;
        }
        #endregion

        public static void InitializeSlots(IntPtr type, ISet<string> initialized)
        {
            if (initialized.Add(nameof(TypeOffset.tp_as_buffer)))
            {
                // TODO: only for unmanaged arrays
                TypeManager.InitializeSlot(type, BufferProcsAddress, nameof(TypeOffset.tp_as_buffer));
            }
        }
    }
}
