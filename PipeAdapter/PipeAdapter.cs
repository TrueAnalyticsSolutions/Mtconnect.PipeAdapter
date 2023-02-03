using Microsoft.Extensions.Logging;
using Mtconnect;
using Mtconnect.AdapterInterface.Contracts;
using System;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Linq;
using System.Threading;

namespace PipeAdapter
{
    public sealed class PipeAdapter : Adapter, IDisposable
    {
        private bool _disposing = false;

        private ConcurrentBag<PipeConnection> Pipes { get; set; } = new ConcurrentBag<PipeConnection>();

        public string PipeName { get; private set; }

        public int MaxConnections { get; private set; } = 2;

        public PipeAdapter(PipeAdapterOptions options, ILogger<Adapter> logger = null) : base(options, logger)
        {
            PipeName = options.Name;
            MaxConnections = options.MaxConcurrentConnections;

            for (int i = 0; i < MaxConnections; i++)
            {
                var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, MaxConnections, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                var connection = new PipeConnection(pipe, (int)Heartbeat);
                connection.OnDataReceived += Connection_OnDataReceived;
                Pipes.Add(connection);
            }
        }

        private bool Connection_OnDataReceived(PipeConnection connection, string message)
        {
            // TODO: Handle Adapter Commands, like * PING
            return true;
        }

        /// <inheritdoc />
        public override void Start(bool begin = true, CancellationToken token = default)
        {
            if (State <= AdapterStates.NotStarted)
            {
                _logger?.LogInformation("Starting Adapter");
                State = AdapterStates.Starting;

                foreach (var pipe in Pipes)
                {
                    pipe.Start(token);
                }

                State = AdapterStates.Started;
            }

            if (begin) Begin();

            State = AdapterStates.Busy;
        }

        /// <inheritdoc />
        public override void Stop()
        {
            base.Stop();

            if (State > AdapterStates.NotStarted)
            {
                _logger?.LogInformation("Stopping Adapter");
                State = AdapterStates.Stopping;

                foreach (var pipe in Pipes)
                {
                    pipe.Stop();
                }

                State = AdapterStates.Stopped;
            }
        }

        /// <inheritdoc />
        protected override void Write(string message, string clientId = null)
        {
            _logger?.LogDebug("Sending message: {Message}", message);
            if (Pipes.Any())
            {
                if (clientId == null)
                {
                    foreach (var pipe in Pipes)
                    {
                        pipe.Write(message);
                    }
                } else if (Pipes.Any(o => o.ClientId == clientId))
                {
                    PipeConnection pipe = Pipes.FirstOrDefault(o => o.ClientId == clientId);
                    pipe?.Write(message);
                }
            }
        }

        /// <summary>
        /// Flush all the communications to all the clients
        /// TODO: Exception handling.
        /// </summary>
        public void FlushAll()
        {
            foreach (var pipe in Pipes)
                pipe.Flush();
        }

        public void Dispose()
        {
            if (_disposing) return;
            _disposing = true;

            bool removingPipe = true;
            while (removingPipe)
            {
                removingPipe = Pipes.TryTake(out PipeConnection pipe);
                pipe?.Dispose();
            }

            _disposing = false;
        }
    }
}
