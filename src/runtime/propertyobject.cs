using System;
using System.Reflection;
using System.Security.Permissions;

namespace Python.Runtime
{
    /// <summary>
    /// Implements a Python descriptor type that manages CLR properties.
    /// </summary>
    internal class PropertyObject : ExtensionType
    {
        internal PropertyInfo info;
        private MethodInfo getter;
        private MethodInfo setter;

        [StrongNameIdentityPermission(SecurityAction.Assert)]
        public PropertyObject(PropertyInfo md)
        {
            getter = md.GetGetMethod(true);
            setter = md.GetSetMethod(true);
            info = md;
        }

        static PropertyObject GetInstance(IntPtr ob)
            => GetManagedObject<PropertyObject>(new BorrowedReference(ob));
        /// <summary>
        /// Descriptor __get__ implementation. This method returns the
        /// value of the property on the given object. The returned value
        /// is converted to an appropriately typed Python object.
        /// </summary>
        public static IntPtr tp_descr_get(IntPtr ds, IntPtr obRaw, IntPtr tp)
        {
            var ob = new BorrowedReference(obRaw);
            var self = GetInstance(ds);
            MethodInfo getter = self.getter;
            object result;


            if (getter == null)
            {
                return Exceptions.RaiseTypeError("property cannot be read");
            }

            if (ob == IntPtr.Zero || ob == Runtime.PyNone)
            {
                if (!getter.IsStatic)
                {
                    Runtime.XIncref(ds);
                    // unbound property
                    return ds;
                }

                try
                {
                    result = self.info.GetValue(null, null);
                    return Converter.ToPython(result);
                }
                catch (Exception e)
                {
                    Exceptions.SetError(e);
                    return IntPtr.Zero;
                }
            }

            var co = ManagedType.GetManagedObject(ob) as CLRObject;
            if (co == null)
            {
                return Exceptions.RaiseTypeError("invalid target");
            }

            try
            {
                result = self.info.GetValue(co.inst, null);
                return Converter.ToPython(result);
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    e = e.InnerException;
                }
                Exceptions.SetError(e);
                return IntPtr.Zero;
            }
        }


        /// <summary>
        /// Descriptor __set__ implementation. This method sets the value of
        /// a property based on the given Python value. The Python value must
        /// be convertible to the type of the property.
        /// </summary>
        public new static int tp_descr_set(IntPtr ds, IntPtr obRaw, IntPtr val)
        {
            var ob = new BorrowedReference(obRaw);
            var self = GetInstance(ds);
            MethodInfo setter = self.setter;
            object newval;

            if (val == IntPtr.Zero)
            {
                Exceptions.RaiseTypeError("cannot delete property");
                return -1;
            }

            if (setter == null)
            {
                Exceptions.RaiseTypeError("property is read-only");
                return -1;
            }


            if (!Converter.ToManaged(val, self.info.PropertyType, out newval, true))
            {
                return -1;
            }

            bool is_static = setter.IsStatic;

            if (ob == IntPtr.Zero || ob == Runtime.PyNone)
            {
                if (!is_static)
                {
                    Exceptions.RaiseTypeError("instance property must be set on an instance");
                    return -1;
                }
            }

            try
            {
                if (!is_static)
                {
                    var co = ManagedType.GetManagedObject(ob) as CLRObject;
                    if (co == null)
                    {
                        Exceptions.RaiseTypeError("invalid target");
                        return -1;
                    }
                    self.info.SetValue(co.inst, newval, null);
                }
                else
                {
                    self.info.SetValue(null, newval, null);
                }
                return 0;
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    e = e.InnerException;
                }
                Exceptions.SetError(e);
                return -1;
            }
        }


        /// <summary>
        /// Descriptor __repr__ implementation.
        /// </summary>
        public static IntPtr tp_repr(IntPtr ob)
        {
            var self = GetInstance(ob);
            return Runtime.PyString_FromString($"<property '{self.info.Name}'>");
        }
    }
}
