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
    public class PyIter : PyObject, IEnumerator<object>
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
        internal PyIter(BorrowedReference reference) : base(reference) { }

        protected override void Dispose(bool disposing)
        {
            _current = null;
            base.Dispose(disposing);
        }

        public bool MoveNext()
        {
            var next = Runtime.PyIter_Next(Reference);
            if (next.IsNull())
            {
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

        public object Current
        {
            get { return _current; }
        }
    }
}
