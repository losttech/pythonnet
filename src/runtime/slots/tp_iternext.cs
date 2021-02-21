namespace Python.Runtime.Slots
{
    interface ITypeIterNext
    {
        NewReference tp_iternext(BorrowedReference self);
    }
}
