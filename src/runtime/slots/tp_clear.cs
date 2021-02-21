namespace Python.Runtime.Slots
{
    interface ITypeClear
    {
        void tp_clear(BorrowedReference self);
    }
}
