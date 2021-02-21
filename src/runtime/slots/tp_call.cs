namespace Python.Runtime.Slots
{
    interface ITypeCall
    {
        NewReference tp_call(BorrowedReference self, BorrowedReference args, BorrowedReference kwargs);
    }
}
