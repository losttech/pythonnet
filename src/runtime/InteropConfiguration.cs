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

        public static InteropConfiguration Default => new InteropConfiguration
        {
            PythonBaseTypeProviders =
            {
                CoreBaseTypeProvider.Instance,
            },
        };
    }
}
