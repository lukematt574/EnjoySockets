// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Collections.Concurrent;

namespace EnjoySockets
{
    internal class EReceiveMsg : EReceiveData
    {
        internal int WroteBytes { get; private set; }

        EMemorySegment? _pipeSegments;

        private EReceiveMsg()
        {
            Form = EDataForm.Msg;
        }

        static readonly ConcurrentStack<EReceiveMsg> _pool = new();
        static EReceiveMsg Rent() => _pool.TryPop(out var s) ? s : new EReceiveMsg();
        static void Return(EReceiveMsg message)
        {
            _pool.Push(message);
        }

        internal static EReceiveMsg? Get(object? userObj, ESocketResource eUser, EBufferControl? buffer, ReadOnlySpan<byte> part, ERCell rCell)
        {
            if (rCell.SocketType == eUser.SocketType)
            {
                var rentBytes = 0;
                if (buffer != null)
                {
                    var payloadLength = ESocketResource.ReadPayloadLength(part);
                    rentBytes = payloadLength < ETCPSocket.MinBufferSlotSizeBytes ? ETCPSocket.MinBufferSlotSizeBytes : payloadLength;
                    if (!buffer.TryRent(rentBytes))
                        return null;
                }

                var instanceId = ESocketResource.ReadInstance(part);
                if (!eUser.TryGetInstance(rCell, instanceId, out object? instance))
                    return null;

                var eMsg = Rent();
                eMsg.Initialize(userObj, rCell, buffer, instance, rentBytes, eUser);
                eMsg.SetBasicData(part, instanceId);
                eMsg.WroteBytes = 0;
                return eMsg;
            }
            return null;
        }

        /// <summary>
        /// Try append part to message
        /// </summary>
        /// <returns>
        /// 0 - completed
        /// 1 - need more data
        /// 2 - buffer full (unable to rent memory [only server socket])
        /// 3 - invalid arguments (null Cell/BufferControl, empty data, or deserialization failure)
        /// </returns>
        internal sealed override int TryPushPart(ReadOnlySpan<byte> part)
        {
            if (Cell == null)
                return 3;

            var payloadLength = ESocketResource.ReadPayloadLength(part);
            if (payloadLength < 1)
            {
                bool isComplete = payloadLength == TotalBytes && Cell.ArgAllowNull;
                return isComplete ? 0 : 3;
            }

            var payload = ESocketResource.ReadPayload(part);
            if (payloadLength >= TotalBytes)
            {
                bool success = Cell.Deserialize(payload, ref FinalArg);
                return success ? 0 : 3;
            }

            if (_pipeSegments == null)
                _pipeSegments = EMemorySegment.GetFirstSegment();
            else
            {
                if (BufferControl != null)
                    if (!BufferControl.TryRent(payloadLength))
                        return 2;
                _rentBytes += payloadLength;
            }

            _pipeSegments.Append(payload);
            WroteBytes += payloadLength;

            if (WroteBytes >= TotalBytes)
            {
                FinalArg = Cell.Deserialize(_pipeSegments.Read());
                ClearPipeSegments();
                return 0;
            }

            return 1;
        }

        internal sealed override object?[]? GetArgs()
        {
            if (Cell == null)
                return null;

            if (Cell.ArgType != null)
            {
                _tab2Params[0] = User;
                _tab2Params[1] = FinalArg;
                return _tab2Params;
            }
            else
            {
                _tab1Param[0] = User;
                return _tab1Param;
            }
        }

        internal override void Dispose()
        {
            base.Dispose();

            ClearPipeSegments();
            Return(this);
        }

        void ClearPipeSegments()
        {
            if (_pipeSegments != null)
            {
                _pipeSegments.Clear();
                _pipeSegments = null;
            }
        }
    }
}
