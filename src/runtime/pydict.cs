using System;

namespace Python.Runtime
{
    /// <summary>
    /// Represents a Python dictionary object. See the documentation at
    /// PY2: https://docs.python.org/2/c-api/dict.html
    /// PY3: https://docs.python.org/3/c-api/dict.html
    /// for details.
    /// </summary>
    public class PyDict : PyObject
    {
        internal PyDict(BorrowedReference reference) : base(reference) { }
        internal PyDict(in StolenReference reference) : base(reference) { }

        /// <summary>
        /// Creates a new Python dictionary object.
        /// </summary>
        public PyDict() : base(Runtime.PyDict_New())
        {
            if (obj == IntPtr.Zero)
            {
                throw PythonException.ThrowLastAsClrException();
            }
        }


        /// <summary>
        /// Wraps existing dictionary object.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the given object is not a Python dictionary object
        /// </exception>
        public PyDict(PyObject o) : base(o is null ? throw new ArgumentNullException(nameof(o)) : o.Reference)
        {
            if (!IsDictType(o))
            {
                throw new ArgumentException("object is not a dict");
            }
        }


        /// <summary>
        /// IsDictType Method
        /// </summary>
        /// <remarks>
        /// Returns true if the given object is a Python dictionary.
        /// </remarks>
        public static bool IsDictType(PyObject value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return Runtime.PyDict_Check(value.obj);
        }


        /// <summary>
        /// HasKey Method
        /// </summary>
        /// <remarks>
        /// Returns true if the object key appears in the dictionary.
        /// </remarks>
        public bool HasKey(PyObject key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return Runtime.PyMapping_HasKey(obj, key.obj) != 0;
        }


        /// <summary>
        /// HasKey Method
        /// </summary>
        /// <remarks>
        /// Returns true if the string key appears in the dictionary.
        /// </remarks>
        public bool HasKey(string key)
        {
            using (var str = new PyString(key))
            {
                return HasKey(str);
            }
        }


        /// <summary>
        /// Keys Method
        /// </summary>
        /// <remarks>
        /// Returns a sequence containing the keys of the dictionary.
        /// </remarks>
        public PyObject Keys()
        {
            using var items = Runtime.PyDict_Keys(Reference);
            if (items.IsNull())
            {
                throw PythonException.ThrowLastAsClrException();
            }
            return items.MoveToPyObject();
        }


        /// <summary>
        /// Values Method
        /// </summary>
        /// <remarks>
        /// Returns a sequence containing the values of the dictionary.
        /// </remarks>
        public PyObject Values()
        {
            IntPtr items = Runtime.PyDict_Values(obj);
            if (items == IntPtr.Zero)
            {
                throw PythonException.ThrowLastAsClrException();
            }
            return new PyObject(items);
        }


        /// <summary>
        /// Items Method
        /// </summary>
        /// <remarks>
        /// Returns a sequence containing the items of the dictionary.
        /// </remarks>
        public PyObject Items()
        {
            var items = Runtime.PyDict_Items(this.Reference);
            try
            {
                if (items.IsNull())
                {
                    throw PythonException.ThrowLastAsClrException();
                }

                return items.MoveToPyObject();
            }
            finally
            {
                items.Dispose();
            }
        }


        /// <summary>
        /// Copy Method
        /// </summary>
        /// <remarks>
        /// Returns a copy of the dictionary.
        /// </remarks>
        public PyDict Copy()
        {
            var op = Runtime.PyDict_Copy(Reference);
            if (op.IsNull())
            {
                throw PythonException.ThrowLastAsClrException();
            }
            return new PyDict(op.Steal());
        }


        /// <summary>
        /// Update Method
        /// </summary>
        /// <remarks>
        /// Update the dictionary from another dictionary.
        /// </remarks>
        public void Update(PyObject other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));

            int result = Runtime.PyDict_Update(Reference, other.Reference);
            if (result < 0)
            {
                throw PythonException.ThrowLastAsClrException();
            }
        }


        /// <summary>
        /// Clear Method
        /// </summary>
        /// <remarks>
        /// Clears the dictionary.
        /// </remarks>
        public void Clear()
        {
            Runtime.PyDict_Clear(obj);
        }
    }
}
