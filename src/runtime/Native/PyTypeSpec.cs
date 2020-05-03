using System;
using System.Runtime.InteropServices;

namespace Python.Runtime.Native
{
    [StructLayout(LayoutKind.Sequential)]
    class PyTypeSpec
    {
        public string Name { get; set; }
        public int BasicSize { get; set; }
        public int ItemSize { get; set; }
        public TypeFlags TypeFlags { get; set; }
        public PyTypeSlot[] Slots { get; set; }
    }
}
