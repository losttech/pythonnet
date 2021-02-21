using System;

namespace Python.Runtime.Slots
{
    interface ITypeFree
    {
        void tp_free(IntPtr self);
    }
}
