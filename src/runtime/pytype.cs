namespace Python.Runtime {
    using System;

    public class PyType : PyObject
    {
        /// <summary>
        /// Creates a new instance from an existing object reference. Note
        /// that the instance assumes ownership of the object reference.
        /// The object reference is not checked for type-correctness.
        /// </summary>
        public PyType(IntPtr ptr) : base(ptr) { }

        /// <summary>
        /// Creates a new instance from an existing object reference.
        /// Ensures the type of the object is Python type.
        /// </summary>
        public PyType(PyObject o) : base(FromPyObject(o)) { }

        static IntPtr FromPyObject(PyObject o) {
            if (o == null) throw new ArgumentNullException(nameof(o));

            if (!IsTypeType(o)) {
                throw new ArgumentException("object is not a type");
            }
            Runtime.XIncref(o.obj);
            return o.obj;
        }

        /// <summary>
        /// Returns <c>true</c> if the given object is Python type.
        /// </summary>
        public static bool IsTypeType(PyObject obj) {
            if (obj == null) {
                throw new ArgumentNullException(nameof(obj));
            }

            return Runtime.PyType_Check(obj.Reference);
        }

        /// <summary>
        /// Returns <c>true</c> if the given object is Python type.
        /// </summary>
        public static bool IsTypeType(IntPtr typeHandle) {
            if (typeHandle == IntPtr.Zero) {
                throw new ArgumentNullException(nameof(typeHandle));
            }

            return Runtime.PyType_Check(new BorrowedReference(typeHandle));
        }

        /// <summary>
        /// Gets <see cref="PyType"/>, which represents the specified CLR type.
        /// Must be called after the CLR type was mapped to its Python type.
        /// </summary>
        public static PyType Get(Type clrType) {
            if (clrType == null) {
                throw new ArgumentNullException(nameof(clrType));
            }

            ClassBase pyClass = ClassManager.GetClass(clrType);
            return new PyType(Runtime.SelfIncRef(pyClass.pyHandle));
        }
    }
}
