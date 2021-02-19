using System;

namespace Python.Runtime
{
    public static class PyObjectInterop
    {
        /// <summary>
        /// Gets raw Python proxy for this object (bypasses all conversions,
        /// except <c>null</c> &lt;==&gt; <c>None</c>)
        /// </summary>
        /// <remarks>
        /// Given an arbitrary managed object, return a Python instance that
        /// reflects the managed object.
        /// </remarks>
        public static PyObject FromManagedObject(object ob)
        {
            // Special case: if ob is null, we return None.
            if (ob == null)
            {
                Runtime.XIncref(Runtime.PyNone);
                return new PyObject(Runtime.PyNone);
            }
            IntPtr op = CLRObject.GetInstHandle(ob);
            return new PyObject(op);
        }
    }
}
