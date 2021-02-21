namespace Python.Runtime.Slots
{
    interface ITypeAlloc
    {
        NewReference tp_alloc(BorrowedReference self, nint itemCount);
    }
}
