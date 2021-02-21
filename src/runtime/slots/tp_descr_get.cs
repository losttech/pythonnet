namespace Python.Runtime.Slots
{
    interface ITypeDescriptorGet
    {
        NewReference tp_descr_get(BorrowedReference self, BorrowedReference instance, BorrowedReference owner);
    }
}
