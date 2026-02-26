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
