using System;
using System.Runtime.CompilerServices;

namespace Python.Runtime.Slots
{
    /// <summary>
    /// Implement this interface to override Python's __getattr__ for your class
    /// </summary>
    public interface IGetAttr {
        bool TryGetAttr(string name, out PyObject value);
    }

    static class SlotOverrides {
        public static IntPtr tp_getattro(IntPtr ob, IntPtr key) {
            if (!Runtime.PyString_Check(key)) {
                return Runtime.PyObject_GenericGetAttr(ob, key);
            }

            Exceptions.Clear();

            var self = (IGetAttr)((CLRObject)ManagedType.GetManagedObject(ob)).inst;
            string attr = Runtime.GetManagedString(key);
            RuntimeHelpers.EnsureSufficientExecutionStack();
            bool gotAttr;
            PyObject value;
            try {
                gotAttr = self.TryGetAttr(attr, out value);
            } catch (Exception error) {
                Exceptions.SetError(error);
                return IntPtr.Zero;
            }
            if (gotAttr) {
                if (value == null || value.Handle == IntPtr.Zero) {
                    Exceptions.SetError(Exceptions.ValueError,
                        nameof(IGetAttr) + " implementation returned null value");
                    return IntPtr.Zero;
                }

                return Runtime.SelfIncRef(value.Handle);
            } else {
                IntPtr result = Runtime.PyObject_GenericGetAttr(ob, key);
                System.Diagnostics.Debug.Assert(result != IntPtr.Zero
                                                || Exceptions.ErrorOccurred());
                return result;
            }
        }
    }

    public static class GetAttr {
        static readonly PyObject getAttr = "__getattr__".ToPython();

        /// <summary>
        /// Tries to call base type's __getattr__ on the specified instance.
        /// <para>Only use when base.TryGetAttr is not available.</para>
        /// </summary>
        /// <param name="self">Python object to call __getattr__ on</param>
        /// <param name="name">Name of the attribute to retrieve</param>
        /// <param name="result">Reference to a variable, that will receive the attribute value</param>
        public static bool TryGetBaseAttr(PyObject self, string name, out PyObject result) {
            if (self == null) throw new ArgumentNullException(nameof(self));
            if (name == null) throw new ArgumentNullException(nameof(name));

            using (var super = new PyObject(Runtime.PySuper))
            using (var @class = self.GetPythonType())
            using (var @base = super.Invoke(@class, self)) {
                if (!@base.HasAttr(getAttr)) {
                    result = null;
                    return false;
                }

                using (var pythonName = name.ToPython()) {
                    result = @base.InvokeMethod(getAttr, pythonName);
                    return true;
                }
            }
        }

        public static bool GenericGetAttr(PyObject self, string name, out PyObject result) {
            if (self == null) throw new ArgumentNullException(nameof(self));
            if (name == null) throw new ArgumentNullException(nameof(name));

            using (var pyName = name.ToPython()) {
                IntPtr pyResult = Runtime.PyObject_GenericGetAttr(self.Handle, pyName.Handle);
                if (pyResult == IntPtr.Zero) {
                    result = null;
                    if (!PythonException.Matches(Exceptions.AttributeError)) {
                        throw PythonException.FromPyErr();
                    }

                    Exceptions.Clear();
                    return false;
                }
                result = new PyObject(Runtime.SelfIncRef(pyResult));
                return true;
            }
        }
    }
}
