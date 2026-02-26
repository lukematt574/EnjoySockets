namespace EnjoySockets
{
    public enum EChannelType
    {
        Private, Share
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class EAttrChannel : Attribute
    {
        private EChannelType channelType;
        /// <summary>
        /// Defines how the underlying message channel is resolved for the target method.
        /// Messages are always routed to this method.
        /// Private – a dedicated channel is created per socket.
        /// Share – a single global channel is shared across all sockets.
        /// Default value is <see cref="EChannelType.Private"/>.
        /// </summary>
        public EChannelType ChannelType { get { return channelType; } set { channelType = value; } }

        private ushort channelTasks = 1;
        /// <summary>
        /// Specifies the number of worker tasks responsible for processing
        /// and executing messages for this channel.
        /// Default value is 1.
        /// </summary>
        public ushort ChannelTasks { get { return channelTasks; } set { channelTasks = value; } }
    }
}
