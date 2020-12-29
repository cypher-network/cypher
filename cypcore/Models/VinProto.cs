// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using ProtoBuf;
using CYPCore.Extentions;

namespace CYPCore.Models
{
    [ProtoContract]
    public class VinProto
    {
        [ProtoMember(1)]
        public AuxProto Key { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToHash()
        {
            return Helper.Util.SHA384ManagedHash(Stream());
        }

        public byte[] Stream()
        {
            byte[] stream;
            using (var ts = new Helper.TangramStream())
            {
                ts
                .Append(Key.K_Image)
                .Append(Key.K_Offsets);

                stream = ts.ToArray();
            }

            return stream;
        }
    }
}
