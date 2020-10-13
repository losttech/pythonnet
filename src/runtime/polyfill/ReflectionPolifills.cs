using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Python.Runtime
{
    [Obsolete("This API is for internal use only")]
    public static class ReflectionPolifills
    {
#if NETSTANDARD
        public static AssemblyBuilder DefineDynamicAssembly(this AppDomain appDomain, AssemblyName assemblyName, AssemblyBuilderAccess assemblyBuilderAccess)
        {
            return AssemblyBuilder.DefineDynamicAssembly(assemblyName, assemblyBuilderAccess);
        }

        public static Type CreateType(this TypeBuilder typeBuilder)
        {
            return typeBuilder.CreateTypeInfo();
        }
#endif
#if NETFX
        public static T GetCustomAttribute<T>(this Type type) where T: Attribute
        {
            return type.GetCustomAttributes(typeof(T), inherit: false)
                .Cast<T>()
                .SingleOrDefault();
        }

        public static T GetCustomAttribute<T>(this Assembly assembly) where T: Attribute
        {
            return assembly.GetCustomAttributes(typeof(T), inherit: false)
                .Cast<T>()
                .SingleOrDefault();
        }
#endif

        public static object GetDefaultValue(this ParameterInfo parameterInfo)
        {
            // parameterInfo.HasDefaultValue is preferable but doesn't exist in .NET 4.0
            bool hasDefaultValue = (parameterInfo.Attributes & ParameterAttributes.HasDefault) ==
                ParameterAttributes.HasDefault;

            if (hasDefaultValue)
            {
                return parameterInfo.DefaultValue;
            }
            else
            {
                // [OptionalAttribute] was specified for the parameter.
                // See https://stackoverflow.com/questions/3416216/optionalattribute-parameters-default-value
                // for rules on determining the value to pass to the parameter
                var type = parameterInfo.ParameterType;
                if (type == typeof(object))
                    return Type.Missing;
                else if (type.IsValueType)
                    return Activator.CreateInstance(type);
                else
                    return null;
            }
        }
    }
}
