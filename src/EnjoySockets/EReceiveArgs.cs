// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Net.Sockets;
using System.Threading.Tasks.Sources;

namespace EnjoySockets
{
    internal sealed class EReceiveArgs : IValueTaskSource<int>
    {
        public SocketAsyncEventArgs SAEA { get; private set; }
        public int MaxLengthArray { get; private set; } = 0;

        readonly byte[] _array = new byte[ETCPSocket.MaxPacketSizeBytes];
        readonly byte[] _arrayDecrypt = new byte[ETCPSocket.MaxPacketSizeBytes];
        ManualResetValueTaskSourceCore<int> _mrvtsc;
        int _offsetMemory = 0;
        bool _softClose = false;

        public EReceiveArgs()
        {
            SAEA = new SocketAsyncEventArgs();
            SAEA.Completed += ReceiveCompleted;
            _mrvtsc = new ManualResetValueTaskSourceCore<int> { RunContinuationsAsynchronously = true };
        }

        /// <summary>
        /// Returns <see langword="true"/> if the connection was intentionally closed by the client
        /// and no reconnection attempts will be made.
        /// </summary>
        /// <param name="esr">The socket resource.</param>
        /// <returns>
        /// <see langword="true"/> if the client intentionally closed the connection and
        /// the cleanup procedure can be executed immediately; otherwise, <see langword="false"/>.
        /// </returns>
        internal static bool IsSoftClose(ESocketResource? esr)
        {
            return esr?.ReceiveArgs._softClose ?? true;
        }

        void ReceiveCompleted(object? s, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                _softClose = e.BytesTransferred == 0;
                _mrvtsc.SetResult(e.BytesTransferred);
            }
            else
            {
                _softClose = e.SocketError == SocketError.ConnectionReset;
                _mrvtsc.SetResult(-1);
            }
        }

        /// <summary>
        /// Set bytes count to read
        /// </summary>
        public void StartPrepare(int bytesToRead)
        {
            MaxLengthArray = bytesToRead;
            _offsetMemory = 0;
        }

        public void AddOffset(int offset)
        {
            _offsetMemory += offset;
        }

        public ReadOnlyMemory<byte> GetSaveBytes()
        {
            return _array.AsMemory(0, MaxLengthArray);
        }

        public ReadOnlyMemory<byte> GetSaveBytes(EAesGcm aes)
        {
            var lengthData = MaxLengthArray - 28;
            if (lengthData < 1)
                return Memory<byte>.Empty;
            var buffer = _arrayDecrypt.AsMemory(0, lengthData);
            if (aes.Decrypt(_array.AsSpan(0, 12), _array.AsSpan(28, lengthData), buffer.Span, _array.AsSpan(12, 16)))
                return buffer;
            else
                return Memory<byte>.Empty;
        }

        public void PrepareBuffer()
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
