using System;
using System.Collections.Generic;
using System.Linq;

namespace Python.Runtime
{
    class PythonBaseTypeProviderGroup : List<IPythonBaseTypeProvider>, IPythonBaseTypeProvider
    {
        public IEnumerable<PyObject> GetBaseTypes(Type type, IList<PyObject> existingBases)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));
            if (existingBases is null)
                throw new ArgumentNullException(nameof(existingBases));

            foreach (var provider in this)
            {
                existingBases = provider.GetBaseTypes(type, existingBases).ToList();
            }

            return existingBases;
        }
    }
}
