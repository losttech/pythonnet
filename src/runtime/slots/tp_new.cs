namespace Python.Runtime.Slots
{
    interface ITypeNew
    {
        NewReference tp_new(BorrowedReference subtype, BorrowedReference args, BorrowedReference kwargs);
    }
}
