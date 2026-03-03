// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
namespace EnjoySockets
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class EAttrPool : Attribute
    {
        private uint maxPoolObjs = 0;
        /// <summary>
        /// Max objects in pool. Default value is 0.
        /// </summary>
        public uint MaxPoolObjs { get { return maxPoolObjs; } set { maxPoolObjs = value; } }
    }
}
