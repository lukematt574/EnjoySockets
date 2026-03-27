// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Security.Cryptography;
using System.Text;

namespace EnjoySockets
{
    /// <summary>
    /// Extended RSA helper that provides:
    /// - RSA encryption (OAEP SHA-256)
    /// - RSA decryption
    /// - RSA-PSS signing (SHA-256)
    /// - RSA signature verification
    ///
    /// The class keeps two independent RSA providers:
    /// - one for encryption/decryption
    /// - one for signing/verification
    ///
    /// Methods are virtual and asynchronous to allow overriding
    /// (e.g., loading keys from external sources like HSM, API, or vault).
    /// </summary>
    public class ERSA
    {
        public static ReadOnlyMemory<byte> HandshakeHeader { get; private set; }
        static byte[] _handshakeHeader { get; set; }

        static ERSA()
        {
            var name = Encoding.ASCII.GetBytes("EnjoySocketsProtocol v1 CertificateVerify");
            _handshakeHeader = new byte[name.Length + 1];
            name.CopyTo(_handshakeHeader, 0);
            HandshakeHeader = new ReadOnlyMemory<byte>(_handshakeHeader);
        }

        RSA? ProviderToEncrypt, ProviderToSign;

        /// <summary>
        /// Initializes an empty instance.
        /// 
        /// This constructor is intended for scenarios where
        /// derived classes initialize RSA providers asynchronously
        /// or from external sources.
        /// </summary>
        public ERSA() { }

        /// <summary>
        /// Initializes a new ERSA instance using PEM encoded keys.
        /// </summary>
        /// <param name="pemKeyToEncrypt">PEM key used for encryption/decryption.</param>
        /// <param name="pemKeyToSign">PEM key used for signing/verification.</param>
        /// <param name="keyLength">RSA key size (default 4096).</param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ArgumentException"/>
        public ERSA(string pemKeyToEncrypt, string pemKeyToSign, int keyLength = 4096)
        {
            if (string.IsNullOrWhiteSpace(pemKeyToEncrypt))
                throw new ArgumentNullException(nameof(pemKeyToEncrypt));

            if (string.IsNullOrWhiteSpace(pemKeyToSign))
                throw new ArgumentNullException(nameof(pemKeyToSign));

            ProviderToEncrypt = CreateRsaFromPem(pemKeyToEncrypt, keyLength);
            ProviderToSign = CreateRsaFromPem(pemKeyToSign, keyLength);
        }

        private ERSA(RSAParameters encryptParams, RSAParameters signParams)
        {
            ProviderToEncrypt = RSA.Create();
            ProviderToEncrypt.ImportParameters(encryptParams);

            ProviderToSign = RSA.Create();
            ProviderToSign.ImportParameters(signParams);
        }

        RSA CreateRsaFromPem(string pemKey, int keyLength)
        {
            var rsa = RSA.Create(keyLength);
            rsa.ImportFromPem(pemKey);
            return rsa;
        }

        /// <summary>
        /// Generates a new RSA private/public key pair in PEM format.
        /// </summary>
        /// <param name="keyLength">Key size (default 4096).</param>
        /// <returns>
        /// Tuple:
        /// - PrivateKeyPem
        /// - PublicKeyPem
        /// </returns>
        public static (string PrivateKeyPem, string PublicKeyPem) GeneratePrivatePublicPEM(int keyLength = 4096)
        {
            RSA? rsa = null;
            try
            {
                rsa = RSA.Create(keyLength);

                var privateKeyBytes = rsa.ExportPkcs8PrivateKey();
                var sbPrivate = new StringBuilder();
                sbPrivate.Append("-----BEGIN PRIVATE KEY-----");
                sbPrivate.Append(Convert.ToBase64String(privateKeyBytes));
                sbPrivate.Append("-----END PRIVATE KEY-----");
                string privateKeyPem = sbPrivate.ToString();

                var publicKeyBytes = rsa.ExportSubjectPublicKeyInfo();
                var sbPublic = new StringBuilder();
                sbPublic.Append("-----BEGIN PUBLIC KEY-----");
                sbPublic.Append(Convert.ToBase64String(publicKeyBytes));
                sbPublic.Append("-----END PUBLIC KEY-----");
                string publicKeyPem = sbPublic.ToString();

                return (privateKeyPem, publicKeyPem);
            }
            catch
            {
                return (string.Empty, string.Empty);
            }
            finally
            {
                rsa?.Dispose();
            }
        }

        /// <summary>
        /// Creates a new ERSA instance with cloned RSA parameters.
        /// </summary>
        /// <returns>A new ERSA instance containing duplicated keys.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public virtual ERSA CloneObjectToServer()
        {
            if (ProviderToEncrypt == null || ProviderToSign == null)
                throw new InvalidOperationException("RSA providers are not initialized.");

            return new ERSA(
                ProviderToEncrypt.ExportParameters(true),
                ProviderToSign.ExportParameters(true));
        }

        /// <summary>
        /// Encrypts the provided plain data into the specified buffer using RSA with OAEP SHA-256 padding.
        /// </summary>
        /// <param name="text">Plain data to be encrypted.</param>
        /// <param name="destination">The buffer where the encrypted data will be stored.</param>
        /// <returns>
        /// The number of bytes written to <paramref name="destination"/>, 
        /// or 0 if the encryption failed.
        /// </returns>
        public virtual Task<int> Encrypt(ReadOnlyMemory<byte> text, Memory<byte> destination)
        {
            try
            {
#if NET8_0
                return Task.FromResult(
                    ProviderToEncrypt?.Encrypt(
                        text.Span,
                        destination.Span,
                        RSAEncryptionPadding.OaepSHA256
                    ) ?? 0
                );
#else
                byte[] input = text.ToArray();
                byte[] encrypted = ProviderToEncrypt?.Encrypt(input, RSAEncryptionPadding.OaepSHA256) ?? [];

                if (encrypted.Length < 1)
                    return Task.FromResult(0);

                int count = Math.Min(encrypted.Length, destination.Length);
                encrypted.AsSpan(0, count).CopyTo(destination.Span);
                return Task.FromResult(count);
#endif
            }
            catch
            {
                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// Decrypts the provided RSA-encrypted data into the specified buffer using RSA with OAEP SHA-256 padding.
        /// </summary>
        /// <param name="text">The RSA-encrypted data to be decrypted.</param>
        /// <param name="destination">The buffer where the decrypted data will be stored.</param>
        /// <returns>
        /// The number of bytes written to <paramref name="destination"/>, 
        /// or 0 if the decryption failed.
        /// </returns>
        public virtual Task<int> Decrypt(ReadOnlyMemory<byte> text, Memory<byte> destination)
        {
            try
            {
#if NET8_0
                return Task.FromResult(
                    ProviderToEncrypt?.Decrypt(
                        text.Span,
                        destination.Span,
                        RSAEncryptionPadding.OaepSHA256
                    ) ?? 0
                );
#else
                byte[] input = text.ToArray();
                byte[] decrypted = ProviderToEncrypt?.Decrypt(input, RSAEncryptionPadding.OaepSHA256) ?? [];

                if (decrypted.Length < 1)
                    return Task.FromResult(0);

                int count = Math.Min(decrypted.Length, destination.Length);
                decrypted.AsSpan(0, count).CopyTo(destination.Span);
                return Task.FromResult(count);
#endif
            }
            catch
            {
                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// Signs the provided data using RSA with PSS padding and SHA-256 hash algorithm.
        /// </summary>
        /// <param name="text">The data to be signed.</param>
        /// <param name="destination">The buffer where the generated signature will be stored.</param>
        /// <returns>
        /// The number of bytes written to <paramref name="destination"/>, 
        /// or 0 if the signing operation failed.
        /// </returns>
        public virtual Task<int> SignDataRsa(ReadOnlyMemory<byte> text, Memory<byte> destination)
        {
            try
            {
#if NET8_0
                return Task.FromResult(
                    ProviderToSign?.SignData(
                        text.Span,
                        destination.Span,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pss
                    ) ?? 0
                );
#else
                if (ProviderToSign == null)
                    return Task.FromResult(0);

                byte[] textArray = text.ToArray();

                byte[] signature = ProviderToSign.SignData(
                    textArray,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss
                );

                if (signature.Length < 1)
                    return Task.FromResult(0);

                int count = Math.Min(signature.Length, destination.Length);
                signature.AsSpan(0, count).CopyTo(destination.Span);
                return Task.FromResult(count);
#endif
            }
            catch
            {
                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// Verifies an RSA signature created using PSS padding and SHA-256 hash algorithm.
        /// </summary>
        /// <param name="data">The original data that was signed.</param>
        /// <param name="signature">The RSA signature to be verified.</param>
        /// <returns>
        /// <c>true</c> if the signature is valid; otherwise, <c>false</c>.
        /// </returns>
        public virtual Task<bool> VerifyDataRsa(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature)
        {
            try
            {
                return Task.FromResult(
                    ProviderToSign?.VerifyData(
                        data.Span,
                        signature.Span,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pss
                    ) ?? false
                );
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}
