// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace EnjoySockets
{
    public abstract class ESocketResource
    {
        internal Socket? BasicSocket { get; set; }
        internal bool Running { get; private set; }
        private protected object _Lock = new();

        public ETCPSocketType SocketType { get; private set; }
        /// <summary>
        /// Buffer in bytes
        /// </summary>
        internal int MessageBuffer { get; private set; }
        internal ushort MaxEncryptPacket { get; private set; }
        internal const ushort MinEncryptPacket = ETCPSocket.PacketEncryptHeader + ETCPSocket.PacketHeader;
        internal object? UserObj { get; set; }

        internal byte[] TokenToReconnect = new byte[32];
        private protected EArrayBufferPool _eArrayBufferPool = EArrayBufferPool.GetPool(ETCPSocket.MaxPacketSizeConnect);
        private protected ESerializeMsg ESerializeMsgObj;

        private protected EAesGcm AESgcm { get; private set; }

        private protected ulong LastSessionReceive;
        private protected Dictionary<ulong, EReceiveMsg> ReceiveDataSessions = [];

        internal ESendChannel ChannelSend { get; private set; }
        private protected EReceiveChannel ChannelReceiveBasic { get; private set; }
        readonly Dictionary<ushort, EReceiveChannel> _privateChannels = [];

        internal ESendArgs SendArgs { get; private set; }
        internal EReceiveArgs ReceiveArgs { get; private set; }
        byte[] _sendBuffer;

        internal EMemorySegmentPool MemorySegmentPool { get; private set; } = new();
        internal EReceiveMsgPool ReceiveMsgPool { get; private set; } = new();

        private protected ECDiffieHellman ECDH { get; private set; }
        internal ReadOnlyMemory<byte> PublicKey { get; set; }
        internal ERSA Ersa { get; private set; }

        readonly ECDiffieHellman _outPublicKey;
        readonly byte[] AESKey = new byte[32];
        private protected readonly byte[] ToSignature;

        internal ETCPConfig Config { get; private set; }
        internal int Heartbeat { get; private set; }

        private protected Dictionary<long, (object, Dictionary<ulong, ERCell>)> _privateInstances = [];
        private protected Dictionary<Type, object> _localInstances = [];

        internal Action<int>? RunOnPotentialSabotageEvent { get; set; }
        internal Action? RunDisposeEvent { get; set; }

        internal ESocketResource(ETCPSocketType socketType, ETCPConfig config, ERSA rsa)
        {
            AESgcm = new EAesGcm(socketType);
            ESerializeMsgObj = new ESerializeMsg(config, MemorySegmentPool);
            Config = config.Clone();
            MessageBuffer = Config.MessageBuffer * 1024;
            Heartbeat = Config.Heartbeat * 1000;
            _sendBuffer = new byte[Config.MaxPacketSize];
            MaxEncryptPacket = (ushort)(Config.MaxPacketSize + ETCPSocket.PacketEncryptHeader);
            SendArgs = new((ushort)(MaxEncryptPacket + ETCPSocket.PacketPrefixLength));
            ReceiveArgs = new(MaxEncryptPacket);
            SocketType = socketType;
            Ersa = rsa;
            _outPublicKey = ECDiffieHellman.Create(Config.Curve);
            ECDH = ECDiffieHellman.Create(Config.Curve);
            var pk = new byte[158];
            var pkLength = EAesGcm.ExportSpki(ECDH, pk);
            ToSignature = new byte[ERSA.HandshakeHeader.Length + (pkLength * 2) + TokenToReconnect.Length];
            ERSA.HandshakeHeader.CopyTo(ToSignature);
            SetPublicKey(pk.AsMemory(0, pkLength));

            ChannelSend = new(this);
            ChannelReceiveBasic = new();
        }

        private protected virtual void SetPublicKey(ReadOnlyMemory<byte> publicKey) { }

        internal bool AppendSocket(Socket socket)
        {
            lock (_Lock)
            {
                if (!Running)
                {
                    ETCPSocket.Close(BasicSocket);
                    BasicSocket = socket;
                    return true;
                }
            }
            _ = ChannelSend.TrySendHeartbeat(RunHeartbeatSend);
            return false;
        }

        internal bool SetAesGcmKey(ReadOnlySpan<byte> outPublicKey, ReadOnlySpan<byte> salt)
        {
            if (salt.Length != 32)
                return false;
            try
            {
                _outPublicKey.ImportSubjectPublicKeyInfo(outPublicKey, out _);
                ECDH.DeriveKeyMaterial(_outPublicKey.PublicKey).CopyTo(AESKey, 0);
                return SetAesGcmKey(salt);
            }
            catch
            {
                return false;
            }
        }

        private protected bool SetAesGcmKey(ReadOnlySpan<byte> salt)
        {
            salt.CopyTo(TokenToReconnect);
            return AESgcm.SetKey(AESKey, salt);
        }

        /// <summary>
        /// Get encrypt bytes with timeout (Config.ResponseTimeout)
        /// </summary>
        internal ValueTask<ReadOnlyMemory<byte>> ReceiveEncryptWithTimeout()
        {
            if (Running || BasicSocket == null)
                return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);

            return ETCPSocket.ReceiveWithTimeout(ETCPSocket.Receive(BasicSocket, AESgcm, ReceiveArgs), Config.ResponseTimeout);
        }

        /// <summary>
        /// Send plain bytes
        /// </summary>
        internal ValueTask<bool> SendPlainBytes(ReadOnlyMemory<byte> bytes)
        {
            return ETCPSocket.Send(BasicSocket, SendArgs, bytes);
        }

        /// <summary>
        /// Send plain bytes as obj
        /// </summary>
        internal ValueTask<bool> SendPlainBytesObj<T>(T obj)
        {
            if (obj == null) return ValueTask.FromResult(false);
            var buffer = _eArrayBufferPool.Rent();
            try
            {
                var payloadLength = ESerial.Serialize(buffer, obj, obj.GetType());
                if (payloadLength == 0 || payloadLength > ETCPSocket.MaxPacketSizeConnect)
                    return ValueTask.FromResult(false);
                return ETCPSocket.Send(BasicSocket, SendArgs, buffer.WrittenMemory);
            }
            finally
            {
                _eArrayBufferPool.Return(buffer);
            }
        }

        /// <summary>
        /// Send encrypt bytes (aesGcm)
        /// </summary>
        internal ValueTask<bool> SendBytes<T>(T obj)
        {
            if (obj == null) return ValueTask.FromResult(false);
            var buffer = _eArrayBufferPool.Rent();
            try
            {
                var payloadLength = ESerial.Serialize(buffer, obj, obj.GetType());
                if (payloadLength == 0 || payloadLength > ETCPSocket.MaxPacketSizeConnect)
                    return ValueTask.FromResult(false);
                return ETCPSocket.Send(BasicSocket, SendArgs, AESgcm, buffer.WrittenMemory);
            }
            finally
            {
                _eArrayBufferPool.Return(buffer);
            }
        }

        /// <summary>
        /// Serialize object
        /// </summary>
        /// <returns>You must 'Clear' after use</returns>
        internal EMemorySegment? ObjToSegments<T>(T obj)
        {
            if (obj == null) return null;
            return ESerializeMsgObj!.ObjToSegments(obj, typeof(T));
        }

        internal virtual void DisposeReceiveDataFromChannel(EReceiveData? eData)
        {
            if (eData != null)
            {
                lock (_Lock)
                {
                    ReceiveDataSessions.Remove(eData.Session, out _);
                }
                eData.Dispose();
            }
        }

        internal virtual ValueTask<bool> SendSpecial(ulong session, ulong typeMsg, long? msg)
        {
            if (BasicSocket == null)
                return ValueTask.FromResult(false);

            return ChannelSend.TrySendSpecial(RunSpecialSend, session, typeMsg, msg);
        }

        internal bool Run()
        {
            lock (_Lock)
            {
                if (Running || BasicSocket == null)
                    return false;
                Running = true;
            }

            _ = StartReceive();
            return true;
        }

        async Task StartReceive()
        {
            while (Running)
            {
                if (BasicSocket == null)
                    break;

                //prefix
                var readed = ETCPSocket.TryRead(BasicSocket, ReceiveArgs, ETCPSocket.PacketPrefixLength);
                if (readed < 0)
                    break;

                if (readed != ETCPSocket.PacketPrefixLength)
                {
                    if (!await ETCPSocket.ReadContinueAsync(BasicSocket, ReceiveArgs, readed))
                        break;
                }

                var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(ReceiveArgs.GetSaveBytesSpan());
                if (dataLength < MinEncryptPacket || dataLength > MaxEncryptPacket)
                    break;

                //rest
                if ((readed = ETCPSocket.TryRead(BasicSocket, ReceiveArgs, dataLength)) < 0)
                    break;

                if (readed != dataLength)
                {
                    if (!await ETCPSocket.ReadContinueAsync(BasicSocket, ReceiveArgs, readed))
                        break;
                }

                if (!PushReceivePart(ReceiveArgs.GetSaveBytesSpan(AESgcm)))
                    break;
            }

            lock (_Lock)
                Running = false;
            ClearReceiveDataSessions();
            RunDisposeEvent?.Invoke();
        }

        private protected virtual void ClearReceiveDataSessions() { }

        bool PushReceivePart(ReadOnlySpan<byte> dto)
        {
            if (dto.Length < ETCPSocket.PacketHeader)
                return false;

            if (dto[0] == (byte)EDataForm.Special)
                return ReceiveSpecial(dto);

            EReceiveMsg? currentSession;
            var session = ReadSession(dto);
            lock (_Lock)
                ReceiveDataSessions.TryGetValue(session, out currentSession);

            if (currentSession == null)
            {
                if (session > LastSessionReceive)
                    LastSessionReceive = session;
                else
                    return true;

                var cell = GetReceiveCell(dto);
                if (cell != null)
                    RunReceiveMsg(cell, dto, session);
            }
            else
                RunReceiveMsg(currentSession, dto);

            return true;
        }

        internal long TryPushReceiveDTO(EReceiveData eData, ReadOnlySpan<byte> dto)
        {
            var result = eData.TryPushPart(dto, MemorySegmentPool);

            if (result == 1)
                return 1;

            if (result == 0)
            {
                var attr = eData.Cell?.AttrMethod;
                if (attr != null)
                {
                    if (attr.ChannelId == 0)
                    {
                        return PushToChannelReceive(ChannelReceiveBasic, eData);
                    }
                    else
                    {
                        if (_privateChannels.TryGetValue(attr.ChannelId, out EReceiveChannel? channelPr))
                        {
                            return PushToChannelReceive(channelPr, eData);
                        }
                        else if (EReceiveCells.TryGetShareChannel(attr.ChannelId, out EReceiveChannel? channel))
                        {
                            return PushToChannelReceive(channel, eData); ;
                        }
                        else if (EReceiveCells.TryGetPrivateChannel(attr.ChannelId, out EAttrChannel? attrChannel))
                        {
                            if (attrChannel != null)
                            {
                                var endChannel = new EReceiveChannel(true, attrChannel.ChannelTasks);
                                _privateChannels.Add(attr.ChannelId, endChannel);
                                return PushToChannelReceive(endChannel, eData); ;
                            }
                        }
                        else
                        {
                            return PushToChannelReceive(ChannelReceiveBasic, eData); ;
                        }
                    }
                }
            }
            return result;
        }

        int PushToChannelReceive(EReceiveChannel? channel, EReceiveData data)
        {
            lock (_Lock)
            {
                if (channel != null && Running)
                {
                    data.InChannel = true;
                    if (channel.Push(data))
                        return 0;
                }
                return 3;
            }
        }

        private protected virtual ERCell? GetReceiveCell(ReadOnlySpan<byte> dto) { return null; }
        private protected virtual void RunReceiveMsg(ERCell eData, ReadOnlySpan<byte> dto, ulong session) { }
        private protected virtual void RunReceiveMsg(EReceiveData eData, ReadOnlySpan<byte> dto) { }
        private protected virtual bool ReceiveSpecial(ReadOnlySpan<byte> dto) { return true; }

        internal ValueTask<bool> RunObjMsgSend(ESendMsg msg)
        {
            var sendBufferSpan = _sendBuffer.AsSpan();

            sendBufferSpan[0] = (byte)EDataForm.Msg;
            if (msg.Session == 0) msg.Session = GetSession();
            WriteSession(sendBufferSpan, msg.Session);
            WriteTotalBytes(sendBufferSpan, msg.TotalBytes);
            WriteTarget(sendBufferSpan, msg.Target);
            WriteInstance(sendBufferSpan, msg.Instance);
            var payloadLength = msg.FillSpan(sendBufferSpan.Slice(ETCPSocket.PacketHeader));

            SendArgs.SetToSend(sendBufferSpan.Slice(0, ETCPSocket.PacketHeader + payloadLength), AESgcm);
            return ETCPSocket.Write(BasicSocket, SendArgs);
        }

        private protected ValueTask<bool> RunSpecialSend(ulong session, ulong typeMsg, long? msg)
        {
            var sendBufferSpan = _sendBuffer.AsSpan();

            sendBufferSpan[0] = (byte)EDataForm.Special;
            WriteSession(sendBufferSpan, session);
            WriteTarget(sendBufferSpan, typeMsg);
            var payloadLength = WriteMsgToPayload(sendBufferSpan, msg);

            SendArgs.SetToSend(sendBufferSpan.Slice(0, ETCPSocket.PacketHeader + payloadLength), AESgcm);
            return ETCPSocket.Write(BasicSocket, SendArgs);
        }

        internal ValueTask<bool> RunHeartbeatSend()
        {
            var sendBufferSpan = _sendBuffer.AsSpan();

            sendBufferSpan[0] = (byte)EDataForm.Special;
            WriteTarget(sendBufferSpan, 0);

            SendArgs.SetToSend(sendBufferSpan.Slice(0, ETCPSocket.PacketHeader), AESgcm);
            return ETCPSocket.Write(BasicSocket, SendArgs);
        }

        private protected ulong LastSessionToGet = (ulong)DateTime.UtcNow.Ticks;
        /// <summary>
        /// Returns a unique session on socket
        /// </summary>
        internal virtual ulong GetSession()
        {
            return ++LastSessionToGet;
        }

        private protected async Task StartHeartbeat()
        {
            var running = true;
            while (true)
            {
                await Task.Delay(Heartbeat * (running ? 1 : 2));
                running = Running;
                if (Running)
                    await ChannelSend.TrySendHeartbeat(RunHeartbeatSend);
            }
        }

        internal long RegisterPrivateInstance(object obj)
        {
            Type t = obj.GetType();
            if (EReceiveCells.ExistInstanceCell(t, out Dictionary<ulong, ERCell>? val))
            {
                if (val == null || !val.Any(x => x.Value.SocketType == SocketType))
                    return 0;
                long id = EUserServer.GetUniqueId();
                lock (_Lock)
                {
                    _privateInstances.Add(id, (obj, val));
                    return id;
                }
            }
            return 0;
        }

        internal bool RemovePrivateInstance(long id)
        {
            lock (_Lock)
            {
                return _privateInstances.Remove(id);
            }
        }

        internal void ClearPrivateInstance()
        {
            lock (_Lock)
            {
                _privateInstances.Clear();
            }
        }

        internal bool TryGetInstance(ERCell cell, long instance, out object? obj)
        {
            if (cell.MethodInfo.IsStatic)
            {
                obj = null;
                return true;
            }

            if (instance > 0)
            {
                obj = GetPrivateInstance(instance);
                if (obj != null)
                    return true;
                else
                {
                    obj = null;
                    return false;
                }
            }

            obj = GetLocalInstance(cell.ClassType);
            return obj != null;
        }

        internal object? GetPrivateInstance(long id)
        {
            lock (_Lock)
            {
                if (_privateInstances.TryGetValue(id, out var instance))
                    return instance.Item1;
            }
            return null;
        }

        object? GetLocalInstance(Type? type)
        {
            if (type == null)
                return null;

            if (_localInstances.TryGetValue(type, out var instance))
                return instance;
            else
            {
                try
                {
                    instance = Activator.CreateInstance(type);
                    if (instance != null)
                        _localInstances?.Add(type, instance);
                }
                catch { }
            }
            return instance;
        }

        internal virtual void Dispose()
        {
            Socket? socket;
            lock (_Lock)
            {
                Running = false;
                socket = BasicSocket;
                BasicSocket = null;
                LastSessionReceive = 0;
                _privateInstances.Clear();
                _localInstances.Clear();
                DisposeReceiveDataSessions();
            }

            ETCPSocket.ShutdownAndClose(socket);
        }

        private protected virtual void DisposeReceiveDataSessions()
        {
            var listToRemove = new List<EReceiveMsg>();
            lock (_Lock)
            {
                foreach (var item in ReceiveDataSessions.Values)
                {
                    if (!item.InChannel)
                        listToRemove.Add(item);
                }
                ReceiveDataSessions.Clear();
            }

            foreach (var item in listToRemove)
                item.Dispose();
        }

        internal bool IsEmptyReceiveDataSessions()
        {
            lock (_Lock)
            {
                return ReceiveDataSessions.Count < 1;
            }
        }

        #region Write and read section

        internal static void WriteTotalBytes(Span<byte> buffer, int totalBytes) => BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(1, 4), totalBytes);

        internal static void WriteSession(Span<byte> buffer, ulong session) => BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(5, 8), session);

        internal static void WriteInstance(Span<byte> buffer, long instance) => BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(13, 8), instance);

        internal static void WriteTarget(Span<byte> buffer, ulong target) => BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(21, 8), target);

        internal static short WriteMsgToPayload(Span<byte> buffer, long? msg)
        {
            if (msg != null)
            {
                BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(ETCPSocket.PacketHeader, 8), (long)msg);
                return 8;
            }
            return 0;
        }

        internal static int ReadTotalBytes(ReadOnlySpan<byte> buffer) => BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1, 4));

        internal static ulong ReadSession(ReadOnlySpan<byte> buffer) => BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(5, 8));

        internal static long ReadInstance(ReadOnlySpan<byte> buffer) => BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(13, 8));

        internal static ulong ReadTarget(ReadOnlySpan<byte> buffer) => BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(21, 8));

        internal static int ReadPayloadLength(ReadOnlySpan<byte> buffer) => buffer.Length - ETCPSocket.PacketHeader;

        internal static ReadOnlySpan<byte> ReadPayload(ReadOnlySpan<byte> buffer) => buffer.Slice(ETCPSocket.PacketHeader);

        internal static long? ReadMsgFromPayload(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length == 37)
                return BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(ETCPSocket.PacketHeader, 8));

            return null;
        }

        #endregion
    }
}
