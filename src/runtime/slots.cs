using System;
using System.Runtime.InteropServices;

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
            return self.TrySetAttr(attr, new PyObject(Runtime.SelfIncRef(val)))
                ? 0
                : Runtime.PyObject_GenericSetAttr(ob, key, val);
        }
    }

    public static class GetAttr {
        static bool TryGetBaseAttr(PyObject self, IntPtr @base, string name, out PyObject result) {
            if (self == null) throw new ArgumentNullException(nameof(self));
            if (@base == IntPtr.Zero) throw new ArgumentNullException(nameof(@base));
            if (name == null) throw new ArgumentNullException(nameof(name));

            result = null;

            for (; @base != IntPtr.Zero; @base = Marshal.ReadIntPtr(@base, TypeOffset.tp_base)) {
                IntPtr getAttr = Marshal.ReadIntPtr(@base, TypeOffset.tp_getattro);
                if (getAttr == IntPtr.Zero) continue;
                IntPtr resultPtr = NativeCall.Call_2(fp: getAttr, self.Handle, name.ToPython().Handle);
                if (resultPtr != IntPtr.Zero) {
                    result = new PyObject(Runtime.SelfIncRef(resultPtr));
                    return true;
                }

                return false;
            }

            return false;
        }

        /// <summary>
        /// Tries to call base type's __getattr__ on the specified instance.
        /// <para>Only use when base.TryGetAttr is not available.</para>
        /// </summary>
        /// <param name="self">Python object to call __getattr__ on</param>
        /// <param name="baseType">Type in the object base type hierarchy, whose __getattr__ to call</param>
        /// <param name="name">Name of the attribute to retrieve</param>
        /// <param name="result">Reference to a variable, that will receive the attribute value</param>
        public static bool TryGetBaseAttr(PyObject self, PyObject baseType, string name, out PyObject result) {
            if (baseType == null) throw new ArgumentNullException(nameof(baseType));

            return TryGetBaseAttr(self, baseType.Handle, name, out result);
        }

        /// <summary>
        /// Tries to call base type's __getattr__ on the specified instance.
        /// <para>Only use when base.TryGetAttr is not available.</para>
        /// </summary>
        /// <param name="self">Python object to call __getattr__ on</param>
        /// <param name="name">Name of the attribute to retrieve</param>
        /// <param name="result">Reference to a variable, that will receive the attribute value</param>
        public static bool TryGetBaseAttr(PyObject self, string name, out PyObject result) {
            if (self == null) throw new ArgumentNullException(nameof(self));

            IntPtr pythonType = Runtime.PyObject_TYPE(self.Handle);
            IntPtr pythonBase = ClassObject.GetPythonBase(pythonType);
            return TryGetBaseAttr(self, pythonBase, name, out result);
        }
    }

    public static class SetAttr {
        public static bool TrySetBaseAttr(PyObject self, PyObject baseType, string name, PyObject value) {
            if (baseType == null) throw new ArgumentNullException(nameof(baseType));
            return TrySetBaseAttr(self, baseType.Handle, name, value);
        }
        public static bool TrySetBaseAttr(PyObject self, string name, PyObject value) {
            if (self == null) throw new ArgumentNullException(nameof(self));

            IntPtr pythonType = Runtime.PyObject_TYPE(self.Handle);
            IntPtr pythonBase = ClassObject.GetPythonBase(pythonType);
            return TrySetBaseAttr(self, pythonBase, name, value);
        }

        static bool TrySetBaseAttr(PyObject self, IntPtr @base, string name, PyObject value) {
            if (self == null) throw new ArgumentNullException(nameof(self));
            if (name == null) throw new ArgumentNullException(nameof(name));

            for (; @base != IntPtr.Zero; @base = Marshal.ReadIntPtr(@base, TypeOffset.tp_base)) {
                IntPtr setAttr = Marshal.ReadIntPtr(@base, TypeOffset.tp_setattro);
                if (setAttr == IntPtr.Zero) continue;

                int result = NativeCall.Int_Call_3(fp: setAttr, self.Handle, name.ToPython().Handle,
                    value?.Handle ?? IntPtr.Zero);
                if (result != 0) throw PythonException.FromPyErr();
                return true;
            }

            return false;
        }
    }
}
