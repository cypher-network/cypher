// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

using ProtoBuf;
using CYPCore.Extentions;

namespace CYPCore.Models
{
    [ProtoContract]
    public class TransactionProto
    {
        [ProtoMember(1)]
        public byte[] TxnId { get; set; }
        [ProtoMember(2)]
        public BpProto[] Bp { get; set; }
        [ProtoMember(3)]
        public int Ver { get; set; }
        [ProtoMember(4)]
        public int Mix { get; set; }
        [ProtoMember(5)]
        public VinProto[] Vin { get; set; }
        [ProtoMember(6)]
        public VoutProto[] Vout { get; set; }
        [ProtoMember(7)]
        public RCTProto[] Rct { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();

            if (TxnId == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "TxnId" }));
            }
            if (TxnId.Length != 48)
            {
                results.Add(new ValidationResult("Range exeption", new[] { "TxnId" }));
            }
            if (Mix != 17)
            {
                results.Add(new ValidationResult("Range exeption", new[] { "Mix" }));
            }
            if (Rct == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "RingSig" }));
            }
            if (Ver != 0x1)
            {
                results.Add(new ValidationResult("Incorrect number", new[] { "Version" }));
            }
            if (Vin == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Vin" }));
            }
            if (Vout == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Vout" }));
            }

            foreach (var bp in Bp)
            {

            }

            foreach (var vi in Vin)
            {

            }

            foreach (var vo in Vout)
            {

            }

            foreach (var rct in Rct)
            {

            }

            return results;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToIdentifier()
        {
            return ToHash().ByteToHex().ToBytes();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] ToHash()
        {
            return Helper.Util.SHA384ManagedHash(Stream());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToKeyImage()
        {
            byte[] hash;
            using (var ts = new Helper.TangramStream())
            {
                Vin.ForEach(x => ts.Append(x.Key.K_Image));
                hash = Helper.Util.SHA384ManagedHash(ts.ToArray());
            }

            return hash;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] Stream()
        {
            byte[] stream;
            using (var ts = new Helper.TangramStream())
            {
                ts
                .Append(Mix)
                .Append(Ver);

                foreach (var bp in Bp)
                {
                    ts.Append(bp.Proof);
                }

                foreach (var vin in Vin)
                {
                    ts.Append(vin.Key.K_Image);
                }

                foreach (var vout in Vout)
                {
                    ts
                      .Append(vout.A)
                      .Append(vout.C)
                      .Append(vout.E)
                      .Append(vout.N)
                      .Append(vout.P)
                      .Append(vout.Scr ?? string.Empty)
                      .Append(vout.T.ToString())
                      .Append(vout.UNLK);
                }

                foreach (var rct in Rct)
                {
                    ts
                      .Append(rct.I)
                      .Append(rct.M)
                      .Append(rct.P)
                      .Append(rct.S);
                }

                stream = ts.ToArray();
            }

            return stream;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Cast<T>()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
