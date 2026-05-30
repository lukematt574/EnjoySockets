// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
namespace EnjoySockets
{
    public class ESocketResourceServer : ESocketResource
    {
        private protected ServerBufferQuota BufferToReceiveMsg;

        internal EServerConfig ConfigServer { get; }

        internal int KeepAlive { get; }
        internal bool FirstConnect { get; set; } = true;

        ServerSessionResponseStore ResponseStore { get; set; } = new(ETCPSocket.MaxServerStoredResponsesPerSession);

        internal Func<long, bool>? CheckAccessEvent { get; set; }

        internal ESocketResourceServer(EServerConfig config, ERSA ersa) : base(ESocketRole.Server, config, ersa)
        {
            ConfigServer = config.Clone();
            KeepAlive = ConfigServer.KeepAlive * 1000;
            BufferToReceiveMsg = new(MessageBuffer);
            _ = StartHeartbeat();
        }

        private protected sealed override void SetPublicKey(ReadOnlyMemory<byte> publicKey)
        {
            int offset = ERSA.HandshakeHeader.Length + publicKey.Length;
            var publicKeyMemory = ToSignature.AsMemory(offset, publicKey.Length);
            publicKey.CopyTo(publicKeyMemory);
            PublicKey = publicKeyMemory;
            _offsetClientPublicKey = ERSA.HandshakeHeader.Length;
            _publicKeyLength = publicKeyMemory.Length;
        }

        int _publicKeyLength;
        int _offsetClientPublicKey;
        byte[] _bufferSignature = new byte[512];
        internal ValueTask<ReadOnlyMemory<byte>> BuildSignature(ReadOnlySpan<byte> key)
        {
            if (key.Length != _publicKeyLength)
                return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);

            key.CopyTo(ToSignature.AsSpan(_offsetClientPublicKey, _publicKeyLength));
            TokenToReconnect.CopyTo(ToSignature, ToSignature.Length - TokenToReconnect.Length);

            var signTask = Ersa.SignDataRsa(ToSignature.AsMemory(), _bufferSignature);
            if (signTask.IsCompletedSuccessfully)
            {
                int count = signTask.Result;
                return count < 1
                    ? ValueTask.FromResult(ReadOnlyMemory<byte>.Empty)
                    : ValueTask.FromResult(new ReadOnlyMemory<byte>(_bufferSignature, 0, count));
            }

            return AwaitSignatureBytes(signTask);
        }

        async ValueTask<ReadOnlyMemory<byte>> AwaitSignatureBytes(Task<int> signTask)
        {
            int count;
            try
            {
                count = await signTask.ConfigureAwait(false);
            }
            catch
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            return count < 1 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(_bufferSignature, 0, count);
        }

        internal bool CheckReconnectToken(ReadOnlySpan<byte> token)
        {
            return token.SequenceEqual(TokenToReconnect);
        }

        private protected sealed override DispatchHandler? GetReceiveHandler(ReadOnlySpan<byte> dto)
        {
            var session = ReadSession(dto);
            if (ReadTotalBytes(dto) > MessageBuffer)
            {
                SendSpecialWithStore(session, 4, null);
                return null;
            }

            var instance = ReadInstance(dto);
            var target = ReadTarget(dto);
            DispatchHandler? dHandler = null;
            if (instance > 0)
            {
                lock (_Lock)
                {
                    if (_privateInstances.TryGetValue(instance, out (object, Dictionary<ulong, DispatchHandler>) val))
                        dHandler = DispatcherRegistry.GetHandlerToInstanceId(val.Item1, target);
                }
            }
            else
                dHandler = DispatcherRegistry.GetHandlerToBasic(target);

            if (dHandler == null)
            {
                SendSpecialWithStore(session, 6, null);
                return null;
            }

            return dHandler;
        }

        /// <summary>
        /// Removes and disposes all receive data sessions 
        /// with a session ID less than or equal to the specified value.
        /// </summary>
        /// <param name="session">
        /// Maximum session ID (inclusive). All sessions with ID less or equal this value will be removed.
        /// </param>
        private protected void ClearReceiveDataSessions(ulong session)
        {
            List<MessageReceiveOperation> sessionsToRemove;
            lock (_Lock)
            {
                sessionsToRemove = ReceiveDataSessions
                    .Where(x => x.Value.Session <= session && !x.Value.InChannel)
                    .Select(x => x.Value)
                    .ToList();

                foreach (var item in sessionsToRemove)
                    ReceiveDataSessions.Remove(item.Session);
            }
            foreach (var item in sessionsToRemove)
                item.Dispose();
        }

        private protected override void ClearReceiveDataSessions() { }

        private protected sealed override void RunReceiveMsg(DispatchHandler dHandler, ReadOnlySpan<byte> dto, ulong session)
        {
            if (dHandler.MethodAttr.MaxParamSize != 0 && ReadTotalBytes(dto) > dHandler.MethodAttr.MaxParamSize)
            {
                SendSpecialWithStore(session, 4, null);
                return;
            }

            var access = dHandler.MethodAttr.Access;
            if (access != 0)
            {
                if (!(CheckAccessEvent?.Invoke(access) ?? false))
                {
                    SendSpecialWithStore(session, 5, null);
                    return;
                }
            }

            var msg = MessageReceiveOperation.Get(UserObj, this, BufferToReceiveMsg, dto, dHandler);
            if (msg != null)
            {
                bool added = false;
                lock (_Lock)
                {
                    if (ReceiveDataSessions.TryAdd(session, msg))
                        added = true;
                }
                if (!added)
                {
                    SendSpecialWithStore(msg.Session, 6, null);
                    msg.Dispose();
                }
                else
                    RunReceiveMsg(msg, dto);
            }
            else
            {
                SendSpecialWithStore(session, 6, null);
            }
        }

        private protected sealed override void RunReceiveMsg(DataReceiveOperation eData, ReadOnlySpan<byte> dto)
        {
            var result = TryPushReceiveDTO(eData, dto);
            if (result > 1)//over 1 - error
            {
                eData.Response = result;
                RunDisposeReceiveData(eData, 2);
            }
        }

        /// <summary>
        /// Handles internal control messages received from the peer.
        /// </summary>
        /// <remarks>
        /// Target values:
        /// 0 = heartbeat,
        /// 1 = request session status,
        /// 2 = clear related session data.
        /// </remarks>
        private protected sealed override bool ReceiveSpecial(ReadOnlySpan<byte> dto)
        {
            switch (ReadTarget(dto))
            {
                case 0: break;
                case 1: ResponseStatusSession(ReadSession(dto)); break;
                case 2: ClearReceiveDataSessions(ReadSession(dto)); break;
            }
            return true;
        }

        void ResponseStatusSession(ulong session)
        {
            ulong typeMsg = 0;
            long? msg = null;
            lock (_Lock)
            {
                if (ResponseStore.TryGet(session, out var result))
                {
                    typeMsg = result.Item1;
                    msg = result.Item2;
                }
                else
                {
                    if (ReceiveDataSessions.TryGetValue(session, out MessageReceiveOperation? rMsg))
                    {
                        if (!rMsg.InChannel)
                        {
                            typeMsg = 7;
                            msg = rMsg.WroteBytes;
                        }
                    }
                    else
                    {
                        if (!FirstConnect)
                        {
                            typeMsg = 7;
                            msg = 0;
                        }
                        else
                            typeMsg = 3;
                    }
                }
            }
            SendSpecial(session, typeMsg, msg);
        }

        internal override sealed void DisposeReceiveDataFromChannel(DataReceiveOperation? eData)
        {
            if (eData != null)
                RunDisposeReceiveData(eData, 1);
        }

        void RunDisposeReceiveData(DataReceiveOperation eData, ulong typeMsg)
        {
            lock (_Lock)
            {
                ReceiveDataSessions.Remove(eData.Session, out _);
                ResponseStore.Store(eData.Session, typeMsg, eData.Response);
            }
            SendSpecial(eData.Session, typeMsg, eData.Response);
            if (eData.CorruptedArg)
                RunOnPotentialSabotageEvent?.Invoke(1);
            eData.Dispose();
        }

        internal ValueTask<bool> SendSpecialWithStore(ulong session, ulong typeMsg, long? msg)
        {
            lock (_Lock)
                ResponseStore.Store(session, typeMsg, msg);
            return SendSpecial(session, typeMsg, msg);
        }

        int _maxSendSpecialCount;
        const int _limitSendSpecial = ETCPSocket.MaxServerStoredResponsesPerSession * 2;
        internal override sealed ValueTask<bool> SendSpecial(ulong session, ulong typeMsg, long? msg)
        {
            if (BasicSocket == null)
                return ValueTask.FromResult(false);

            if (Interlocked.Increment(ref _maxSendSpecialCount) > _limitSendSpecial)
            {
                RunOnPotentialSabotageEvent?.Invoke(2);
                RunDisposeEvent?.Invoke();
                Interlocked.Decrement(ref _maxSendSpecialCount);
                return ValueTask.FromResult(false);
            }

            var vt = ExecutorSend.TrySendSpecial(RunSpecialSend, session, typeMsg, msg);
            if (vt.IsCompletedSuccessfully)
            {
                Interlocked.Decrement(ref _maxSendSpecialCount);
                return vt;
            }
            return AwaitedSendSpecial(vt);
        }

        async ValueTask<bool> AwaitedSendSpecial(ValueTask<bool> pending)
        {
            try
            {
                return await pending;
            }
            finally
            {
                Interlocked.Decrement(ref _maxSendSpecialCount);
            }
        }

        internal override sealed void Dispose()
        {
            base.Dispose();
            BufferToReceiveMsg.Reset();
            FirstConnect = true;
        }

        private protected sealed override void DisposeReceiveDataSessions()
        {
            var listToRemove = new List<MessageReceiveOperation>();
            lock (_Lock)
            {
                foreach (var item in ReceiveDataSessions.Values)
                {
                    if (!item.InChannel)
                        listToRemove.Add(item);
                }

                foreach (var item in listToRemove)
                    ReceiveDataSessions.Remove(item.Session);
            }

            foreach (var item in listToRemove)
                item.Dispose();
        }

        internal void ClearResponseStore()
        {
            _maxSendSpecialCount = 0;
            lock (_Lock)
                ResponseStore.Clear();
        }
    }
}
