using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Python.Runtime
{
    /// <summary>
    /// Implements a Python binding type for CLR methods. These work much like
    /// standard Python method bindings, but the same type is used to bind
    /// both static and instance methods.
    /// </summary>
    internal class MethodBinding : ExtensionType
    {
        internal MethodInfo info;
        internal MethodObject m;
        internal IntPtr target;
        internal IntPtr targetType;
        internal BorrowedReference Target => new BorrowedReference(this.target);
        internal BorrowedReference TargetType => new BorrowedReference(this.targetType);

        public MethodBinding(MethodObject m, IntPtr target, IntPtr targetType)
        {
            if (target != IntPtr.Zero) Runtime.XIncref(target);
            this.target = target;

            Runtime.XIncref(targetType);
            if (targetType == IntPtr.Zero)
            {
                targetType = Runtime.PyObject_Type(target);
            }
            this.targetType = targetType;

            this.info = null;
            this.m = m;
        }

        public MethodBinding(MethodObject m, IntPtr target) : this(m, target, IntPtr.Zero)
        {
        }

        /// <summary>
        /// Implement binding of generic methods using the subscript syntax [].
        /// </summary>
        public static IntPtr mp_subscript(IntPtr tp, IntPtr idx)
        {
            var self = GetInstance(tp);

            Type[] types = Runtime.PythonArgsToTypeArray(idx);
            if (types == null)
            {
                return Exceptions.RaiseTypeError("type(s) expected");
            }

            MethodInfo mi = MethodBinder.MatchParameters(self.m.info, types);
            if (mi == null)
            {
                return Exceptions.RaiseTypeError("No match found for given type params");
            }

            var mb = new MethodBinding(self.m, self.target) { info = mi };
            return mb.pyHandle;
        }

        PyObject Singature {
            get {
                var infos = this.info == null ? this.m.info : new[] {this.info};
                var type = infos.Select(i => i.DeclaringType)
                    .OrderByDescending(t => t, new TypeSpecificityComparer())
                    .First();
                infos = infos.Where(info => info.DeclaringType == type).ToArray();
                // this is a primitive version
                // the overload with the maximum number of parameters should be used
                var primary = infos.OrderByDescending(i => i.GetParameters().Length).First();
                var primaryParameters = primary.GetParameters();
                PyObject signatureClass = Runtime.InspectModule.GetAttr("Signature");
                var primaryReturn = primary.ReturnParameter;

                var parameters = new PyList();
                var parameterClass = primaryParameters.Length > 0 ? Runtime.InspectModule.GetAttr("Parameter") : null;
                var positionalOrKeyword = parameterClass?.GetAttr("POSITIONAL_OR_KEYWORD");
                for (int i = 0; i < primaryParameters.Length; i++) {
                    var parameter = primaryParameters[i];
                    var alternatives = infos.Select(info => {
                        ParameterInfo[] altParamters = info.GetParameters();
                        return i < altParamters.Length ? altParamters[i] : null;
                    }).Where(p => p != null);
                    var defaultValue = alternatives
                        .Select(alternative => alternative.DefaultValue != DBNull.Value ? alternative.DefaultValue.ToPython() : null)
                        .FirstOrDefault(v => v != null) ?? parameterClass.GetAttr("empty");

                    if (alternatives.Any(alternative => alternative.Name != parameter.Name)) {
                        return signatureClass.Invoke();
                    }

                    var args = new PyTuple(new []{ parameter.Name.ToPython(), positionalOrKeyword});
                    var kw = new PyDict();
                    if (defaultValue != null) {
                        kw["default"] = defaultValue;
                    }
                    var parameterInfo = parameterClass.Invoke(args: args, kw: kw);
                    parameters.Append(parameterInfo);
                }

                // TODO: add return annotation
                return signatureClass.Invoke(parameters);
            }
        }

        struct TypeSpecificityComparer : IComparer<Type> {
            public int Compare(Type a, Type b) {
                if (a == b) return 0;
                if (a.IsSubclassOf(b)) return 1;
                if (b.IsSubclassOf(a)) return -1;
                throw new NotSupportedException();
            }
        }

        static MethodBinding GetInstance(IntPtr ob)
            => GetInstance(new BorrowedReference(ob));
        static MethodBinding GetInstance(BorrowedReference ob)
            => GetManagedObject<MethodBinding>(ob);

        /// <summary>
        /// MethodBinding __getattribute__ implementation.
        /// </summary>
        public static IntPtr tp_getattro(IntPtr ob, IntPtr key)
        {
            var self = GetInstance(ob);

            if (!Runtime.PyString_Check(key))
            {
                Exceptions.SetError(Exceptions.TypeError, "string expected");
                return IntPtr.Zero;
            }

            string name = Runtime.GetManagedString(key);
            switch (name)
            {
                case "__doc__":
                    IntPtr doc = self.m.GetDocString();
                    Runtime.XIncref(doc);
                    return doc;
                // FIXME: deprecate __overloads__ soon...
                case "__overloads__":
                case "Overloads":
                    var om = new OverloadMapper(self.m, self.target);
                    return om.pyHandle;
                case "__signature__":
                    var sig = self.Singature;
                    if (sig is null)
                    {
                        return Runtime.PyObject_GenericGetAttr(ob, key);
                    }
                    return sig.Reference.DangerousIncRefOrNull();
                case "__name__":
                    var pyName = self.m.GetName();
                    return pyName == IntPtr.Zero
                        ? IntPtr.Zero
                        : Runtime.SelfIncRef(pyName);
                default:
                    return Runtime.PyObject_GenericGetAttr(ob, key);
            }
        }


        /// <summary>
        /// MethodBinding  __call__ implementation.
        /// </summary>
        public static IntPtr tp_call(IntPtr ob, IntPtr args, IntPtr kw)
        {
            var self = GetInstance(ob);

            // This works around a situation where the wrong generic method is picked,
            // for example this method in the tests: string Overloaded<T>(int arg1, int arg2, string arg3)
            if (self.info != null)
            {
                if (self.info.IsGenericMethod)
                {
                    var len = Runtime.PyTuple_Size(args); //FIXME: Never used
                    Type[] sigTp = Runtime.PythonArgsToTypeArray(args, true);
                    if (sigTp != null)
                    {
                        Type[] genericTp = self.info.GetGenericArguments();
                        MethodInfo betterMatch = MethodBinder.MatchSignatureAndParameters(self.m.info, genericTp, sigTp);
                        if (betterMatch != null)
                        {
                            self.info = betterMatch;
                        }
                    }
                }
            }

            // This supports calling a method 'unbound', passing the instance
            // as the first argument. Note that this is not supported if any
            // of the overloads are static since we can't know if the intent
            // was to call the static method or the unbound instance method.
            var disposeList = new List<IntPtr>();
            try
            {
                var target = self.target;

                if (target == IntPtr.Zero && !self.m.IsStatic())
                {
                    var len = Runtime.PyTuple_Size(args);
                    if (len < 1)
                    {
                        Exceptions.SetError(Exceptions.TypeError, "not enough arguments");
                        return IntPtr.Zero;
                    }
                    target = Runtime.PyTuple_GetItem(args, 0);
                    Runtime.XIncref(target);
                    disposeList.Add(target);

                    args = Runtime.PyTuple_GetSlice(args, 1, len);
                    disposeList.Add(args);
                }

                // if the class is a IPythonDerivedClass and target is not the same as self.targetType
                // (eg if calling the base class method) then call the original base class method instead
                // of the target method.
                IntPtr superType = IntPtr.Zero;
                if (Runtime.PyObject_TYPE(target) != self.targetType)
                {
                    var inst = ManagedType.GetManagedObject(target) as CLRObject;
                    if (inst?.inst is IPythonDerivedType)
                    {
                        var baseType = ManagedType.GetManagedObject(self.TargetType) as ClassBase;
                        if (baseType != null)
                        {
                            string baseMethodName = "_" + baseType.type.Name + "__" + self.m.name;
                            using var baseMethod = Runtime.PyObject_GetAttrString(self.Target, baseMethodName);
                            if (!baseMethod.IsNull())
                            {
                                BorrowedReference baseMethodType = Runtime.PyObject_TYPE(baseMethod);
                                BorrowedReference methodBindingTypeHandle = TypeManager.GetTypeHandle(typeof(MethodBinding));
                                if (baseMethodType == methodBindingTypeHandle)
                                {
                                    self = GetInstance(baseMethod);
                                }
                            }
                            else
                            {
                                Runtime.PyErr_Clear();
                            }
                        }
                    }
                }

                return self.m.Invoke(target, args, kw, self.info);
            }
            finally
            {
                foreach (IntPtr ptr in disposeList)
                {
                    Runtime.XDecref(ptr);
                }
            }
        }


        /// <summary>
        /// MethodBinding  __hash__ implementation.
        /// </summary>
        public static IntPtr tp_hash(IntPtr ob)
        {
            var self = GetInstance(ob);
            long x = 0;
            long y = 0;

            if (self.target != IntPtr.Zero)
            {
                x = Runtime.PyObject_Hash(self.target).ToInt64();
                if (x == -1)
                {
                    return new IntPtr(-1);
                }
            }

            y = Runtime.PyObject_Hash(self.m.pyHandle).ToInt64();
            if (y == -1)
            {
                return new IntPtr(-1);
            }

            x ^= y;

            if (x == -1)
            {
                x = -1;
            }

            return new IntPtr(x);
        }

        /// <summary>
        /// MethodBinding  __repr__ implementation.
        /// </summary>
        public static IntPtr tp_repr(IntPtr ob)
        {
            var self = GetInstance(ob);
            string type = self.target == IntPtr.Zero ? "unbound" : "bound";
            string name = self.m.name;
            return Runtime.PyString_FromString($"<{type} method '{name}'>");
        }

        /// <summary>
        /// MethodBinding dealloc implementation.
        /// </summary>
        public new static void tp_dealloc(IntPtr ob)
        {
            var self = GetInstance(ob);
            Runtime.XDecref(self.target);
            Runtime.XDecref(self.targetType);
            FinalizeObject(self);
        }
    }
}
