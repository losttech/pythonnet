using System;

using Python.Runtime.Native;

namespace Python.Runtime
{
    public class PyModule: PyObject
    {
        internal PyModule(IntPtr handle): base(handle) { }

        /// <summary>
        /// Given a module or package name, import the
        /// module and return the resulting module object as a <see cref="PyModule"/>.
        /// </summary>
        /// <param name="name">Fully-qualified module or package name</param>
        public static PyModule Import(string name)
        {
            IntPtr op = Runtime.PyImport_ImportModule(name);
            PythonException.ThrowIfIsNull(op);
            return new PyModule(op);
        }

        /// <summary>
        /// Reloads the module, and returns the updated object
        /// </summary>
        public PyModule Reload()
        {
            IntPtr op = Runtime.PyImport_ReloadModule(this.Handle);
            PythonException.ThrowIfIsNull(op);
            return new PyModule(op);
        }

        public static PyModule FromString(string name, string code)
        {
            IntPtr c = Runtime.Py_CompileString(code, "none", (int)RunFlagType.File);
            PythonException.ThrowIfIsNull(c);
            IntPtr m = Runtime.PyImport_ExecCodeModule(name, c);
            PythonException.ThrowIfIsNull(m);
            return new PyModule(m);
        }

        public static PyModule Compile(string code, string filename = "", RunFlagType mode = RunFlagType.File)
        {
            var flag = (int)mode;
            IntPtr ptr = Runtime.Py_CompileString(code, filename, flag);
            PythonException.ThrowIfIsNull(ptr);
            return new PyModule(ptr);
        }
    }
}
