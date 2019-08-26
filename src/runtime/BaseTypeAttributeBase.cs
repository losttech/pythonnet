using System;

namespace Python.Runtime
{
    /// <summary>
    /// Base class for all attributes, that override base type for C# classes as seen from Python
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Delegate | AttributeTargets.Enum
                    | AttributeTargets.Interface | AttributeTargets.Struct,
        AllowMultiple = false,
        Inherited = true)]
    public abstract class BaseTypeAttributeBase : Attribute
    {
        /// <summary>
        /// Get the handle of a Python type, that should be presented to Python as the base type
        /// for the specified .NET type.
        /// </summary>
        public abstract IntPtr BaseType(Type type);
    }
}
