using FASTER.core;

namespace CYPCore.Persistence
{
    public class StoreContext
    {
        private Status _status;
        private StoreOutput _output;

        internal void Populate(ref Status status, ref StoreOutput output)
        {
            _status = status;
            _output = output;
        }

        internal void FinalizeRead(ref Status status, ref StoreOutput output)
        {
            status = _status;
            output = _output;
        }
    }
}
