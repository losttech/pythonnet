using System;

namespace Python.Runtime
{
    /// <summary>
    /// Base class for Python types that reflect managed exceptions based on
    /// System.Exception
    /// </summary>
    /// <remarks>
    /// The Python wrapper for managed exceptions LIES about its inheritance
    /// tree. Although the real System.Exception is a subclass of
    /// System.Object the Python type for System.Exception does NOT claim that
    /// it subclasses System.Object. Instead TypeManager.CreateType() uses
    /// Python's exception.Exception class as base class for System.Exception.
    /// </remarks>
    [Serializable]
    internal class ExceptionClassObject : ClassObject
    {
        internal ExceptionClassObject(Type tp) : base(tp)
        {
        }

        internal static Exception ToException(IntPtr ob)
        {
            var co = GetManagedObject(ob) as CLRObject;
            if (co == null)
            {
                return null;
            }
            var e = co.inst as Exception;
            if (e == null)
            {
                return null;
            }
            return e;
        }

        /// <summary>
        /// Exception __repr__ implementation
        /// </summary>
        public new static IntPtr tp_repr(IntPtr ob)
        {
            Exception e = ToException(ob);
            if (e == null)
            {
                return Exceptions.RaiseTypeError("invalid object");
            }
            string name = e.GetType().Name;
            string message;
            if (e.Message != String.Empty)
            {
                message = String.Format("{0}('{1}')", name, e.Message);
            }
            else
            {
                message = String.Format("{0}()", name);
            }
            return Runtime.PyUnicode_FromString(message);
        }

        /// <summary>
        /// Exception __str__ implementation
        /// </summary>
        public new static IntPtr tp_str(IntPtr ob)
        {
            Exception e = ToException(ob);
            if (e == null)
            {
                return Exceptions.RaiseTypeError("invalid object");
            }

            string message = e.ToString();
            string fullTypeName = e.GetType().FullName;
            string prefix = fullTypeName + ": ";
            if (message.StartsWith(prefix))
            {
                message = message.Substring(prefix.Length);
            }
            else if (message.StartsWith(fullTypeName))
            {
                message = message.Substring(fullTypeName.Length);
            }
            return Runtime.PyUnicode_FromString(message);
        }
    }
}
