using System;

namespace Python.Runtime
{
    /// <summary>
    /// Represents a Python integer object. See the documentation at
    /// PY2: https://docs.python.org/2/c-api/int.html
    /// PY3: No equivalent
    /// for details.
    /// </summary>
    public class PyInt : PyNumber
    {
        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new PyInt from an existing object reference. Note
        /// that the instance assumes ownership of the object reference.
        /// The object reference is not checked for type-correctness.
        /// </remarks>
        public PyInt(IntPtr ptr) : base(ptr)
        {
        }


        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Copy constructor - obtain a PyInt from a generic PyObject. An
        /// ArgumentException will be thrown if the given object is not a
        /// Python int object.
        /// </remarks>
        public PyInt(PyObject o) : base(FromObject(o))
        {
        }

        /// <summary>Copy constructor</summary>
        public PyInt(PyInt other) : base(FromPyInt(other)) { }

        private static IntPtr FromObject(PyObject o)
        {
            if (o == null || !IsIntType(o))
            {
                throw new ArgumentException("object is not an int");
            }
            Runtime.XIncref(o.obj);
            return o.obj;
        }

        private static IntPtr FromPyInt(PyInt o)
        {
            Runtime.XIncref(o.obj);
            return o.obj;
        }

        private static IntPtr FromInt(int value)
        {
            IntPtr val = Runtime.PyInt_FromInt32(value);
            PythonException.ThrowIfIsNull(val);
            return val;
        }

        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python int from an int32 value.
        /// </remarks>
        public PyInt(int value) : base(FromInt(value))
        {
        }


        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python int from a uint32 value.
        /// </remarks>
        [CLSCompliant(false)]
        public PyInt(uint value) : base(FromLong(value))
        {
        }


        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python int from an int64 value.
        /// </remarks>
        public PyInt(long value) : base(FromLong(value))
        {
        }

        private static IntPtr FromLong(long value)
        {
            IntPtr val = Runtime.PyInt_FromInt64(value);
            PythonException.ThrowIfIsNull(val);
            return val;
        }


        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python int from a uint64 value.
        /// </remarks>
        [CLSCompliant(false)]
        public PyInt(ulong value) : base(FromLong((long)value))
        {
        }


        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python int from an int16 value.
        /// </remarks>
        public PyInt(short value) : this((int)value)
        {
        }


        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python int from a uint16 value.
        /// </remarks>
        [CLSCompliant(false)]
        public PyInt(ushort value) : this((int)value)
        {
        }


        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python int from a byte value.
        /// </remarks>
        public PyInt(byte value) : this((int)value)
        {
        }


        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python int from an sbyte value.
        /// </remarks>
        [CLSCompliant(false)]
        public PyInt(sbyte value) : this((int)value)
        {
        }


        private static IntPtr FromString(string value)
        {
            IntPtr val = Runtime.PyInt_FromString(value, IntPtr.Zero, 0);
            PythonException.ThrowIfIsNull(val);
            return val;
        }

        /// <summary>
        /// PyInt Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python int from a string value.
        /// </remarks>
        public PyInt(string value) : base(FromString(value))
        {
        }


        /// <summary>
        /// IsIntType Method
        /// </summary>
        /// <remarks>
        /// Returns true if the given object is a Python int.
        /// </remarks>
        public static bool IsIntType(PyObject value)
        {
            return Runtime.PyInt_Check(value.obj);
        }


        /// <summary>
        /// AsInt Method
        /// </summary>
        /// <remarks>
        /// Convert a Python object to a Python int if possible, raising
        /// a PythonException if the conversion is not possible. This is
        /// equivalent to the Python expression "int(object)".
        /// </remarks>
        public static PyInt AsInt(PyObject value)
        {
            IntPtr op = Runtime.PyNumber_Int(value.obj);
            PythonException.ThrowIfIsNull(op);
            return new PyInt(op);
        }


        /// <summary>
        /// ToInt16 Method
        /// </summary>
        /// <remarks>
        /// Return the value of the Python int object as an int16.
        /// </remarks>
        public short ToInt16()
        {
            return Convert.ToInt16(ToInt32());
        }


        /// <summary>
        /// ToInt32 Method
        /// </summary>
        /// <remarks>
        /// Return the value of the Python int object as an int32.
        /// </remarks>
        public int ToInt32()
        {
            return Runtime.PyInt_AsLong(obj);
        }


        /// <summary>
        /// ToInt64 Method
        /// </summary>
        /// <remarks>
        /// Return the value of the Python int object as an int64.
        /// </remarks>
        public long ToInt64()
        {
            return Runtime.PyLong_AsLongLong(Reference);
        }

        [CLSCompliant(false)]
        public ulong ToUInt64() => Runtime.PyLong_AsUnsignedLong64(obj);

        public static implicit operator long(PyInt pyInt) => pyInt.ToInt64();
        public static implicit operator int(PyInt pyInt) => pyInt.ToInt32();
        public static implicit operator short(PyInt pyInt) => pyInt.ToInt16();
        [CLSCompliant(false)]
        public static implicit operator sbyte(PyInt pyInt) => checked((sbyte)pyInt.ToInt32());

        [CLSCompliant(false)]
        public static implicit operator byte(PyInt pyInt) => checked((byte)pyInt.ToInt32());
        [CLSCompliant(false)]
        public static implicit operator ushort(PyInt pyInt) => checked((ushort)pyInt.ToInt32());
        [CLSCompliant(false)]
        public static implicit operator uint(PyInt pyInt) => checked((uint)pyInt.ToUInt64());
        [CLSCompliant(false)]
        public static implicit operator ulong(PyInt pyInt) => checked(pyInt.ToUInt64());

    }
}
