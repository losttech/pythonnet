using System;

namespace Python.Runtime
{
    /// <summary>
    /// Base class for all attributes, that override converting Python values to specific types.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct
                  | AttributeTargets.Enum | AttributeTargets.Delegate,
        AllowMultiple = false,
        Inherited = true)]
    public abstract class ConvertibleFromPythonAttribute: Attribute
    {
        /// <summary>
        /// Converts Python object <paramref name="pyObj"/> to type <typeparamref name="T"/>.
        /// If conversion this converter does not support source or target type, it returns <c>false</c> instead.
        /// </summary>
        /// <typeparam name="T">.NET type to convert Python object to</typeparam>
        /// <param name="pyObj">A pointer to Python object to convert</param>
        /// <param name="value">A variable, that will receive the converted object</param>
        public abstract bool TryConvertFromPython<T>(IntPtr pyObj, out T value);
    }
}
