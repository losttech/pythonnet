using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace Python.Runtime
{
    /// <summary>
    /// Several places in the runtime generate code on the fly to support
    /// dynamic functionality. The CodeGenerator class manages the dynamic
    /// assembly used for code generation and provides utility methods for
    /// certain repetitive tasks.
    /// </summary>
    internal class CodeGenerator
    {
        private AssemblyBuilder aBuilder;
        private ModuleBuilder mBuilder;

        internal const string DynamicAssemblyName = "__PythonNET__CodeGenerator__DynamicAssembly";

        internal CodeGenerator()
        {
            var aname = new AssemblyName
            {
                Name = DynamicAssemblyName,
                KeyPair = GetStrongNameKeyPair(),
            };
            var aa = AssemblyBuilderAccess.Run;

            aBuilder = Thread.GetDomain().DefineDynamicAssembly(aname, aa);
            mBuilder = aBuilder.DefineDynamicModule(DynamicAssemblyName);
        }

        /// <summary>
        /// DefineType is a shortcut utility to get a new TypeBuilder.
        /// </summary>
        internal TypeBuilder DefineType(string name)
        {
            var attrs = TypeAttributes.Public;
            return mBuilder.DefineType(name, attrs);
        }

        /// <summary>
        /// DefineType is a shortcut utility to get a new TypeBuilder.
        /// </summary>
        internal TypeBuilder DefineType(string name, Type basetype)
        {
            var attrs = TypeAttributes.Public;
            return mBuilder.DefineType(name, attrs, basetype);
        }

        static StrongNameKeyPair GetStrongNameKeyPair()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("pythonnet.snk");
            using var temp = new System.IO.MemoryStream();
            stream.CopyTo(temp);
            return new StrongNameKeyPair(temp.ToArray());
        }
    }
}
