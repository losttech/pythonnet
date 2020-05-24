using System;
using System.Collections.Generic;

namespace Python.Runtime
{
    /// <summary>
    /// Minimal Python base type provider
    /// </summary>
    public sealed class CoreBaseTypeProvider : IPythonBaseTypeProvider
    {
        public IEnumerable<PyObject> GetBaseTypes(Type type, IList<PyObject> existingBases)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));
            if (existingBases is null)
                throw new ArgumentNullException(nameof(existingBases));
            if (existingBases.Count > 0)
                throw new ArgumentException("To avoid confusion, this type provider requires the initial set of base types to be empty");

            return new[] { new PyObject(GetBaseType(type)) };
        }

        static BorrowedReference GetBaseType(Type type)
        {
            if (type == typeof(Exception))
                return new BorrowedReference(Exceptions.Exception);

            return type.BaseType != null
                ? ClassManager.GetClass(type.BaseType).Instance
                : new BorrowedReference(Runtime.PyBaseObjectType);
        }

        CoreBaseTypeProvider(){}
        public static CoreBaseTypeProvider Instance { get; } = new CoreBaseTypeProvider();
    }
}
