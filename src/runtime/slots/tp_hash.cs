namespace Python.Runtime.Slots
{
    interface ITypeHash
    {
        nint tp_hash(BorrowedReference self);
    }
}
