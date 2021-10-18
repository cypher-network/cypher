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
using CYPCore.Network.Messages;
using CYPCore.Persistence;
using Proto;

namespace CYPCore.Cryptography
{
    /// <summary>
    /// 
    /// </summary>
    public class CryptoKeySign: IActor
    {
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly ILogger _logger;
        private readonly IUnitOfWork _unitOfWork;

        private IDataProtector _dataProtector;
        private DataProtection _protectionProto;

        public static string DefaultSigningKeyName => "DefaultSigning.Key";

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dataProtectionProvider"></param>
        /// <param name="unitOfWork"></param>
        /// <param name="logger"></param>
        public CryptoKeySign(IDataProtectionProvider dataProtectionProvider, IUnitOfWork unitOfWork, ILogger logger)
        {
            _dataProtectionProvider = dataProtectionProvider;
            _unitOfWork = unitOfWork;
            _logger = logger.ForContext("SourceContext", nameof(CryptoKeySign));
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public Task ReceiveAsync(IContext context) => context.Message switch
        {
            KeyPairRequest keyPairRequest => OnGetOrUpdateKeyPair(keyPairRequest, context),
            SignatureRequest signatureRequest => OnGetSignature(signatureRequest, context),
            VerifySignatureAutoRequest verifySignatureRequest => OnGetAutoVerifySignature(verifySignatureRequest, context),
            VerifySignatureManualRequest verifySignatureManualRequest => OnGetManualVerifySignature(verifySignatureManualRequest, context),
            CalculateVrfRequest calculateVrfSignatureRequest => OnGetCalculateVrfSignature(calculateVrfSignatureRequest, context),
            VerifyVrfSignatureRequest verifyVrfSignatureRequest => OnGetVerifyVrfSignature(verifyVrfSignatureRequest, context),
            _ => Task.CompletedTask
        };

        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyPairRequest"></param>
        /// <param name="context"></param>
        private async Task OnGetOrUpdateKeyPair(KeyPairRequest keyPairRequest, IContext context)
        {
            Guard.Argument(keyPairRequest, nameof(keyPairRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            try
            {
                _dataProtector = _dataProtectionProvider.CreateProtector(keyPairRequest.KeyName);
                _protectionProto = await _unitOfWork.DataProtectionPayload.GetAsync(keyPairRequest.KeyName.ToBytes());
                if (_protectionProto is null)
                {
                    _protectionProto = new DataProtection
                    {
                        FriendlyName = keyPairRequest.KeyName,
                        Payload = _dataProtector.Protect(JsonConvert.SerializeObject(GenerateKeyPair()))
                    };
                    var saved = await _unitOfWork.DataProtectionPayload.PutAsync(keyPairRequest.KeyName.ToBytes(), _protectionProto);
                    if (!saved)
                    {
                        _logger.Here().Error("Unable to save protection key payload for: {@KeyName}", keyPairRequest.KeyName);
                        return;
                    }
                }
                
                context.Respond(new KeyPairResponse(GetKeyPair()));
                return;
            }
            catch (CryptographicException ex)
            {
                _logger.Here().Fatal(ex, "Cannot get keypair");
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get keypair");
            }
            
            context.Respond(new KeyPairResponse(null));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static KeyPair GenerateKeyPair()
        {
            var keys = Curve.generateKeyPair();
            return new KeyPair(keys.getPrivateKey().serialize(), keys.getPublicKey().serialize());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <param name="context"></param>
        private async Task OnGetSignature(SignatureRequest signatureRequest, IContext context)
        {
            Guard.Argument(signatureRequest, nameof(signatureRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            try
            {
                var props = Props(_dataProtectionProvider, _unitOfWork, _logger);
                var self = context.Spawn(props);
                var keyPairResponse = await context.RequestAsync<KeyPairResponse>(self, new KeyPairRequest(signatureRequest.KeyName));
                var signature = Curve.calculateSignature(Curve.decodePrivatePoint(keyPairResponse.KeyPair.PrivateKey), signatureRequest.Message);
                context.Respond(new SignatureResponse(signature, keyPairResponse.KeyPair.PublicKey));
                return;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to create the signature");
            }
            
            context.Respond(new SignatureResponse(null, null));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="verifySignatureRequest"></param>
        /// <param name="context"></param>
        private async Task OnGetAutoVerifySignature(VerifySignatureAutoRequest verifySignatureRequest, IContext context)
        {
            Guard.Argument(verifySignatureRequest, nameof(verifySignatureRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            try
            {
                var props = Props(_dataProtectionProvider, _unitOfWork, _logger);
                var self = context.Spawn(props);
                var keyPairResponse = await context.RequestAsync<KeyPairResponse>(self,
                    new KeyPairRequest(verifySignatureRequest.KeyName));
                var verified = Curve.verifySignature(Curve.decodePoint(keyPairResponse.KeyPair.PublicKey, 0),
                    verifySignatureRequest.Message, verifySignatureRequest.Signature);
                context.Respond(new VerifySignatureAutoResponse(verified));
                return;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to verify the signature");
            }

            context.Respond(new VerifySignatureAutoResponse(false));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="verifySignatureManualRequest"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private Task OnGetManualVerifySignature(VerifySignatureManualRequest verifySignatureManualRequest,
            IContext context)
        {
            Guard.Argument(verifySignatureManualRequest, nameof(verifySignatureManualRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            try
            {
                var verified = Curve.verifySignature(Curve.decodePoint(verifySignatureManualRequest.PublicKey, 0),
                    verifySignatureManualRequest.Message, verifySignatureManualRequest.Signature);
                context.Respond(new VerifySignatureManualResponse(verified));
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot verify signature");
            }

            context.Respond(new VerifySignatureManualResponse(false));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="calculateVrfSignatureRequest"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private Task OnGetCalculateVrfSignature(CalculateVrfRequest calculateVrfSignatureRequest, IContext context)
        {
            Guard.Argument(calculateVrfSignatureRequest, nameof(calculateVrfSignatureRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            var signature = Curve.calculateVrfSignature(calculateVrfSignatureRequest.EcPrivateKey,
                calculateVrfSignatureRequest.Message);
            context.Respond(new CalculateVrfResponse(signature));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="verifyVrfSignatureRequest"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private Task OnGetVerifyVrfSignature(VerifyVrfSignatureRequest verifyVrfSignatureRequest, IContext context)
        {
            Guard.Argument(verifyVrfSignatureRequest, nameof(verifyVrfSignatureRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            var signature = Curve.verifyVrfSignature(verifyVrfSignatureRequest.EcPublicKey, verifyVrfSignatureRequest.Message, verifyVrfSignatureRequest.Signature);
            context.Respond(new VerifyVrfSignatureResponse(signature));
            return Task.CompletedTask;
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
            return new KeyPair(Convert.FromBase64String(message.PrivateKey),
                Convert.FromBase64String(message.PublicKey));
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="dataProtectionProvider"></param>
        /// <param name="unitOfWork"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public static Props Props(IDataProtectionProvider dataProtectionProvider, IUnitOfWork unitOfWork, ILogger logger)
        {
            var props = Proto.Props.FromProducer(() => new CryptoKeySign(dataProtectionProvider, unitOfWork, logger));
            return props;
        }
    }
}
