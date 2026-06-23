// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using static EnjoySockets.ClientSendFlowController;

namespace EnjoySockets
{
    /// <summary>
    /// Represents the client-side session over a reusable socket resource.
    /// </summary>
    /// <remarks>
    /// This class is designed to be reused for the entire lifetime of the application.
    /// <para/>
    /// It is not disposed after a single connection lifecycle and does not follow a per-connection disposal model.
    /// </remarks>
    public class EClient : ESession<ESocketResourceClient>
    {
        /// <summary>
        /// Represents the current status of the client connection.
        /// </summary>
        /// <remarks>
        /// Regardless of the current status, the client object is reusable.
        /// <para/>
        /// Do not create new client instances unnecessarily; reuse existing ones whenever possible.
        /// </remarks>
        public EClientStatus Status { get; private set; } = EClientStatus.Disconnected;
        public EAddress? Address { get; set; }

        int _reconnectDelayMs = 0;
        readonly ClientSendFlowController _sendFlowController;

        /// <summary>
        /// Initializes a new instance of the <see cref="EClient"/> class
        /// using the specified RSA provider and default client configuration.
        /// </summary>
        /// <param name="ersa">RSA provider used for encryption and signing.</param>
        /// <remarks>
        /// The client instance is reusable regardless of its connection status.
        /// Do not create new instances unnecessarily - the same object can be reconnected and reused multiple times.
        /// </remarks>
        public EClient(ERSA ersa) : this(ersa, new()) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="EClient"/> class using the specified RSA provider and client configuration.
        /// </summary>
        /// <param name="ersa">RSA provider used for encryption and signing.</param>
        /// <param name="config">Client configuration settings.</param>
        /// <remarks>
        /// The client instance is reusable regardless of its connection status. 
        /// It is recommended to reuse existing instances instead of creating new ones for each connection attempt.
        /// </remarks>
        public EClient(ERSA ersa, EConfigClient config) : base(new ESocketResourceClient(config, ersa))
        {
            DispatcherRegistry.Initialize();
            if (SocketResource != null)
            {
                SocketResource.UserObj = this;
                SocketResource.RunDisposeEvent = Dispose;
            }
            _sendFlowController = new(SocketResource?.MessageBuffer ?? config.MessageBuffer * 1024);
            UserId = SetGuidUserId();
        }

        /// <summary>
        /// Generates a unique user ID used for client reconnection.
        /// </summary>
        /// <returns>A new <see cref="Guid"/> for the session.</returns>
        protected virtual Guid SetGuidUserId()
        {
            return Guid.NewGuid();
        }

        /// <summary>
        /// Sends a message without payload to the specified target and awaits a guaranteed response.
        /// </summary>
        /// <inheritdoc cref="SendTransact{T}(long, string, T)"/>
        public ValueTask<ETransactResult> SendTransact(string target)
        {
            return SendTransact(0, target);
        }

        /// <summary>
        /// Sends a message with payload to the specified target and awaits a guaranteed response.
        /// </summary>
        /// <inheritdoc cref="SendTransact{T}(long, string, T)"/>
        public ValueTask<ETransactResult> SendTransact<T>(string target, T obj)
        {
            return SendTransact(0, target, obj);
        }

        /// <summary>
        /// Sends a message without payload to the specified target and instance and awaits a guaranteed response.
        /// </summary>
        /// <inheritdoc cref="SendTransact{T}(long, string, T)"/>
        public ValueTask<ETransactResult> SendTransact(long instance, string target)
        {
            return SendTransact<object>(instance, target, null);
        }

        /// <summary>
        /// Sends a message with a payload to the specified target and instance, and awaits a guaranteed response.
        /// </summary>
        /// <remarks>
        /// This method guarantees that a response will be received even if the connection was temporarily lost and the client is in reconnection mode.
        /// <para>
        /// The returned <c><see cref="ETransactResult.Code"/></c> value encodes the outcome:
        /// <list type="bullet">
        /// <item><c><see cref="ETransactResult.Success"/> (0)</c> - the operation completed successfully.</item>
        /// <item><c><see cref="ETransactResult.ExecutionFailed"/> (-1)</c> - method execution failed.</item>
        /// <item><c><see cref="ETransactResult.BufferFull"/> (-2)</c> - buffer full (unable to rent memory).</item>
        /// <item><c><see cref="ETransactResult.SessionExpired"/> (-3)</c> - session expired (response could not be retrieved).</item>
        /// <item><c><see cref="ETransactResult.AccessDenied"/> (-4)</c> - access denied.</item>
        /// <item><c><see cref="ETransactResult.InvalidPayload"/> (-5)</c> - invalid payload or invalid arguments (e.g., null parameters, empty data, or deserialization failure).</item>
        /// <item>
        /// <c><see cref="ETransactResult.IsCustomCode"/> (&gt;0 and &lt;=1_000_000)</c> - user-defined or application-specific codes or internal status.
        /// </item>
        /// <item><c><see cref="ETransactResult.IsEntityId"/> (&gt;1_000_000)</c> - result represents a server-generated entity identifier (e.g. instance ID)</item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <typeparam name="T">The type of the payload being sent.</typeparam>
        /// <param name="instance">The target instance ID.</param>
        /// <param name="target">The destination to which the message is sent.</param>
        /// <param name="obj">The payload object to send. May be <see langword="null"/>.</param>
        /// <returns>
        /// <see cref="ETransactResult"/>
        /// </returns>
        public async ValueTask<ETransactResult> SendTransact<T>(long instance, string target, T? obj)
        {
            if (SocketResource?.BasicSocket == null)
                return ETransactResult.ExecutionFailed;

            var t = DispatcherRegistry.GetUlongToSend(target);
            if (t < 1) return ETransactResult.ExecutionFailed;

            var segments = SocketResource.ObjToSegments(obj);

            if (segments == null && obj is object)
                return ETransactResult.ExecutionFailed;

            if (segments != null && segments.WrittenBytes > SocketResource.MessageBuffer)
            {
                segments?.Clear();
                return ETransactResult.ExecutionFailed;
            }

            var length = segments?.WrittenBytes ?? ETCPSocket.MinBufferSlotSizeBytes;
            length = Math.Max(length, ETCPSocket.MinBufferSlotSizeBytes);
            ClientSendPermit? csp;
            if ((csp = _sendFlowController.TryWait(length)) == null)
                csp = await _sendFlowController.Wait(length);

            if (csp.Cancel)
            {
                segments?.Clear();
                _sendFlowController.Release(csp);
                return ETransactResult.ExecutionFailed;
            }

            ulong session = SocketResource.GetSession();
            var context = SocketResource.MsgTracker.Get(session, t, instance, segments);
            if (context == null)
            {
                segments?.Clear();
                _sendFlowController.Release(csp);
                return ETransactResult.ExecutionFailed;
            }

            var objMsg = SocketResource.ExecutorSend.MessagePool.Rent();
            objMsg.RunPrepare(SocketResource.RunObjMsgSend, t, segments, instance, session);

            var vt = SocketResource.ExecutorSend.TrySendMsgAndGetSession(objMsg);
            if (!vt.IsCompletedSuccessfully)
                await vt;

            var resultFromServ = await context.AsValueTask();
            SocketResource.MsgTracker.Remove(session);
            _sendFlowController.Release(csp);
            return resultFromServ;
        }

        /// <summary>
        /// Sends a message without payload to the specified target and awaits a response.
        /// </summary>
        /// <inheritdoc cref="SendAndFetch{TResponse, T}(long, string, T)"/>
        public ValueTask<TResponse?> SendAndFetch<TResponse>(string target)
        {
            return SendAndFetch<TResponse>(0, target);
        }

        /// <summary>
        /// Sends a message with payload to the specified target and awaits a response.
        /// </summary>
        /// <inheritdoc cref="SendAndFetch{TResponse, T}(long, string, T)"/>
        public ValueTask<TResponse?> SendAndFetch<TResponse, T>(string target, T obj)
        {
            return SendAndFetch<TResponse, T>(0, target, obj);
        }

        /// <summary>
        /// Sends a message without payload to the specified target and instance, and awaits a response.
        /// </summary>
        /// <inheritdoc cref="SendAndFetch{TResponse, T}(long, string, T)"/>
        public ValueTask<TResponse?> SendAndFetch<TResponse>(long instance, string target)
        {
            return SendAndFetch<TResponse, object>(instance, target, null);
        }

        /// <summary>
        /// Sends a message with a payload to the specified target and instance, and awaits a response.
        /// </summary>
        /// <remarks>
        /// This method guarantees that the call will not complete immediately on transient transport failures.
        /// If the connection is lost, the operation will be suspended and resumed once the connection is restored,
        /// or completed when a manual disconnect or cancellation occurs.
        /// <para/>
        /// The method returns a response if the operation succeeds. If the transport layer fails 
        /// (e.g. connection loss, reconnection state, or serialization/dispatch error), 
        /// the method returns <c>null</c> or <c>default(<typeparamref name="TResponse"/>)</c> depending on whether <typeparamref name="TResponse"/> is a reference or value type.
        /// <para/>
        /// Do not use non-nullable primitive types (e.g. <c>int</c>, <c>bool</c>) as <typeparamref name="TResponse"/>.
        /// In failure scenarios, these types may return default values (e.g. <c>0</c>, <c>false</c>), which are indistinguishable from valid responses.
        /// Prefer nullable types (e.g. <c>int?</c>, <c>bool?</c>) to explicitly detect transport failures.
        /// </remarks>
        /// <typeparam name="TResponse">The type of response sent from server.</typeparam>
        /// <typeparam name="T">The type of the payload being sent.</typeparam>
        /// <param name="instance">The target instance ID.</param>
        /// <param name="target">The destination to which the message is sent.</param>
        /// <param name="obj">The payload object to send. May be <see langword="null"/>.</param>
        public async ValueTask<TResponse?> SendAndFetch<TResponse, T>(long instance, string target, T? obj)
        {
            if (SocketResource?.BasicSocket == null)
                return default;

            var t = DispatcherRegistry.GetUlongToSend(target);
            if (t < 1) return default;

            var segments = SocketResource.ObjToSegments(obj);
            if (segments == null && obj is object)
                return default;

            if (segments != null && segments.WrittenBytes > SocketResource.MessageBuffer)
            {
                segments?.Clear();
                return default;
            }

            var length = segments?.WrittenBytes ?? ETCPSocket.MinBufferSlotSizeBytes;
            length = Math.Max(length, ETCPSocket.MinBufferSlotSizeBytes);
            ClientSendPermit? csp;
            if ((csp = _sendFlowController.TryWait(length)) == null)
                csp = await _sendFlowController.Wait(length);

            if (csp.Cancel)
            {
                segments?.Clear();
                _sendFlowController.Release(csp);
                return default;
            }

            ulong session = SocketResource.GetSession();
            var context = SocketResource.MsgTracker.Get(session, t, instance, segments);
            if (context == null)
            {
                segments?.Clear();
                _sendFlowController.Release(csp);
                return default;
            }

            var objMsg = SocketResource.ExecutorSend.MessagePool.Rent();
            objMsg.RunPrepare(SocketResource.RunObjMsgSend, t, segments, instance, session);

            var vt = SocketResource.ExecutorSend.TrySendMsgAndGetSession(objMsg);
            if (!vt.IsCompletedSuccessfully)
                await vt;

            var responseLong = await context.AsValueTask();

            TResponse? response;
            if (typeof(TResponse) == typeof(long))
                response = (TResponse)(object)responseLong;
            else
                response = context.GetResponseMsg<TResponse>(SocketResource.ConfigClient.ESerial);

            SocketResource.MsgTracker.Remove(session);
            _sendFlowController.Release(csp);

            return response;
        }

        public async override sealed ValueTask<bool> Send<T>(long instance, string target, T? obj) where T : default
        {
            if (SocketResource?.BasicSocket == null || Status != EClientStatus.Connected)
                return false;

            var t = DispatcherRegistry.GetUlongToSend(target);
            if (t < 1)
                return false;

            var segments = SocketResource.ObjToSegments(obj);
            if (segments == null && obj is object)
                return false;

            if (segments != null && segments.WrittenBytes > SocketResource.MessageBuffer)
            {
                segments?.Clear();
                return false;
            }

            var length = segments?.WrittenBytes ?? ETCPSocket.MinBufferSlotSizeBytes;
            length = Math.Max(length, ETCPSocket.MinBufferSlotSizeBytes);
            ClientSendPermit? csp;
            if ((csp = _sendFlowController.TryWait(length)) == null)
                csp = await _sendFlowController.Wait(length);

            if (csp.Cancel)
            {
                segments?.Clear();
                _sendFlowController.Release(csp);
                return false;
            }

            var totalBytes = segments?.WrittenBytes ?? 0;
            ulong session = SocketResource.GetSession();
            var context = SocketResource.MsgTracker.Get(session, totalBytes);
            if (context == null)
            {
                segments?.Clear();
                _sendFlowController.Release(csp);
                return false;
            }

            var objMsg = SocketResource.ExecutorSend.MessagePool.Rent();
            objMsg.RunPrepare(SocketResource.RunObjMsgSend, t, segments, instance, session);

            var success = false;
            var vt = SocketResource.ExecutorSend.TrySendMsgAndGetSession(objMsg);
            if (vt.IsCompletedSuccessfully)
                success = vt.Result > 0;
            else
                success = await vt > 0;

            segments?.Clear();

            if (!success)
            {
                SocketResource.MsgTracker.Remove(session);
                _sendFlowController.Release(csp);
                return false;
            }

            _ = SendWaitOnResultFromServ(SocketResource, session, csp, context);
            return true;
        }

        async ValueTask SendWaitOnResultFromServ(ESocketResourceClient sr, ulong session, ClientSendPermit ecsw, ClientReliableSendContext context)
        {
            await context.AsValueTask();
            sr.MsgTracker.Remove(session);
            _sendFlowController.Release(ecsw);
        }

        #region Connection

        /// <summary>
        /// Gets the user-defined authorization object to be sent during the connection handshake.
        /// <para/>
        /// The returned object is always of type <see cref="object"/> in the base class,
        /// but in the derived <c><see cref="EServerSession"/></c> class the user knows its actual type
        /// and should pass it directly to the corresponding method:
        /// <code>
        /// protected Task&lt;byte&gt; Authorization(actualType obj)
        /// </code>
        /// where <c>actualType</c> matches the type returned by <see cref="GetAuthorization"/>.
        /// </summary>
        /// <remarks>
        /// The serialized size of the returned object must not exceed 1250 bytes.
        /// The base implementation returns <c>null</c>, meaning no authorization.
        /// </remarks>
        protected internal virtual object? GetAuthorization() { return null; }

        /// <summary>
        /// Called after a reconnection attempt.
        /// </summary>
        /// <param name="attemptCount">The current reconnection attempt number.</param>
        /// <param name="attemptResult">
        /// The result code of the attempt. A value of <c>0</c> typically indicates success; any other value represents an error.
        /// </param>
        protected virtual void OnReconnectAttempt(int attemptCount, EConnectResult attemptResult) { }

        const int _maxReconnectDelayMs = 60000;
        const int _minReconnectDelayMs = 1000;
        /// <summary>
        /// Asynchronously connects to the server endpoint and performs handshake.
        /// </summary>
        /// <remarks>
        /// The reconnect loop can be interrupted by calling <see cref="Disconnect"/>.
        /// This method builds on <see cref="Connect(EAddress?)"/> but adds automatic reconnection logic.
        /// </remarks>
        /// <param name="eAddress">Optional server endpoint; uses <c>Address</c> if null.</param>
        /// <param name="reconnectDelayMs">
        /// Delay in milliseconds between reconnection attempts (default 5000ms).
        /// Must be between 1000 and 60000 milliseconds; if the value is outside this range, the default of 5000ms will be used.
        /// </param>
        /// <returns>
        /// A <see cref="byte"/> status code representing the result of the connection attempt:
        /// <list type="table">
        /// <item><c><see cref="EConnectResult.Success"/> (0)</c> - connection succeeded.</item>
        /// <item><c><see cref="EConnectResult.InvalidEndpoint"/> (1)</c> - invalid endpoint (IP or address).</item>
        /// <item><c><see cref="EConnectResult.Timeout"/> (2)</c> - connection attempt timed out.</item>
        /// <item><c><see cref="EConnectResult.ServerFull"/> (3)</c> - server is full.</item>
        /// <item><c><see cref="EConnectResult.ServerVerificationFailed"/> (4)</c> - server verification failed.</item>
        /// <item><c><see cref="EConnectResult.HandshakeFailed"/> (5)</c> - handshake (encryption key exchange) failed.</item>
        /// <item><c><see cref="EConnectResult.ServerUnavailable"/> (6)</c> - server unavailable or connection failed.</item>
        /// <item><c><see cref="EConnectResult.InvalidAuthData"/> (7)</c> - invalid authentication data.</item>
        /// <item><c><see cref="EConnectResult.AlreadyConnectedOrConnecting"/> (8)</c> - connection already active or in progress.</item>
        /// <item><c><see cref="EConnectResult.ReconnectCancelled"/> (9)</c> - reconnect cancelled via <see cref="Disconnect"/>.</item>
        /// <item><c><see cref="EConnectResult.IsCustomError"/> (&gt;9)</c> - is a custom user-defined error code</item>
        /// </list>
        /// </returns>
        public Task<EConnectResult> ConnectWithAutoReconnect(EAddress? eAddress, int reconnectDelayMs = 5000)
        {
            _reconnectDelayMs = reconnectDelayMs < _minReconnectDelayMs || reconnectDelayMs > _maxReconnectDelayMs ? 5000 : reconnectDelayMs;
            return Connect(eAddress);
        }

        async Task<bool> RunConnectWithReconnect(EAddress? address, int reconnectDelayMs)
        {
            int attempt = 1;

            if (reconnectDelayMs < _minReconnectDelayMs || reconnectDelayMs > _maxReconnectDelayMs || SocketResource == null)
            {
                OnReconnectAttempt(attempt, 6);
                return false;
            }

            if (address == null || address.EndPoint == null)
            {
                OnReconnectAttempt(attempt, 1);
                return false;
            }

            Address = address;
            _reconnectDelayMs = reconnectDelayMs;

            while (true)
            {
                var time = _reconnectDelayMs;
                if (time < 1)
                {
                    OnReconnectAttempt(attempt, 9);
                    return false;
                }

                var result = await Connect(Address);
                if (result == 0 || SocketResource.Running)
                {
                    OnReconnectAttempt(attempt, result);
                    return true;
                }
                else
                {
                    OnReconnectAttempt(attempt, result);
                    await Task.Delay(time);
                    attempt++;
                }
            }
        }

        readonly byte[] _bufferConnectDTO = new byte[512];
        readonly byte[] _bufferEncryptConnectDTO = new byte[512];
        bool _connecting = false;
        bool _firstConnect = true;
        /// <summary>
        /// Asynchronously connects to the server endpoint and performs handshake.
        /// </summary>
        /// <param name="eAddress">Optional server endpoint; uses <c>Address</c> if null.</param>
        /// <returns>
        /// A <see cref="byte"/> status code representing the result of the connection attempt:
        /// <list type="table">
        /// <item><c><see cref="EConnectResult.Success"/> (0)</c> - connection succeeded.</item>
        /// <item><c><see cref="EConnectResult.InvalidEndpoint"/> (1)</c> - invalid endpoint (IP or address).</item>
        /// <item><c><see cref="EConnectResult.Timeout"/> (2)</c> - connection attempt timed out.</item>
        /// <item><c><see cref="EConnectResult.ServerFull"/> (3)</c> - server is full.</item>
        /// <item><c><see cref="EConnectResult.ServerVerificationFailed"/> (4)</c> - server verification failed.</item>
        /// <item><c><see cref="EConnectResult.HandshakeFailed"/> (5)</c> - handshake (encryption key exchange) failed.</item>
        /// <item><c><see cref="EConnectResult.ServerUnavailable"/> (6)</c> - server unavailable or connection failed.</item>
        /// <item><c><see cref="EConnectResult.InvalidAuthData"/> (7)</c> - invalid authentication data.</item>
        /// <item><c><see cref="EConnectResult.AlreadyConnectedOrConnecting"/> (8)</c> - connection already active or in progress.</item>
        /// <item><c><see cref="EConnectResult.IsCustomError"/> (&gt;9)</c> - is a custom user-defined error code</item>
        /// </list>
        /// </returns>
        public async Task<EConnectResult> Connect(EAddress? eAddress)
        {
            var addr = eAddress ?? Address;
            if (addr == null || addr.EndPoint == null)
                return EConnectResult.InvalidEndpoint;

            Address = addr;
            try
            {
                if (SocketResource == null)
                    return EConnectResult.ServerUnavailable;

                lock (_lock)
                {
                    if (_connecting)
                        return EConnectResult.AlreadyConnectedOrConnecting;

                    _connecting = true;
                }

                var nSocket = new Socket(addr.EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                if (!SocketResource.AppendSocket(nSocket))
                {
                    ETCPSocket.Close(nSocket);
                    return EConnectResult.AlreadyConnectedOrConnecting;
                }

                var connectResult = await ConnectWithTimeout(SocketResource.BasicSocket!, addr.EndPoint, TimeSpan.FromSeconds(SocketResource.ConfigClient.ConnectTimeout));
                if (connectResult != 0)
                    return Close(connectResult);

                var connectDTO = BuildConnectDTO();
                int lengthEncryptConnectDTO = await SocketResource.Ersa.Encrypt(connectDTO, _bufferEncryptConnectDTO);
                if (await SocketResource.SendAsPlainBytes(_bufferEncryptConnectDTO.AsMemory(0, lengthEncryptConnectDTO)))
                {
                    var responseDTO = await ETCPSocket.ReceiveWithTimeout(ETCPSocket.Receive(SocketResource.BasicSocket!, SocketResource.ReceiveArgs), SocketResource.Config.ResponseTimeout);

                    var control = ReadControl(responseDTO);

                    if (control == 0)
                        return Close(EConnectResult.ServerVerificationFailed);

                    if (control == 3)
                        return Close(control);

                    if (control == 2 || control == 5)
                    {
                        SocketResource.SetTokenToReconnect();
                        if (_firstConnect)
                            return Close(EConnectResult.HandshakeFailed);

                        if (control == 2)
                        {
                            return await SendAuth();
                        }
                        else
                        {
                            if (await SocketResource.SendAsEncryptBytes(SocketResource.Salt))
                            {
                                Start();
                                return EConnectResult.Success;
                            }
                        }
                    }
                    else if (control == 1 || control == 4)
                    {
                        var pKey = ReadPublicKey(responseDTO);
                        var signature = SocketResource.BuildSignature(pKey, SocketResource.Salt);
                        if (signature.Length < 1)
                            return Close(EConnectResult.HandshakeFailed);

                        if (await SocketResource.Ersa.VerifyDataRsa(signature, ReadSign(responseDTO)))
                        {
                            if (SocketResource.SetAesGcmKey(pKey.Span, SocketResource.Salt))
                            {
                                _firstConnect = false;
                                if (control == 1)
                                    return await SendAuth();
                                else
                                {
                                    Start();
                                    return EConnectResult.Success;
                                }
                            }
                            else return Close(EConnectResult.HandshakeFailed);
                        }
                    }
                }
                else return Close(EConnectResult.ServerVerificationFailed);
            }
            catch
            {
                return Close(EConnectResult.ServerUnavailable);
            }
            finally
            {
                lock (_lock)
                    _connecting = false;
            }
            return Close(EConnectResult.ServerUnavailable);
        }

        byte ReadControl(ReadOnlyMemory<byte> buffer) => buffer.Length < 1 ? (byte)0 : buffer.Span[0];
        ReadOnlyMemory<byte> ReadPublicKey(ReadOnlyMemory<byte> buffer)
        {
            if (buffer.Length < 5)
                return ReadOnlyMemory<byte>.Empty;

            var span = buffer.Span;
            ushort publicKeyLength = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(1, 2));
            if (publicKeyLength + 5 > buffer.Length)
                return ReadOnlyMemory<byte>.Empty;

            return buffer.Slice(5, publicKeyLength);
        }
        ReadOnlyMemory<byte> ReadSign(ReadOnlyMemory<byte> buffer)
        {
            if (buffer.Length < 5)
                return ReadOnlyMemory<byte>.Empty;

            var span = buffer.Span;
            ushort publicKeyLength = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(1, 2));
            ushort signatureLength = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(3, 2));
            if (publicKeyLength + signatureLength + 5 > buffer.Length)
                return ReadOnlyMemory<byte>.Empty;

            return buffer.Slice(5 + publicKeyLength, signatureLength);
        }
        ReadOnlyMemory<byte> BuildConnectDTO()
        {
            if (SocketResource == null)
                return ReadOnlyMemory<byte>.Empty;

            RandomNumberGenerator.Fill(SocketResource.Salt);
            UserId.TryWriteBytes(_bufferConnectDTO.AsMemory().Span);
            SocketResource.TokenToReconnect.CopyTo(_bufferConnectDTO.AsMemory(16));
            SocketResource.Salt.CopyTo(_bufferConnectDTO.AsMemory(48));
            SocketResource.PublicKey.CopyTo(_bufferConnectDTO.AsMemory(80));
            return _bufferConnectDTO.AsMemory(0, 80 + SocketResource.PublicKey.Length);
        }

        async Task<byte> SendAuth()
        {
            var objAuth = GetAuthorization();
            if (objAuth == null || SocketResource == null)
                return Close(EConnectResult.InvalidAuthData);

            if (await SocketResource.SendBytes(objAuth))
            {
                var msg = await SocketResource.ReceiveEncryptWithTimeout();
                if (msg.Length < 1)
                    return Close(EConnectResult.ServerUnavailable);

                var resAuth = SocketResource.Config.ESerial.Deserialize<byte>(msg.Span);
                if (resAuth == 0)
                    Start();
                return resAuth;
            }
            else return Close(EConnectResult.ServerUnavailable);
        }

        async Task<EConnectResult> ConnectWithTimeout(Socket socket, IPEndPoint endpoint, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await socket.ConnectAsync(endpoint).WaitAsync(cts.Token);
                return EConnectResult.Success;
            }
            catch (OperationCanceledException)
            {
                return EConnectResult.Timeout;
            }
            catch
            {
                return EConnectResult.ServerUnavailable;
            }
        }

        /// <summary>
        /// Efficiently sends a heartbeat message to detect disconnection.
        /// </summary>
        /// <remarks>
        /// The message is considered sent once it is successfully handed off to the operating system. 
        /// Note that returning <see langword="true"/> does not guarantee that the connection is still active; 
        /// rather, it forces the underlying TCP socket to attempt a transmission, which triggers the stack to update and verify the current connection status.
        /// </remarks>
        /// <returns>
        /// <see langword="true"/> if the message was successfully passed to the operating system; otherwise, <see langword="false"/>.
        /// </returns>
        public ValueTask<bool> SendHeartbeat()
        {
            if (SocketResource?.BasicSocket == null)
                return ValueTask.FromResult(false);

            return SocketResource.ExecutorSend.TrySendHeartbeat(SocketResource.RunHeartbeatSend);
        }

        internal sealed override bool Start()
        {
            if (base.Start())
            {
                EClientStatus status = EClientStatus.Connected;
                lock (_lock)
                {
                    status = Status;
                    Status = EClientStatus.Connected;
                }
                if (status == EClientStatus.Disconnected)
                    OnConnected();
                else
                    _ = StartRebuildSessions();
                return true;
            }
            return false;
        }

        async ValueTask StartRebuildSessions()
        {
            await SocketResource!.RebuildSessions();
        }

        void Dispose()
        {
            if (SocketReceiveContext.IsSoftClose(SocketResource))
                Disconnect();
            else
            {
                if (_reconnectDelayMs > 0)
                {
                    lock (_lock)
                        Status = EClientStatus.ReconnectAttempt;

                    _ = StartReconnect();
                }
                else
                    Disconnect();
            }
        }

        async Task StartReconnect()
        {
            if (!await RunConnectWithReconnect(Address, _reconnectDelayMs))
                Disconnect();
        }

        EConnectResult Close(EConnectResult error)
        {
            ETCPSocket.Close(SocketResource?.BasicSocket);
            return error;
        }

        /// <summary>
        /// Closes the active connection or cancel an ongoing connection attempt, then releases all associated resources to prepare for a new connection in the future.
        /// </summary>
        public void Disconnect()
        {
            bool shouldCleanup = false;
            lock (_lock)
            {
                if (Status != EClientStatus.Disconnected)
                {
                    Status = EClientStatus.Disconnected;
                    _reconnectDelayMs = 0;
                    UserId = SetGuidUserId();
                    SocketResource?.Dispose();
                    shouldCleanup = true;
                }
            }
            if (shouldCleanup)
            {
                _sendFlowController.CancelAll();
                OnDisconnected();
            }
        }

        #endregion
    }
}
