// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;
using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

using libsignal.ecc;

using Newtonsoft.Json;

using Dawn;

using CYPCore.Models;
using CYPCore.Extentions;
using CYPCore.Persistence;

namespace CYPCore.Cryptography
{
    public class Signing : ISigning
    {
        private const string TableDataProtectionPayload = "DataProtectionPayload";

        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly ILogger _logger;
        private readonly IUnitOfWork _unitOfWork;

        private IDataProtector _dataProtector;
        private DataProtectionPayloadProto _protectionPayloadProto;

        public string DefaultSigningKeyName => "DefaultSigning.Key";

        public Signing(IDataProtectionProvider dataProtectionProvider, IUnitOfWork unitOfWork, ILogger<Signing> logger)
        {
            _dataProtectionProvider = dataProtectionProvider;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private KeyPair GetKeyPair()
        {
            Guard.Argument(_protectionPayloadProto, nameof(_protectionPayloadProto)).NotNull();
            Guard.Argument(_protectionPayloadProto.Payload, nameof(_protectionPayloadProto.Payload)).NotNull().NotWhiteSpace();

            var unprotectedPayload = _dataProtector.Unprotect(_protectionPayloadProto.Payload);
            var definition = new { PrivateKey = string.Empty, PublicKey = string.Empty };
            var message = JsonConvert.DeserializeAnonymousType(unprotectedPayload, definition);

            return new KeyPair(Convert.FromBase64String(message.PrivateKey), Convert.FromBase64String(message.PublicKey));
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyName"></param>
        /// <returns></returns>
        public async Task<KeyPair> GetOrUpsertKeyName(string keyName)
        {
            Guard.Argument(keyName, nameof(keyName)).NotNull().NotWhiteSpace();

            KeyPair kp = null;

            try
            {
                _dataProtector = _dataProtectionProvider.CreateProtector(keyName);
                _protectionPayloadProto = await _unitOfWork.DataProtectionPayload.FirstOrDefaultAsync(x => x.FriendlyName == keyName);

                if (_protectionPayloadProto == null)
                {
                    _protectionPayloadProto = new DataProtectionPayloadProto
                    {
                        FriendlyName = keyName,
                        Payload = _dataProtector.Protect(JsonConvert.SerializeObject(GenerateKeyPair()))
                    };

                    var stored = await _unitOfWork.DataProtectionPayload.PutAsync(_protectionPayloadProto, _protectionPayloadProto.FriendlyName.ToBytes());
                    if (stored == null)
                    {
                        _logger.LogError($"<<< SigningProvider.GetOrUpsertKeyName >>>: Unable to save protection key payload for: {keyName}");
                        return null;
                    }
                }

                kp = GetKeyPair();
            }
            catch(CryptographicException ex)
            {
                _logger.LogCritical($"<<< SigningProvider.GetOrUpsertKeyName >>>: {ex}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< SigningProvider.GetOrUpsertKeyName >>>: {ex}");
            }

            return kp;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private static KeyPair GenerateKeyPair()
        {
            var keys = Curve.generateKeyPair();
            return new KeyPair(keys.getPrivateKey().serialize(), keys.getPublicKey().serialize());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyName"></param>
        /// <returns></returns>
        public async Task<byte[]> GePublicKey(string keyName)
        {
            var kp = await GetOrUpsertKeyName(keyName);
            return kp.PublicKey;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyName"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public async Task<byte[]> Sign(string keyName, byte[] message)
        {
            Guard.Argument(keyName, nameof(keyName)).NotNull().NotWhiteSpace();
            Guard.Argument(message, nameof(message)).NotNull();

            byte[] signature = null;

            try
            {
                var keyPair = await GetOrUpsertKeyName(keyName);
                signature = Curve.calculateSignature(Curve.decodePrivatePoint(keyPair.PrivateKey), message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< SigningActor.Sign >>>: {ex}");
            }

            return signature;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="signature"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public bool VerifySignature(byte[] signature, byte[] message)
        {
            Guard.Argument(signature, nameof(signature)).NotNull();
            Guard.Argument(message, nameof(message)).NotNull();

            bool verified = false;

            try
            {
                var keyPair = GetKeyPair();
                verified = Curve.verifySignature(Curve.decodePoint(keyPair.PublicKey, 0), message, signature);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< SigningActor.VerifiySignature >>>: {ex}");
            }

            return verified;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="signature"></param>
        /// <param name="publicKey"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public bool VerifySignature(byte[] signature, byte[] publicKey, byte[] message)
        {
            Guard.Argument(signature, nameof(signature)).NotNull();
            Guard.Argument(publicKey, nameof(publicKey)).NotNull();
            Guard.Argument(message, nameof(message)).NotNull();

            bool verified = false;

            try
            {
                verified = Curve.verifySignature(Curve.decodePoint(publicKey, 0), message, signature);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< SigningActor.VerifiySignature(byte[] signature, byte[] publicKey, byte[] message) >>>: {ex}");
            }

            return verified;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="privateKey"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public byte[] CalculateVrfSignature(ECPrivateKey privateKey, byte[] message)
        {
            return Curve.calculateVrfSignature(privateKey, message);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="publicKey"></param>
        /// <param name="message"></param>
        /// <param name="signature"></param>
        /// <returns></returns>
        public byte[] VerifyVrfSignature(ECPublicKey publicKey, byte[] message, byte[] signature)
        {
            return Curve.verifyVrfSignature(publicKey, message, signature);
        }
    }
}
