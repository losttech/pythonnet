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
    public class BaseTypeAttributeBase : Attribute
    {
        /// <summary>
        /// Get a tuple of Python type(s), that should be presented to Python as the base type(s)
        /// for the specified .NET type.
        /// </summary>
        public virtual PyTuple BaseTypes(Type type) {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (type == typeof(Exception))
            {
                return PyTuple.FromSingleElement(Exceptions.Exception);
            }

            if (type.BaseType != null) {
                ClassBase bc = ClassManager.GetClass(type.BaseType);
                return PyTuple.FromSingleElement(bc.pyHandle);
            }

            return new PyTuple();
        }

        internal static BaseTypeAttributeBase Default { get; } = new BaseTypeAttributeBase();
    }
}
