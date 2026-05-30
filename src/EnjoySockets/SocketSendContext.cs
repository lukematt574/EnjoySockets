// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading.Tasks.Sources;

namespace EnjoySockets
{
    internal sealed class SocketSendContext : IValueTaskSource<int>
    {
        internal SocketAsyncEventArgs SAEA { get; private set; }
        internal int TotalLength { get; private set; } = 0;

        readonly byte[] _array;
        ManualResetValueTaskSourceCore<int> _mrvtsc;
        int _offsetMemory = 0;

        const short _fullPacketHeaderEncrypt = ETCPSocket.PacketEncryptHeader + ETCPSocket.PacketPrefixLength;

        internal SocketSendContext(ushort maxFullEncryptPacket)
        {
            _array = new byte[maxFullEncryptPacket];
            SAEA = new SocketAsyncEventArgs();
            SAEA.Completed += SendCompleted;
            _mrvtsc = new ManualResetValueTaskSourceCore<int> { RunContinuationsAsynchronously = true };
        }

        void SendCompleted(object? s, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
                _mrvtsc.SetResult(e.BytesTransferred);
            else
                _mrvtsc.SetResult(-1);
        }

        internal bool SetToSend(ReadOnlyMemory<byte> dataToWrite)
        {
            TotalLength = ETCPSocket.PacketPrefixLength + dataToWrite.Length;
            _offsetMemory = 0;
            if (_array.Length < TotalLength)
            {
                TotalLength = 0;
                return false;
            }
            BinaryPrimitives.WriteUInt16LittleEndian(_array.AsSpan(0, ETCPSocket.PacketPrefixLength), (ushort)dataToWrite.Length);
            dataToWrite.CopyTo(_array.AsMemory(ETCPSocket.PacketPrefixLength, dataToWrite.Length));
            return true;
        }

        internal bool SetToSend(ReadOnlySpan<byte> dataToWrite, EAesGcm aes)
        {
            TotalLength = _fullPacketHeaderEncrypt + dataToWrite.Length;
            _offsetMemory = 0;
            if (_array.Length < TotalLength)
            {
                TotalLength = 0;
                return false;
            }

            Span<byte> arraySpan = _array;
            BinaryPrimitives.WriteUInt16LittleEndian(arraySpan.Slice(0, ETCPSocket.PacketPrefixLength), (ushort)(dataToWrite.Length + ETCPSocket.PacketEncryptHeader));

            Span<byte> nonce = arraySpan.Slice(ETCPSocket.PacketPrefixLength, 12);
            Span<byte> tag = arraySpan.Slice(14, 16);
            Span<byte> encryptedData = arraySpan.Slice(_fullPacketHeaderEncrypt, dataToWrite.Length);
            if (!aes.Encrypt(nonce, dataToWrite, encryptedData, tag))
            {
                TotalLength = 0;
                return false;
            }
            return true;
        }

        internal void AddOffset(int offset)
        {
            _offsetMemory += offset;
        }

        internal void Prepare()
        {
            _mrvtsc.Reset();
            SAEA.SetBuffer(_array.AsMemory(_offsetMemory, TotalLength - _offsetMemory));
        }

        public ValueTask<int> WaitForCompletionAsync() => new(this, _mrvtsc.Version);

        public int GetResult(short token) => _mrvtsc.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _mrvtsc.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _mrvtsc.OnCompleted(continuation, state, token, flags);
    }
}
