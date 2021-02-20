// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;
using System.Security.Cryptography;
using CYPCore.Extensions;
using Dawn;
using Microsoft.AspNetCore.DataProtection;
using libsignal.ecc;
using Newtonsoft.Json;
using Serilog;

using CYPCore.Models;
using CYPCore.Extentions;
using CYPCore.Persistence;

namespace CYPCore.Cryptography
{
    public class Signing : ISigning
    {
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly ILogger _logger;
        private readonly IUnitOfWork _unitOfWork;

        private IDataProtector _dataProtector;
        private DataProtectionProto _protectionProto;

        public string DefaultSigningKeyName => "DefaultSigning.Key";

        public Signing(IDataProtectionProvider dataProtectionProvider, IUnitOfWork unitOfWork, ILogger logger)
        {
            _dataProtectionProvider = dataProtectionProvider;
            _unitOfWork = unitOfWork;
            _logger = logger.ForContext("SourceContext", nameof(Signing));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private KeyPair GetKeyPair()
        {
            Guard.Argument(_protectionProto, nameof(_protectionProto)).NotNull();
            Guard.Argument(_protectionProto.Payload, nameof(_protectionProto.Payload)).NotNull().NotWhiteSpace();

            var unprotectedPayload = _dataProtector.Unprotect(_protectionProto.Payload);
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
                _protectionProto = await _unitOfWork.DataProtectionPayload.GetAsync(keyName.ToBytes());

                if (_protectionProto == null)
                {
                    _protectionProto = new DataProtectionProto
                    {
                        FriendlyName = keyName,
                        Payload = _dataProtector.Protect(JsonConvert.SerializeObject(GenerateKeyPair()))
                    };

                    var saved = await _unitOfWork.DataProtectionPayload.PutAsync(keyName.ToBytes(), _protectionProto);
                    if (!saved)
                    {
                        _logger.Here().Error("Unable to save protection key payload for: {@KeyName}", keyName);
                        return null;
                    }
                }

                kp = GetKeyPair();
            }
            catch (CryptographicException ex)
            {
                _logger.Here().Fatal(ex, "Cannot get keypair");
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get keypair");
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
        public async Task<byte[]> GetPublicKey(string keyName)
        {
            var kp = await GetOrUpsertKeyName(keyName);
            return kp?.PublicKey;
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
                _logger.Here().Error(ex, "Cannot sign");
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
                _logger.Here().Error(ex, "Cannot verify signature");
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
                _logger.Here().Error(ex, "Cannot verify signature");
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
