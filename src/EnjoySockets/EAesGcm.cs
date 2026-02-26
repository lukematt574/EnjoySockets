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
            CurveInfo ci;

            if (p.Q.X == null || p.Q.Y == null) return 0;

            if (p.Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value)
                ci = P256;
            else if (p.Curve.Oid.Value == ECCurve.NamedCurves.nistP384.Oid.Value)
                ci = P384;
            else if (p.Curve.Oid.Value == ECCurve.NamedCurves.nistP521.Oid.Value)
                ci = P521;
            else
                return 0;

            int Xlen = p.Q.X.Length;
            int Ylen = p.Q.Y.Length;

            if (Xlen != ci.CoordinateSize || Ylen != ci.CoordinateSize)
                return 0;

            if (buffer.Length < ci.SpkiSize)
                return 0;

            int coord = ci.CoordinateSize;
            int ecPointSize = 1 + coord + coord; // 04 + X + Y
            int bitStringTotal = ecPointSize + 3; // 03 len 00 + ecPoint
            int algIdLen = 2 + EcPublicKeyOid.Length + ci.CurveOid.Length;

            int spkiLen = 2 + algIdLen + bitStringTotal;

            if (spkiLen != ci.SpkiSize)
                return 0;

            int o = 0;

            // ---- SPKI SEQUENCE
            buffer[o++] = 0x30;
            buffer[o++] = (byte)(spkiLen - 2);

            // ---- AlgorithmIdentifier SEQUENCE
            buffer[o++] = 0x30;
            buffer[o++] = (byte)(algIdLen - 2);

            EcPublicKeyOid.CopyTo(buffer[o..]);
            o += EcPublicKeyOid.Length;

            ci.CurveOid.CopyTo(buffer[o..]);
            o += ci.CurveOid.Length;

            // ---- BIT STRING
            buffer[o++] = 0x03;
            buffer[o++] = (byte)(bitStringTotal - 2);
            buffer[o++] = 0x00;

            buffer[o++] = 0x04; // uncompressed point

            p.Q.X.CopyTo(buffer[o..]);
            o += coord;

            p.Q.Y.CopyTo(buffer[o..]);
            o += coord;

            return o;
        }

        #endregion
    }
}
