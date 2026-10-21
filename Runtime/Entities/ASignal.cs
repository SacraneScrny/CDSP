namespace CDSP.Entities
{
    public abstract class ASignal
    {
        public abstract int SubscriberCount { get; }

        /// <summary>Optional debug tag (any object); error messages print its runtime type.
        /// Pass e.g. the owning context: <c>new Signal(this)</c>.</summary>
        public object Tag { get; }

        protected ASignal(object tag) { Tag = tag; }

        internal string Describe(string payload) =>
            "Signal" + (Tag != null ? $" (tag: {Tag.GetType().FullName})" : "") + $"<{payload}>";
    }
}