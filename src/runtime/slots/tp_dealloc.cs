namespace Python.Runtime.Slots
{
    interface ITypeDealloc
    {
        void tp_dealloc(BorrowedReference self);
    }
}
