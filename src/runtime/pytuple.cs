using System;

namespace Python.Runtime
{
    /// <summary>
    /// Represents a Python tuple object. See the documentation at
    /// PY2: https://docs.python.org/2/c-api/tupleObjects.html
    /// PY3: https://docs.python.org/3/c-api/tupleObjects.html
    /// for details.
    /// </summary>
    public class PyTuple : PySequence
    {
        /// <summary>
        /// PyTuple Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new PyTuple from an existing object reference. Note
        /// that the instance assumes ownership of the object reference.
        /// The object reference is not checked for type-correctness.
        /// </remarks>
        public PyTuple(IntPtr ptr) : base(ptr)
        {
        }


        /// <summary>
        /// PyTuple Constructor
        /// </summary>
        /// <remarks>
        /// Copy constructor - obtain a PyTuple from a generic PyObject. An
        /// ArgumentException will be thrown if the given object is not a
        /// Python tuple object.
        /// </remarks>
        public PyTuple(PyObject o):base(FromPyObject(o))
        {
        }

        static IntPtr FromPyObject(PyObject o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));

            if (!IsTupleType(o)) {
                throw new ArgumentException("object is not a tuple");
            }
            Runtime.XIncref(o.obj);
            return o.obj;
        }


        /// <summary>
        /// PyTuple Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new empty PyTuple.
        /// </remarks>
        public PyTuple():base(Exceptions.ErrorOccurredCheck(Runtime.PyTuple_New(0)))
        {
        }


        /// <summary>
        /// PyTuple Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new PyTuple from an array of PyObject instances.
        /// <para />
        /// See caveats about PyTuple_SetItem:
        /// https://www.coursehero.com/file/p4j2ogg/important-exceptions-to-this-rule-PyTupleSetItem-and-PyListSetItem-These/
        /// </remarks>
        public PyTuple(PyObject[] items):base(FromItems(items))
        {
        }

        static IntPtr FromItems(PyObject[] items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            int count = items.Length;
            IntPtr obj = Runtime.PyTuple_New(count);
            try {
                for (var i = 0; i < count; i++) {
                    if (items[i] == null) throw new ArgumentNullException();

                    IntPtr ptr = items[i].obj;
                    Runtime.PyTuple_SetItem(obj, i, ptr);
                    Runtime.CheckExceptionOccurred();
                    Runtime.XIncref(ptr);
                }
            } catch {
                Runtime.XDecref(obj);
                throw;
            }

            return obj;
        }


        /// <summary>
        /// IsTupleType Method
        /// </summary>
        /// <remarks>
        /// Returns true if the given object is a Python tuple.
        /// </remarks>
        public static bool IsTupleType(PyObject value)
        {
            return Runtime.PyTuple_Check(value.obj);
        }


        /// <summary>
        /// AsTuple Method
        /// </summary>
        /// <remarks>
        /// Convert a Python object to a Python tuple if possible, raising
        /// a PythonException if the conversion is not possible. This is
        /// equivalent to the Python expression "tuple(object)".
        /// </remarks>
        public static PyTuple AsTuple(PyObject value)
        {
            IntPtr op = Runtime.PySequence_Tuple(value.obj);
            Runtime.CheckExceptionOccurred();
            return new PyTuple(op);
        }

        /// <summary>
        /// Create a <see cref="PyTuple"/> with single element
        /// </summary>
        public static PyTuple FromSingleElement(PyObject value)
            => new PyTuple(items: new []{value});
    }
}
