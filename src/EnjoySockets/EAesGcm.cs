// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EnjoySockets
{
    internal class EAesGcm
    {
        internal byte[] Key { get; private set; } = new byte[32];

        AesGcm _cipherSend, _cipherReceive;
        ETCPSocketType _socketType;

        public EAesGcm(ETCPSocketType type)
        {
            RandomNumberGenerator.Fill(Key);
#if NET8_0
            _cipherSend = new AesGcm(Key, 16);
            _cipherReceive = new AesGcm(Key, 16);
#else
            _cipherSend = new AesGcm(Key);
            _cipherReceive = new AesGcm(Key);
#endif
            _socketType = type;
        }

        /// <summary>
        /// Sets a new AES-256-GCM key derived using HKDF (SHA-256).
        /// </summary>
        /// <param name="key">Input key material (must be exactly 32 bytes).</param>
        /// <param name="salt">Salt value used for HKDF key derivation.</param>
        /// <returns>
        /// true if the key was successfully derived and applied; otherwise false.
        /// </returns>
        public bool SetKey(byte[]? key, ReadOnlySpan<byte> salt)
        {
            if (key != null && key.Length == 32)
            {
                ReadOnlySpan<byte> info = "AES-256-GCM"u8;
                HKDF.DeriveKey(HashAlgorithmName.SHA256, key, Key, salt, info);
                AppendNewKey();
                return true;
            }
            else
            {
                return false;
            }
        }

        void AppendNewKey()
        {
            _cipherSend.Dispose();
            _cipherReceive.Dispose();
#if NET8_0
            _cipherSend = new AesGcm(Key, 16);
            _cipherReceive = new AesGcm(Key, 16);
#else
            _cipherSend = new AesGcm(Key);
            _cipherReceive = new AesGcm(Key);
#endif
        }

        ulong _counter = 1;
        public bool Encrypt(Span<byte> nonce, ReadOnlySpan<byte> plainBytes, Span<byte> buffer, Span<byte> tag)
        {
            //set nonce
            if (nonce.Length != 12) return false;
            if (_socketType == ETCPSocketType.Server)
            {
                RandomNumberGenerator.Fill(nonce.Slice(8, 4));
                BinaryPrimitives.WriteUInt64LittleEndian(nonce.Slice(0, 8), ++_counter);
            }
            else
            {
                RandomNumberGenerator.Fill(nonce.Slice(0, 4));
                BinaryPrimitives.WriteUInt64LittleEndian(nonce.Slice(4, 8), ++_counter);
            }

            //encrypt
            try
            {
                _cipherSend.Encrypt(nonce, plainBytes, buffer, tag);
                return true;
            }
            catch { return false; }
        }

        public bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipherBytes, Span<byte> buffer, ReadOnlySpan<byte> tag)
        {
            try
            {
                _cipherReceive.Decrypt(nonce, cipherBytes, tag, buffer);
                return true;
            }
            catch { return false; }
        }

        #region ECDH

        // id-ecPublicKey
        private static readonly byte[] EcPublicKeyOid =
        {
            0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01
        };

        // secp256r1 (1.2.840.10045.3.1.7)
        private static readonly byte[] Secp256r1Oid =
        {
            0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07
        };

        // secp384r1 (1.3.132.0.34)
        private static readonly byte[] Secp384r1Oid =
        {
            0x06, 0x05, 0x2B, 0x81, 0x04, 0x00, 0x22
        };

        // secp521r1 (1.3.132.0.35)
        private static readonly byte[] Secp521r1Oid =
        {
            0x06, 0x05, 0x2B, 0x81, 0x04, 0x00, 0x23
        };

        private struct CurveInfo
        {
            public int CoordinateSize;
            public int SpkiSize;
            public ReadOnlySpan<byte> CurveOid => curveOid;
            private readonly byte[] curveOid;
            public CurveInfo(int coordinateSize, int spkiSize, byte[] oid)
            {
                CoordinateSize = coordinateSize;
                SpkiSize = spkiSize;
                curveOid = oid;
            }
        }

        private static readonly CurveInfo P256 =
            new CurveInfo(32, 91, Secp256r1Oid);   // SPKI size 91

        private static readonly CurveInfo P384 =
            new CurveInfo(48, 120, Secp384r1Oid);  // SPKI size 120

        private static readonly CurveInfo P521 =
            new CurveInfo(66, 158, Secp521r1Oid);  // SPKI size 158

        internal static int ExportSpki(ECDiffieHellman ec, Span<byte> buffer)
        {
            var p = ec.ExportParameters(false);
            if (p.Q.X == null || p.Q.Y == null) return 0;

            CurveInfo ci = p.Curve.Oid.Value switch
            {
                "1.2.840.10045.3.1.7" => P256, // nistP256
                "1.3.132.0.34" => P384,        // nistP384
                "1.3.132.0.35" => P521,        // nistP521
                _ => default
            };

            if (ci.CurveOid == null) return 0;

            int coord = ci.CoordinateSize;
            int ecPointSize = 1 + 2 * coord; // 04 + X + Y

            // BIT STRING: Tag(1) + Len(?) + UnusedBits(1) + Data
            int bitStringDataLen = 1 + ecPointSize;
            int bitStringHeaderLen = bitStringDataLen > 127 ? 3 : 2;
            int bitStringTotal = bitStringHeaderLen + bitStringDataLen;

            // AlgorithmIdentifier: Tag(1) + Len(1) + OIDs
            int algIdLen = 2 + EcPublicKeyOid.Length + ci.CurveOid.Length;

            // SPKI: Tag(1) + Len(?) + AlgId + BitString
            int spkiDataLen = algIdLen + bitStringTotal;
            int spkiHeaderLen = spkiDataLen > 127 ? 3 : 2;
            int totalSize = spkiHeaderLen + spkiDataLen;

            if (buffer.Length < totalSize) return 0;

            int o = 0;

            // ---- SPKI SEQUENCE
            buffer[o++] = 0x30;
            if (spkiDataLen > 127)
            {
                buffer[o++] = 0x81;
            }
            buffer[o++] = (byte)spkiDataLen;

            // ---- AlgorithmIdentifier SEQUENCE
            buffer[o++] = 0x30;
            buffer[o++] = (byte)(algIdLen - 2);
            EcPublicKeyOid.CopyTo(buffer[o..]);
            o += EcPublicKeyOid.Length;
            ci.CurveOid.CopyTo(buffer[o..]);
            o += ci.CurveOid.Length;

            // ---- BIT STRING
            buffer[o++] = 0x03;
            if (bitStringDataLen > 127)
            {
                buffer[o++] = 0x81;
            }
            buffer[o++] = (byte)bitStringDataLen;
            buffer[o++] = 0x00; // 0 unused bits

            // ---- EC Point
            buffer[o++] = 0x04; // uncompressed

            CopyCoordinate(p.Q.X, buffer.Slice(o, coord));
            o += coord;
            CopyCoordinate(p.Q.Y, buffer.Slice(o, coord));
            o += coord;

            return o;
        }

        private static void CopyCoordinate(byte[] source, Span<byte> target)
        {
            if (source.Length == target.Length)
                source.CopyTo(target);
            else if (source.Length > target.Length)
                source.AsSpan(source.Length - target.Length).CopyTo(target);
            else
                source.CopyTo(target.Slice(target.Length - source.Length));
        }

        #endregion
    }
}
