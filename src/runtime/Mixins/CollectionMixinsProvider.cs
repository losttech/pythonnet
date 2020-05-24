using System;
using System.Collections.Generic;
using System.Linq;

namespace Python.Runtime.Mixins
{
    class CollectionMixinsProvider: IPythonBaseTypeProvider
    {
        readonly Lazy<PyObject> mixinsModule;
        public CollectionMixinsProvider(Lazy<PyObject> mixinsModule)
        {
            this.mixinsModule = mixinsModule ?? throw new ArgumentNullException(nameof(mixinsModule));
        }

        public PyObject Mixins => this.mixinsModule.Value;

        public IEnumerable<PyObject> GetBaseTypes(Type type, IList<PyObject> existingBases)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            if (existingBases is null)
                throw new ArgumentNullException(nameof(existingBases));

            foreach (PyObject existingBase in existingBases)
                yield return existingBase;

            var interfaces = NewInterfaces(type).Select(GetDefinition).ToArray();

            // dictionaries
            if (interfaces.Contains(typeof(IDictionary<,>)))
            {
               yield return this.Mixins.GetAttr("MutableMappingMixin");
#if NETSTANDARD
            } else if (interfaces.Contains(typeof(IReadOnlyDictionary<,>)))
            {
                yield return this.Mixins.GetAttr("MappingMixin");
#endif
            }

            // item collections
            if (interfaces.Contains(typeof(IList<>))
                || interfaces.Contains(typeof(System.Collections.IList)))
            {
                yield return this.Mixins.GetAttr("MutableSequenceMixin");
#if NETSTANDARD
            } else if (interfaces.Contains(typeof(IReadOnlyList<>)))
            {
                yield return this.Mixins.GetAttr("SequenceMixin");
#endif
            } else if (interfaces.Contains(typeof(ICollection<>))
                       || interfaces.Contains(typeof(System.Collections.ICollection)))
            {
                yield return this.Mixins.GetAttr("CollectionMixin");
            } else if (interfaces.Contains(typeof(System.Collections.IEnumerable)))
            {
                yield return this.Mixins.GetAttr("IterableMixin");
            }

            // enumerators
            if (interfaces.Contains(typeof(System.Collections.IEnumerator)))
            {
                yield return this.Mixins.GetAttr("IteratorMixin");
            }
        }

        static Type[] NewInterfaces(Type type)
        {
            var result = type.GetInterfaces();
            return type.BaseType != null
                ? result.Except(type.BaseType.GetInterfaces()).ToArray()
                : result;
        }

        static Type GetDefinition(Type type)
            => type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }
}
