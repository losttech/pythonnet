namespace Python.Runtime
{
    using System;
    using System.Collections.Generic;
    using Python.Runtime.Mixins;

    public sealed class InteropConfiguration
    {
        internal readonly PythonBaseTypeProviderGroup pythonBaseTypeProviders
            = new PythonBaseTypeProviderGroup();
        public IList<IPythonBaseTypeProvider> PythonBaseTypeProviders => this.pythonBaseTypeProviders;

        public static InteropConfiguration MakeDefault() => new InteropConfiguration
        {
            PythonBaseTypeProviders =
            {
                CoreBaseTypeProvider.Instance,
                new CollectionMixinsProvider(new Lazy<PyObject>(() => Py.Import("clr._extras.collections"))),
            },
        };
    }
}
