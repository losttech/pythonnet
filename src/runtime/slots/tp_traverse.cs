using System;

namespace Python.Runtime.Slots
{
    interface ITypeTraverse
    {
        int tp_traverse(BorrowedReference self, IntPtr visitProc, IntPtr arg);
    }
}
