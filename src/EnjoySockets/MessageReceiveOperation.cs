// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Collections.Concurrent;

namespace EnjoySockets
{
    internal class MessageReceiveOperationPool
    {
        readonly ConcurrentStack<MessageReceiveOperation> _pool = new();

        internal MessageReceiveOperation Rent() => _pool.TryPop(out var operation) ? operation : new MessageReceiveOperation(this);
        internal void Return(MessageReceiveOperation operation) => _pool.Push(operation);
    }

    internal class MessageReceiveOperation : DataReceiveOperation
    {
        internal int WroteBytes { get; private set; }

        MemorySegment? _pipeSegments;
        MessageReceiveOperationPool _pool;

        internal MessageReceiveOperation(MessageReceiveOperationPool pool)
        {
            _pool = pool;
        }

        internal static MessageReceiveOperation? Get(object? userObj, ESocketResource eUser, ServerBufferQuota? buffer, ReadOnlySpan<byte> part, DispatchHandler dHandler)
        {
            if (dHandler.SocketRole == eUser.SocketRole)
            {
                var rentBytes = 0;
                if (buffer != null)
                {
                    var payloadLength = ESocketResource.ReadPayloadLength(part);
                    rentBytes = Math.Max(payloadLength, ETCPSocket.MinBufferSlotSizeBytes);
                    if (!buffer.TryRent(rentBytes))
                        return null;
                }

                var instanceId = ESocketResource.ReadInstance(part);
                if (!eUser.TryGetInstance(dHandler, instanceId, out object? instance))
                    return null;

                var eMsg = eUser.ReceiveMsgPool.Rent();
                eMsg.Initialize(userObj, dHandler, buffer, instance, rentBytes, eUser);
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
        /// 3 - invalid arguments (null Handler/BufferControl, empty data, or deserialization failure)
        /// </returns>
        internal sealed override int TryPushPart(ReadOnlySpan<byte> part, MemorySegmentPool segmentPool, IESerializer serializer)
        {
            if (Handler == null)
                return 3;

            var payloadLength = ESocketResource.ReadPayloadLength(part);
            if (payloadLength < 1)
            {
                bool isComplete = payloadLength == TotalBytes && Handler.ArgAllowNull;
                return isComplete ? 0 : 3;
            }

            var payload = ESocketResource.ReadPayload(part);
            if (payloadLength >= TotalBytes)
            {
                bool success = Handler.Deserialize(payload, ref FinalArg, serializer);
                return success ? 0 : 3;
            }

            if (_pipeSegments == null)
                _pipeSegments = segmentPool.Rent();
            else
            {
                if (_serverBufferQuota != null)
                    if (!_serverBufferQuota.TryRent(payloadLength))
                        return 2;
                _rentBytes += payloadLength;
            }

            _pipeSegments.Append(payload);
            WroteBytes += payloadLength;

            if (WroteBytes >= TotalBytes)
            {
                FinalArg = Handler.Deserialize(_pipeSegments.Read(), serializer);
                ClearPipeSegments();
                return 0;
            }

            return 1;
        }

        internal sealed override object?[]? GetArgs()
        {
            if (Handler == null)
                return null;

            if (Handler.ArgType != null)
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
            _pool.Return(this);
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
