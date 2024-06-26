using System;
using System.Collections;

namespace Python.Runtime
{
    /// <summary>
    /// Implements a generic Python iterator for IEnumerable objects and
    /// managed array objects. This supports 'for i in object:' in Python.
    /// </summary>
    internal class Iterator : ExtensionType
    {
        private IEnumerator iter;

        public Iterator(IEnumerator e)
        {
            iter = e;
        }


        /// <summary>
        /// Implements support for the Python iteration protocol.
        /// </summary>
        public static IntPtr tp_iternext(IntPtr ob)
        {
            var self = GetManagedObject<Iterator>(new BorrowedReference(ob));
            try
            {
                if (!self.iter.MoveNext())
                {
                    Exceptions.SetError(Exceptions.StopIteration, Runtime.PyNone);
                    return IntPtr.Zero;
                }
            }
            catch (Exception e)
            {
                Exceptions.SetError(e);
                return IntPtr.Zero;
            }
            object item = self.iter.Current;
            return Converter.ToPython(item);
        }

        public static IntPtr tp_iter(IntPtr ob)
        {
            Runtime.XIncref(ob);
            return ob;
        }
    }
}
