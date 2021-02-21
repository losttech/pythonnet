namespace Python.Runtime.Slots
{
    interface ITypeDescriptorSet
    {
        int tp_descr_set(BorrowedReference self, BorrowedReference instance, BorrowedReference value);
    }
}
