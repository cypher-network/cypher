// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Models;
using CypherNetwork.Models.Messages;
using CypherNetwork.Persistence;
using Dawn;
using Libsecp256k1Zkp.Net;
using libsignal.ecc;
using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json;
using Serilog;

namespace CypherNetwork.Cryptography;

public interface ICrypto
{
    Task<Models.KeyPair> GetOrUpsertKeyNameAsync(string keyName);
    Task<byte[]> GetPublicKeyAsync(string keyName);
    Task<SignatureResponse> SignAsync(string keyName, byte[] message);
    bool VerifySignature(byte[] signature, byte[] message);
    bool VerifySignature(VerifySignatureManualRequest verifySignatureManualRequest);
    byte[] GetCalculateVrfSignature(ECPrivateKey ecPrivateKey, byte[] msg);
    byte[] GetVerifyVrfSignature(ECPublicKey ecPublicKey, byte[] msg, byte[] sig);
    // byte[] EncryptChaCha20Poly1305(byte[] data, byte[] key, byte[] associatedData, out byte[] tag, out byte[] nonce);
    byte[] DecryptChaCha20Poly1305(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> associatedData, ReadOnlyMemory<byte> tag, ReadOnlyMemory<byte> nonce);
    byte[] BoxSeal(ReadOnlySpan<byte> msg, ReadOnlySpan<byte> publicKey);
    byte[] BoxSealOpen(ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> secretKey, ReadOnlySpan<byte> publicKey);
}

/// <summary>
/// </summary>
public class Crypto : ICrypto
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger _logger;
    private readonly IUnitOfWork _unitOfWork;

    private IDataProtector _dataProtector;
    private DataProtection _protectionProto;

    /// <summary>
    /// </summary>
    /// <param name="dataProtectionProvider"></param>
    /// <param name="unitOfWork"></param>
    /// <param name="logger"></param>
    public Crypto(IDataProtectionProvider dataProtectionProvider, IUnitOfWork unitOfWork, ILogger logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _unitOfWork = unitOfWork;
        _logger = logger.ForContext("SourceContext", nameof(Crypto));
    }

    /// <summary>
    /// </summary>
    /// <param name="keyName"></param>
    /// <returns></returns>
    public async Task<Models.KeyPair> GetOrUpsertKeyNameAsync(string keyName)
    {
        Guard.Argument(keyName, nameof(keyName)).NotNull().NotWhiteSpace();
        Models.KeyPair kp = null;
        try
        {
            _dataProtector = _dataProtectionProvider.CreateProtector(keyName);
            _protectionProto = await _unitOfWork.DataProtectionPayload.GetAsync(keyName.ToBytes());
            if (_protectionProto == null)
            {
                _protectionProto = new DataProtection
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
    /// </summary>
    /// <param name="keyName"></param>
    /// <returns></returns>
    public async Task<byte[]> GetPublicKeyAsync(string keyName)
    {
        var kp = await GetOrUpsertKeyNameAsync(keyName);
        return kp?.PublicKey;
    }

    /// <summary>
    /// </summary>
    /// <param name="keyName"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public async Task<SignatureResponse> SignAsync(string keyName, byte[] message)
    {
        Guard.Argument(keyName, nameof(keyName)).NotNull().NotWhiteSpace();
        Guard.Argument(message, nameof(message)).NotNull();
        SignatureResponse signatureResponse = null;
        try
        {
            var keyPair = await GetOrUpsertKeyNameAsync(keyName);
            var signature = Curve.calculateSignature(Curve.decodePrivatePoint(keyPair.PrivateKey), message);
            signatureResponse = new SignatureResponse(signature, keyPair.PublicKey);
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to sign the message");
        }

        return signatureResponse;
    }

    /// <summary>
    /// </summary>
    /// <param name="signature"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public bool VerifySignature(byte[] signature, byte[] message)
    {
        Guard.Argument(signature, nameof(signature)).NotNull();
        Guard.Argument(message, nameof(message)).NotNull();
        var verified = false;
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
    /// </summary>
    /// <param name="verifySignatureManualRequest"></param>
    /// <returns></returns>
    public bool VerifySignature(VerifySignatureManualRequest verifySignatureManualRequest)
    {
        Guard.Argument(verifySignatureManualRequest, nameof(verifySignatureManualRequest)).NotNull();
        Guard.Argument(verifySignatureManualRequest.Signature, nameof(verifySignatureManualRequest.Signature))
            .NotNull();
        Guard.Argument(verifySignatureManualRequest.PublicKey, nameof(verifySignatureManualRequest.PublicKey))
            .NotNull();
        Guard.Argument(verifySignatureManualRequest.Message, nameof(verifySignatureManualRequest.Message)).NotNull();
        var verified = false;
        try
        {
            verified = Curve.verifySignature(Curve.decodePoint(verifySignatureManualRequest.PublicKey, 0),
                verifySignatureManualRequest.Message, verifySignatureManualRequest.Signature);
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
    /// <param name="ecPrivateKey"></param>
    /// <param name="msg"></param>
    /// <returns></returns>
    public byte[] GetCalculateVrfSignature(ECPrivateKey ecPrivateKey, byte[] msg)
    {
        Guard.Argument(ecPrivateKey, nameof(ecPrivateKey)).NotNull();
        Guard.Argument(msg, nameof(msg)).NotNull().NotEmpty();
        var calculateVrfSignature = Curve.calculateVrfSignature(ecPrivateKey, msg);
        return calculateVrfSignature;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="ecPublicKey"></param>
    /// <param name="msg"></param>
    /// <param name="sig"></param>
    /// <returns></returns>
    public byte[] GetVerifyVrfSignature(ECPublicKey ecPublicKey, byte[] msg, byte[] sig)
    {
        Guard.Argument(ecPublicKey, nameof(ecPublicKey)).NotNull();
        Guard.Argument(sig, nameof(sig)).NotNull().NotEmpty();
        Guard.Argument(msg, nameof(msg)).NotNull().NotEmpty();
        var vrfSignature = Curve.verifyVrfSignature(ecPublicKey, msg, sig);
        return vrfSignature;
    }
    
    // public byte[] EncryptChaCha20Poly1305(byte[] data, byte[] key, byte[] associatedData, out byte[] tag,
    //     out byte[] nonce)
    // {
    //     tag = new byte[Chacha20poly1305.Abytes()];
    //     nonce = GetRandomData();
    //     var cipherText = new byte[data.Length + (int)Chacha20poly1305.Abytes()];
    //     var cipherTextLength = 0ul;
    //     return Chacha20poly1305.Encrypt(cipherText, ref cipherTextLength, data, (ulong)data.Length,
    //         associatedData, (ulong)associatedData.Length, null, nonce, key) != 0
    //         ? Array.Empty<byte>()
    //         : cipherText;
    // }

    /// <summary>
    /// </summary>
    /// <param name="data"></param>
    /// <param name="key"></param>
    /// <param name="associatedData"></param>
    /// <param name="tag"></param>
    /// <param name="nonce"></param>
    /// <returns></returns>
    public unsafe byte[] DecryptChaCha20Poly1305(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> associatedData, ReadOnlyMemory<byte> tag, ReadOnlyMemory<byte> nonce)
    {
        var decryptedData = stackalloc byte[data.Length];
        var decryptedDataLength = 0ul;
        int result;
        fixed (byte* dPtr = data.Span, aPrt = associatedData.Span, nPrt = nonce.Span, kPrt = key.Span)
        {
            result = LibSodiumChacha20Poly1305.Decrypt(decryptedData, ref decryptedDataLength, null, dPtr,
                (ulong)data.Length, aPrt, (ulong)associatedData.Length, nPrt, kPrt);
        }

        var destination = new Span<byte>(decryptedData, (int)decryptedDataLength);
        return result != 0 ? Array.Empty<byte>() : destination.Slice(0, (int)decryptedDataLength).ToArray();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="cipher"></param>
    /// <param name="secretKey"></param>
    /// <param name="publicKey"></param>
    /// <returns></returns>
    public unsafe byte[] BoxSealOpen(ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> secretKey, ReadOnlySpan<byte> publicKey)
    {
        var len = cipher.Length - (int)LibSodiumBox.Sealbytes();
        var msg = stackalloc byte[len];
        int result;
        fixed (byte* cPtr = cipher, pkPtr = publicKey, skPtr = secretKey)
        {
            result = LibSodiumBox.SealOpen(msg, cPtr, (ulong)cipher.Length, pkPtr, skPtr);
        }
        
        var destination = new Span<byte>(msg, len);
        return result != 0 ? Array.Empty<byte>() : destination.Slice(0, len).ToArray();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="publicKey"></param>
    /// <returns></returns>
    public unsafe byte[] BoxSeal(ReadOnlySpan<byte> msg, ReadOnlySpan<byte> publicKey)
    {
        var cipher = new byte[msg.Length + (int)LibSodiumBox.Sealbytes()];
        var result = 0;
        fixed (byte* mPtr = msg, cPtr = cipher, pkPtr = publicKey)
        {
            result = LibSodiumBox.Seal(cPtr, mPtr, (ulong)msg.Length, pkPtr);
        }

        return result != 0 ? Array.Empty<byte>() : cipher;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public static Models.KeyPair GenerateKeyPair()
    {
        var keys = Curve.generateKeyPair();
        return new Models.KeyPair(keys.getPrivateKey().serialize(), keys.getPublicKey().serialize());
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public static byte[] GetRandomData()
    {
        using var secp256K1 = new Secp256k1();
        return secp256K1.RandomSeed();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private Models.KeyPair GetKeyPair()
    {
        Guard.Argument(_protectionProto, nameof(_protectionProto)).NotNull();
        Guard.Argument(_protectionProto.Payload, nameof(_protectionProto.Payload)).NotNull().NotWhiteSpace();
        var unprotectedPayload = _dataProtector.Unprotect(_protectionProto.Payload);
        var definition = new { PrivateKey = string.Empty, PublicKey = string.Empty };
        var message = JsonConvert.DeserializeAnonymousType(unprotectedPayload, definition);
        return new Models.KeyPair(Convert.FromBase64String(message.PrivateKey),
            Convert.FromBase64String(message.PublicKey));
    }
}