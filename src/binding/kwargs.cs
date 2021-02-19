using System;
using System.Linq;

namespace Python.Runtime
{
    public class KeywordArguments : PyDict
    {
        public static KeywordArguments FromKeysAndValues(params object[] kv)
        {
            var dict = new KeywordArguments();
            if (kv.Length % 2 != 0)
            {
                throw new ArgumentException("Must have an equal number of keys and values");
            }
            for (var i = 0; i < kv.Length; i += 2)
            {
                IntPtr value;
                if (kv[i + 1] is PyObject)
                {
                    value = ((PyObject)kv[i + 1]).Handle;
                }
                else
                {
                    value = Converter.ToPython(kv[i + 1], kv[i + 1]?.GetType());
                }
                if (Runtime.PyDict_SetItemString(dict.Handle, (string)kv[i], value) != 0)
                {
                    throw new ArgumentException(string.Format("Cannot add key '{0}' to dictionary.", (string)kv[i]));
                }
                if (!(kv[i + 1] is PyObject))
                {
                    Runtime.XDecref(value);
                }
            }
            return dict;
        }
    }
}
