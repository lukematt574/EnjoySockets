// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
namespace EnjoySockets
{
    public class ESocketResourceClient : ESocketResource
    {
        internal byte[] Salt = new byte[32];

        internal EClientConfig ConfigClient { get; }
        internal ClientReliableSendTracker MsgCache { get; }

        internal ESocketResourceClient(EClientConfig config, ERSA ersa) : base(ESocketRole.Client, config, ersa)
        {
            ConfigClient = config.Clone();
            MsgCache = new();
            if (Heartbeat > 0)
                _ = StartHeartbeat();
        }

        private protected sealed override void SetPublicKey(ReadOnlyMemory<byte> publicKey)
        {
            int offset = ERSA.HandshakeHeader.Length;
            var publicKeyMemory = ToSignature.AsMemory(offset, publicKey.Length);
            publicKey.CopyTo(publicKeyMemory);
            PublicKey = publicKeyMemory;
            _offsetServerPublicKey = ERSA.HandshakeHeader.Length + publicKey.Length;
            _publicKeyLength = publicKeyMemory.Length;
        }

        int _publicKeyLength;
        int _offsetServerPublicKey;
        internal ReadOnlyMemory<byte> BuildSignature(ReadOnlyMemory<byte> publicKey, byte[] token)
        {
            if (publicKey.Length != _publicKeyLength)
                return ReadOnlyMemory<byte>.Empty;

            publicKey.CopyTo(ToSignature.AsMemory(_offsetServerPublicKey, _publicKeyLength));
            token.CopyTo(ToSignature, ToSignature.Length - token.Length);
            return ToSignature.AsMemory();
        }

        internal bool SetSalt()
        {
            try
            {
                return SetAesGcmKey(Salt);
            }
            catch { return false; }
        }

        private protected sealed override DispatchHandler? GetReceiveHandler(ReadOnlySpan<byte> dto)
        {
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

            return dHandler;
        }

        private protected sealed override void RunReceiveMsg(DispatchHandler dHandler, ReadOnlySpan<byte> dto, ulong session)
        {
            if (dHandler.MethodAttr.MaxParamSize != 0 && ReadTotalBytes(dto) > dHandler.MethodAttr.MaxParamSize)
                return;

            var msg = MessageReceiveOperation.Get(UserObj, this, null, dto, dHandler);
            if (msg != null)
            {
                lock (_Lock)
                {
                    if (!ReceiveDataSessions.TryAdd(session, msg))
                    {
                        msg.Dispose();
                        return;
                    }
                }
            }
            else return;

            RunReceiveMsg(msg, dto);
        }

        private protected override void ClearReceiveDataSessions()
        {
            DisposeReceiveDataSessions();
        }

        internal override sealed void DisposeReceiveDataFromChannel(DataReceiveOperation? eData)
        {
            base.DisposeReceiveDataFromChannel(eData);
        }

        private protected sealed override void RunReceiveMsg(DataReceiveOperation eData, ReadOnlySpan<byte> dto)
        {
            var result = TryPushReceiveDTO(eData, dto);
            if (result > 1)//over 1 - error
            {
                lock (_Lock)
                {
                    ReceiveDataSessions.Remove(eData.Session, out _);
                }
                eData.Dispose();
            }
        }

        private protected sealed override bool ReceiveSpecial(ReadOnlySpan<byte> dto)
        {
            switch (ReadTarget(dto))
            {
                case 0: break;
                case 1: MsgCache.SetEndMsg(ReadSession(dto), ReadMsgFromPayload(dto)); break;//method executed. The result is stored in msg. Possible error: -1 = method execution failed.
                case 2:
                    var msg = ReadMsgFromPayload(dto);
                    MsgCache.SetEndMsg(ReadSession(dto), msg == 2 ? -2 : -5);
                    break;
                case 3: MsgCache.SetEndMsg(ReadSession(dto), -3); break;//response could not be retrieved (session expired)
                case 4: MsgCache.SetEndMsg(ReadSession(dto), -2); break;//buffer full
                case 5: MsgCache.SetEndMsg(ReadSession(dto), -4); break;//access denied
                case 6: MsgCache.SetEndMsg(ReadSession(dto), -5); break;//invalid payload
                case 7: SetBrokeMsgToSend(dto); break;//resend remaining bytes starting from the position specified in msg (if use 'SendWithResponse' method)
            }
            return true;
        }

        void SetBrokeMsgToSend(ReadOnlySpan<byte> dto)
        {
            var offset = ReadMsgFromPayload(dto);
            var session = ReadSession(dto);
            var sender = MsgCache.SetBrokeMsgToSend(session, offset);
            if (sender != null)
            {
                var obj = ExecutorSend._messagePool.Rent();
                obj.RunPrepare(RunObjMsgSend, sender.Target, sender.Msg, sender.Instance);
                obj.SetToWriteAndSession((long)offset!, sender.Session);
                ExecutorSend.TrySendMsgAndGetSession(obj);
            }
            else
                MsgCache.SetEndMsg(session, -3);
        }

        internal async ValueTask<bool> RebuildSessions()
        {
            var maxSession = GetSession();
            var list = MsgCache.GetNonReceivedSessions();
            bool sendMax = true;
            foreach (var item in list)
            {
                if (!await SendSpecial(item, 1, null))
                {
                    sendMax = false;
                    break;
                }
            }
            if (sendMax)
            {
                return await SendSpecial(maxSession, 2, null);
            }
            else
                return false;
        }

        internal override ulong GetSession()
        {
            return Interlocked.Increment(ref LastSessionToGet);
        }

        internal override sealed void Dispose()
        {
            base.Dispose();
            MsgCache.Clear();
        }

        private protected sealed override void DisposeReceiveDataSessions()
        {
            base.DisposeReceiveDataSessions();
        }
    }
}
