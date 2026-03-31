// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Net;
using System.Net.Sockets;

namespace EnjoySockets
{
    public abstract class EUser<T1> where T1 : ESocketResource
    {
        public ETCPSocketType ESocketType { get; protected set; }
        internal Guid UserId { get; set; }

        public EndPoint? EndPointSocket { get => SocketResource?.BasicSocket?.RemoteEndPoint; }
        public AddressFamily? AddressFamilySocket { get => SocketResource?.BasicSocket?.AddressFamily; }

        internal T1? SocketResource;

        private protected EBufferControl BufferToSendMsg;
        private protected object _lock = new();

        public EUser(T1 esr)
        {
            SocketResource = esr;
            BufferToSendMsg = new(SocketResource.MessageBuffer);
        }

        internal virtual bool Start()
        {
            return SocketResource?.Run() ?? false;
        }

        /// <summary>
        /// Registers an instance in the resource and returns its generated ID.
        /// </summary>
        /// <param name="obj">
        /// The object to register. Must contain at least one non-static access method.
        /// </param>
        /// <returns>
        /// The generated instance ID (&gt; 0).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="obj"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the instance cannot be registered due to an invalid type
        /// or missing access configuration.
        /// </exception>
        public long InstanceRegister(object obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            var id = SocketResource?.RegisterPrivateInstance(obj);

#if DEBUG
            if (id == null || id <= 0)
                throw new InvalidOperationException(
                    $"Private instance of type '{obj.GetType().FullName}' cannot be registered. No exist any one fit method for: {ESocketType}");
#endif

            return id ?? 0;
        }

        /// <summary>
        /// Removes the specified instance from the resource.
        /// </summary>
        /// <param name="id">The ID of the instance to remove.</param>
        /// <returns>
        /// <see langword="true"/> if the instance was successfully removed; 
        /// <see langword="false"/> if the instance could not be removed or the ID is invalid.
        /// </returns>
        /// <remarks>
        /// After removal, any incoming data for this instance will be dropped.
        /// </remarks>
        public bool InstanceRemove(long id)
        {
            return SocketResource?.RemovePrivateInstance(id) ?? false;
        }

        /// <summary>
        /// Detaches all registered instances from the resource.
        /// </summary>
        /// <remarks>
        /// After detachment, any incoming data for these instances will be dropped.
        /// </remarks>
        public void InstanceDetach()
        {
            SocketResource?.ClearPrivateInstance();
        }

        /// <summary>
        /// Sends a message without a payload to the specified target.
        /// </summary>
        /// <remarks>
        /// The message is considered sent once it is successfully handed off to the operating system.
        /// <para>
        /// This does not guarantee delivery to the receiving client.
        /// </para>
        /// </remarks>
        /// <param name="target">The destination to which the message is sent.</param>
        /// <returns>
        /// <see langword="true"/> if the message was successfully passed to the operating system;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public ValueTask<bool> Send(string target)
        {
            return Send(0, target);
        }

        /// <summary>
        /// Sends a message with a payload to the specified target.
        /// </summary>
        /// <remarks>
        /// The message is considered sent once it is successfully handed off to the operating system.
        /// <para>
        /// This does not guarantee delivery to the receiving client.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">The type of the payload being sent.</typeparam>
        /// <param name="target">The destination to which the message is sent.</param>
        /// <param name="obj">The payload object to send. May be <see langword="null"/>.</param>
        /// <returns>
        /// <see langword="true"/> if the message was successfully passed to the operating system;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public ValueTask<bool> Send<T>(string target, T? obj)
        {
            return Send(0, target, obj);
        }

        /// <summary>
        /// Sends a message without a payload to the specified target and specified instance.
        /// </summary>
        /// <remarks>
        /// The message is considered sent once it is successfully handed off to the operating system.
        /// <para>
        /// This does not guarantee delivery to the receiving client.
        /// </para>
        /// </remarks>
        /// <param name="instance">The destination to which instance the message is sent.</param>
        /// <param name="target">The destination to which the message is sent.</param>
        /// <returns>
        /// <see langword="true"/> if the message was successfully passed to the operating system;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public ValueTask<bool> Send(long instance, string target)
        {
            return Send<object>(instance, target, null);
        }

        public virtual ValueTask<bool> Send<T>(long instance, string target, T? obj)
        {
            return ValueTask.FromResult(false);
        }

        /// <summary>
        /// Called when the connection has been successfully established.
        /// </summary>
        protected virtual void OnConnected() { }

        /// <summary>
        /// Called when the connection has been closed.
        /// </summary>
        /// <remarks>
        /// This method is invoked regardless of whether the disconnection
        /// was intentional or caused by an error.
        /// </remarks>
        protected virtual void OnDisconnected() { }

        static long LastId = DateTime.UtcNow.Ticks;
        /// <summary>
        /// Generates a thread-safe unique ID (long) for the entire application.
        /// </summary>
        public static long GetUniqueId()
        {
            return Interlocked.Increment(ref LastId);
        }
    }
}
