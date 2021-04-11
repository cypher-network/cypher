using MP = MessagePack;

namespace rxcypcore.Helper.MessagePack
{
    public class Resolver
    {
        public static MP.IFormatterResolver Get()
        {
            return MP.Resolvers.CompositeResolver.Create(
                IPAddressResolver.Instance,
                MP.Resolvers.DynamicEnumAsStringResolver.Instance,
                MP.Resolvers.ContractlessStandardResolver.Instance
            );
        }
    }
}