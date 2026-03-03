// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EnjoySockets
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class EAttr : Attribute
    {
        private int maxParamSize = 0;
        /// <summary>
        /// Max size param object in bytes. 0 value means without limits. Default value is 0.
        /// </summary>
        public int MaxParamSize { get { return maxParamSize; } set { maxParamSize = value; AddChange(); } }

        private long access;
        /// <summary>
        /// Value define access to method. 0 value means access always open. Default value is 0. (Work only on server type methods)
        /// </summary>
        public long Access { get { return access; } set { access = value; AddChange(); } }

        private ushort channelId;
        /// <summary>
        /// Channel id set as const ushort with 'EAttrChannel' in entire project. Default is 0 which use: [EAttrChannel(ChannelType = EChannelType.Private, ChannelTasks = 1)]
        /// </summary>
        public ushort ChannelId { get { return channelId; } set { channelId = value; AddChange(); } }

        private ushort pooling;
        /// <summary>
        /// Pool id set as const ushort with 'EAttrPool' in entire project. Default is 0 - means no pooling.
        /// </summary>
        public ushort PoolId { get { return pooling; } set { pooling = value; AddChange(); } }

        HashSet<string> propertyChanged = new();
        static PropertyInfo[] properties = typeof(EAttr).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        private void AddChange([CallerMemberName] string? name = null)
        {
            if (name != null)
            {
                lock (propertyChanged)
                {
                    propertyChanged.Add(name);
                }
            }
        }
        /// <summary>
        /// Change all default property from 'attr' param
        /// </summary>
        internal void Fill(EAttr? attr)
        {
            if (attr == null) return;
            foreach (var property in properties)
            {
                lock (propertyChanged)
                {
                    if (!propertyChanged.Contains(property.Name) && property.CanWrite)
                    {
                        var newValue = property.GetValue(attr);
                        property.SetValue(this, newValue);
                    }
                }
            }
        }

        internal EAttr Clone()
        {
            return new()
            {
                access = access,
                propertyChanged = propertyChanged,
                maxParamSize = maxParamSize,
                channelId = channelId,
                pooling = pooling
            };
        }
    }
}
