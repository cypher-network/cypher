using FASTER.core;

namespace CYPCore.Persistence
{
    public sealed class StoreFunctions : FunctionsBase<StoreKey, StoreValue, StoreInput, StoreOutput, StoreContext>
    {
        public override void SingleReader(ref StoreKey key, ref StoreInput input, ref StoreValue value, ref StoreOutput dst)
        {
            dst.value = value;
        }

        public override void ConcurrentReader(ref StoreKey key, ref StoreInput input, ref StoreValue value, ref StoreOutput dst)
        {
            dst.value = value;
        }

        public override void ReadCompletionCallback(ref StoreKey key, ref StoreInput input, ref StoreOutput output, StoreContext ctx, Status status)
        {
            ctx.Populate(ref status, ref output);
        }
    }
}
