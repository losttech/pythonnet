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

    /// <summary>
    /// Implement this interface to override Python's __setattr__ for your class
    /// </summary>
    public interface ISetAttr {
        bool TrySetAttr(string name, PyObject value);
    }

    static class SlotOverrides {
        public static IntPtr tp_getattro(IntPtr ob, IntPtr key) {
            IntPtr genericResult = Runtime.PyObject_GenericGetAttr(ob, key);
            if (genericResult != IntPtr.Zero || !Runtime.PyString_Check(key)) {
                return genericResult;
            }

            Exceptions.Clear();

            var self = (IGetAttr)((CLRObject)ManagedType.GetManagedObject(ob)).inst;
            string attr = Runtime.GetManagedString(key);
            RuntimeHelpers.EnsureSufficientExecutionStack();
            return self.TryGetAttr(attr, out var value)
                ? Runtime.SelfIncRef(value.Handle)
                : Runtime.PyObject_GenericGetAttr(ob, key);
        }

        public static int tp_setattro(IntPtr ob, IntPtr key, IntPtr val) {
            if (!Runtime.PyString_Check(key)) {
                return Runtime.PyObject_GenericSetAttr(ob, key, val);
            }

            var self = (ISetAttr)((CLRObject)ManagedType.GetManagedObject(ob)).inst;
            string attr = Runtime.GetManagedString(key);
            RuntimeHelpers.EnsureSufficientExecutionStack();
            return self.TrySetAttr(attr, new PyObject(Runtime.SelfIncRef(val)))
                ? 0
                : Runtime.PyObject_GenericSetAttr(ob, key, val);
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
            using (var @class = self.GetAttr("__class__"))
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
    }

    public static class SetAttr {
        static readonly PyObject setAttr = "__setattr__".ToPython();

        /// <summary>
        /// Tries to call base type's __setattr__ on the specified instance.
        /// <para>Only use when base.TrySetAttr is not available.</para>
        /// </summary>
        /// <param name="self">Python object to call __setattr__ on</param>
        /// <param name="name">Name of the attribute to write</param>
        /// <param name="value">New value for the attribute</param>
        public static bool TrySetBaseAttr(PyObject self, string name, PyObject value) {
            if (self == null) throw new ArgumentNullException(nameof(self));
            if (name == null) throw new ArgumentNullException(nameof(name));

            using (var super = new PyObject(Runtime.PySuper))
            using (var @class = self.GetAttr("__class__"))
            using (var @base = super.Invoke(@class, self)) {
                if (!@base.HasAttr(setAttr)) return false;

                using (var pythonName = name.ToPython()) {
                    @base.InvokeMethod(setAttr, pythonName, value)?.Dispose();
                    return true;
                }
            }
        }
    }
}
