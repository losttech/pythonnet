using System;

namespace Python.Runtime
{
    /// <summary>
    /// Represents a standard Python list object. See the documentation at
    /// PY2: https://docs.python.org/2/c-api/list.html
    /// PY3: https://docs.python.org/3/c-api/list.html
    /// for details.
    /// </summary>
    public class PyList : PySequence
    {
        /// <summary>
        /// Gets an instance of <see cref="PyList"/> representing the same object as the given object.
        /// </summary>
        public static PyList Wrap(PyObject o)
        {
            if (o is null)
            {
                throw new ArgumentNullException(nameof(o));
            }
            if (!IsListType(o))
            {
                throw new ArgumentException("object is not a list");
            }
            return new PyList(o.Reference);
        }

        /// <summary>
        /// PyList Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new PyList from an existing object reference. Note
        /// that the instance assumes ownership of the object reference.
        /// The object reference is not checked for type-correctness.
        /// </remarks>
        public PyList(IntPtr ptr) : base(ptr)
        {
        }

        /// <summary>
        /// PyList Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new PyList from an existing object reference.
        /// The object reference is not checked for type-correctness.
        /// </remarks>
        internal PyList(BorrowedReference reference) : base(reference)
        {
        }

        /// <summary>
        /// PyList Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new empty Python list object.
        /// </remarks>
        public PyList(): base(Exceptions.ErrorCheck(Runtime.PyList_New(0)))
        {
        }


        /// <summary>
        /// PyList Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new Python list object from an array of PyObjects.
        /// </remarks>
        public PyList(PyObject[] items)
        {
            int count = items.Length;
            obj = Runtime.PyList_New(count);
            for (var i = 0; i < count; i++)
            {
                IntPtr ptr = items[i].obj;
                Runtime.XIncref(ptr);
                int r = Runtime.PyList_SetItem(obj, i, ptr);
                if (r < 0)
                {
                    throw PythonException.ThrowLastAsClrException();
                }
            }
        }


        /// <summary>
        /// IsListType Method
        /// </summary>
        /// <remarks>
        /// Returns true if the given object is a Python list.
        /// </remarks>
        public static bool IsListType(PyObject value)
        {
            return Runtime.PyList_Check(value.obj);
        }


        /// <summary>
        /// AsList Method
        /// </summary>
        /// <remarks>
        /// Converts a Python object to a Python list if possible, raising
        /// a PythonException if the conversion is not possible. This is
        /// equivalent to the Python expression "list(object)".
        /// </remarks>
        public static PyList AsList(PyObject value)
        {
            IntPtr op = Runtime.PySequence_List(value.obj);
            Exceptions.ErrorCheck(op);
            return new PyList(op);
        }


        /// <summary>
        /// Append Method
        /// </summary>
        /// <remarks>
        /// Append an item to the list object.
        /// </remarks>
        public void Append(PyObject item)
        {
            int r = Runtime.PyList_Append(this.Reference, item.obj);
            if (r < 0)
            {
                throw PythonException.ThrowLastAsClrException();
            }
        }

        /// <summary>
        /// Insert Method
        /// </summary>
        /// <remarks>
        /// Insert an item in the list object at the given index.
        /// </remarks>
        public void Insert(int index, PyObject item)
        {
            int r = Runtime.PyList_Insert(this.Reference, index, item.obj);
            if (r < 0)
            {
                throw PythonException.ThrowLastAsClrException();
            }
        }


        /// <summary>
        /// Reverse Method
        /// </summary>
        /// <remarks>
        /// Reverse the order of the list object in place.
        /// </remarks>
        public void Reverse()
        {
            int r = Runtime.PyList_Reverse(this.Reference);
            if (r < 0)
            {
                throw PythonException.ThrowLastAsClrException();
            }
        }


        /// <summary>
        /// Sort Method
        /// </summary>
        /// <remarks>
        /// Sort the list in place.
        /// </remarks>
        public void Sort()
        {
            int r = Runtime.PyList_Sort(this.Reference);
            if (r < 0)
            {
                throw PythonException.ThrowLastAsClrException();
            }
        }
    }
}
