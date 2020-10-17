using System;
using System.Collections.Generic;

namespace Python.Runtime
{
    /// <summary>
    /// Represents a standard Python iterator object. See the documentation at
    /// PY2: https://docs.python.org/2/c-api/iterator.html
    /// PY3: https://docs.python.org/3/c-api/iterator.html
    /// for details.
    /// </summary>
    public class PyIter : PyObject, IEnumerator<PyObject>
    {
        private PyObject _current;

        /// <summary>
        /// PyIter Constructor
        /// </summary>
        /// <remarks>
        /// Creates a new PyIter from an existing iterator reference. Note
        /// that the instance assumes ownership of the object reference.
        /// The object reference is not checked for type-correctness.
        /// </remarks>
        public PyIter(IntPtr ptr) : base(ptr)
        {
        }
        /// <summary>
        /// Creates new <see cref="PyIter"/> from an untyped reference to Python object.
        /// </summary>
        public PyIter(PyObject pyObject) : base(FromPyObject(pyObject)) { }
        static BorrowedReference FromPyObject(PyObject pyObject) {
            if (pyObject is null) throw new ArgumentNullException(nameof(pyObject));

            if (!Runtime.PyIter_Check(pyObject.Reference))
                throw new ArgumentException("Object does not support iterator protocol");

            return pyObject.Reference;
        }

        internal PyIter(BorrowedReference reference) : base(reference) { }

        protected override void Dispose(bool disposing)
        {
            _current = null;
            base.Dispose(disposing);
        }

        public bool MoveNext()
        {
            using var next = Runtime.PyIter_Next(Reference);
            if (next.IsNull())
            {
                if (Exceptions.ErrorOccurred())
                    throw PythonException.ThrowLastAsClrException();

                // dispose of the previous object, if there was one
                _current = null;
                return false;
            }

            _current = next.MoveToPyObject();
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public PyObject Current => _current;
        object System.Collections.IEnumerator.Current => this.Current;
    }
}
