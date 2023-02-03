using Mtconnect.AdapterInterface;

namespace PipeAdapter
{
    public sealed class PipeAdapterOptions : AdapterOptions
    {
        /// <summary>
        /// The name for the pipe(s)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Number of simultaneous connections expected to be allowed.
        /// </summary>
        public int MaxConcurrentConnections { get; set; } = 2;
    }
}
