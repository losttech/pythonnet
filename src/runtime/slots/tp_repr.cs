namespace Python.Runtime.Slots
{
    interface ITypeRepr
    {
        NewReference tp_repr(BorrowedReference self);
    }
}
