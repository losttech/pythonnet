namespace Python.Runtime.Slots
{
    interface ITypeIter
    {
        NewReference tp_iter(BorrowedReference self);
    }
}
