// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading.Tasks.Sources;

namespace EnjoySockets
{
    internal sealed class ESendArgs : IValueTaskSource<int>
    {
        public SocketAsyncEventArgs SAEA { get; private set; }
        public int MaxLengthArray { get; private set; } = 0;

        readonly byte[] _array = new byte[ETCPSocket.MaxPacketSizeBytes];
        ManualResetValueTaskSourceCore<int> _mrvtsc;
        int _offsetMemory = 0;

        public ESendArgs()
        {
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

        public bool SetToSend(ReadOnlyMemory<byte> dataToWrite)
        {
            MaxLengthArray = ETCPSocket.PacketPrefixLength + dataToWrite.Length;
            _offsetMemory = 0;
            if (ETCPSocket.MaxPacketSizeBytes - MaxLengthArray < 0)
            {
                MaxLengthArray = 0;
                return false;
            }
            BinaryPrimitives.WriteInt16LittleEndian(_array.AsSpan(0, ETCPSocket.PacketPrefixLength), (short)dataToWrite.Length);
            dataToWrite.CopyTo(_array.AsMemory(ETCPSocket.PacketPrefixLength, dataToWrite.Length));
            return true;
        }

        public bool SetToSend(ReadOnlyMemory<byte> dataToWrite, EAesGcm aes)
        {
            MaxLengthArray = 30 + dataToWrite.Length;
            _offsetMemory = 0;
            if (ETCPSocket.MaxPacketSizeBytes - MaxLengthArray < 0)
            {
                MaxLengthArray = 0;
                return false;
            }
            BinaryPrimitives.WriteInt16LittleEndian(_array.AsSpan(0, ETCPSocket.PacketPrefixLength), (short)(dataToWrite.Length + 28));
            if (!aes.Encrypt(_array.AsSpan(ETCPSocket.PacketPrefixLength, 12), dataToWrite.Span, _array.AsSpan(30, dataToWrite.Length), _array.AsSpan(14, 16)))
            {
                MaxLengthArray = 0;
                return false;
            }
            return true;
        }

        public void AddOffset(int offset)
        {
            _offsetMemory += offset;
        }

        public void Prepare()
        {
            _mrvtsc.Reset();
            SAEA.SetBuffer(_array.AsMemory(_offsetMemory, MaxLengthArray - _offsetMemory));
        }

        public ValueTask<int> WaitForCompletionAsync() => new(this, _mrvtsc.Version);

        public int GetResult(short token) => _mrvtsc.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _mrvtsc.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _mrvtsc.OnCompleted(continuation, state, token, flags);
    }
}
