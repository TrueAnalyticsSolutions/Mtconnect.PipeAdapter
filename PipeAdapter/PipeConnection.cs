using Mtconnect.AdapterInterface.DataItems;
using System;
using System.Collections;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PipeAdapter
{
    public delegate void PipeConnectionConnected(PipeConnection connection);
    public delegate void PipeConnectionDisconnected(PipeConnection connection, Exception exception = null);
    public delegate bool PipeConnectionDataReceived(PipeConnection connection, string message);

    public class PipeConnection : IDisposable
    {
        /// <summary>
        /// An event that fires when the underlying client stream is opened and connected.
        /// </summary>
        public event PipeConnectionConnected OnConnected;

        /// <summary>
        /// An event that fires when the underlying client stream is closed and disconnected.
        /// </summary>
        public event PipeConnectionDisconnected OnDisconnected;

        /// <summary>
        /// An event that fires when data is fully parsed from the underlying client stream. Note that a new line is used to determine the end of a full message.
        /// </summary>
        public event PipeConnectionDataReceived OnDataReceived;

        /// <summary>
        /// A reference to the client user name and process ID currently connected to the pipe. If no client is connected, this returns <c>null</c>.
        /// </summary>
        public string ClientId { get; private set; }

        private NamedPipeServerStream _pipe { get; set; }

        private Task _listenerThread { get; set; }

        private CancellationTokenSource _listenerToken = new CancellationTokenSource();
        private CancellationTokenSource _clientToken = null;

        /// <summary>
        /// The period of time (in milliseconds) to timeout stream reading.
        /// </summary>
        public int Heartbeat { get; set; }

        /// <summary>
        /// Maximum amount of binary data to receive at a time.
        /// </summary>
        private const int BUFFER_SIZE = 4096;

        public ASCIIEncoding Encoder { get; set; } = new ASCIIEncoding();

        public PipeConnection(NamedPipeServerStream pipe, int heartbeat = 1000)
        {
            _pipe = pipe;
            Heartbeat = heartbeat;
        }

        public void Start(CancellationToken cancellationToken = default)
        {
            cancellationToken.Register(() => _listenerToken?.Cancel());

            _listenerThread = Task.Factory.StartNew(
                _listenForClients,
                _listenerToken.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );
        }
        public void Stop()
        {
            _clientDisconnect();

            _listenerToken.Cancel();
            _pipe.Disconnect();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out uint ClientProcessId);
        private static uint getNamedPipeClientProcID(NamedPipeServerStream pipe)
        {
            UInt32 nProcID;
            IntPtr hPipe = pipe.SafePipeHandle.DangerousGetHandle();
            if (GetNamedPipeClientProcessId(hPipe, out nProcID))
                return nProcID;
            return 0;
        }

        private void _clientConnect()
        {
            if (_pipe.IsConnected)
            {
                _clientToken = new CancellationTokenSource();
                ClientId = $"{_pipe.GetImpersonationUserName()}:{getNamedPipeClientProcID(_pipe)}";
                OnConnected?.Invoke(this);
            }
        }

        private void _clientRead()
        {
            bool heartbeatActive = false;
            byte[] message = new byte[BUFFER_SIZE];
            int length = 0;

            //ArrayList readList = new ArrayList();

            int bytesRead = 0;

            //readList.Clear();
            //readList.Add(_pipe);
            //if (Heartbeat > 0 && heartbeatActive)
            //    Socket.Select(readList, null, null, (int)(Heartbeat * 2000));
            if (heartbeatActive)// && readList.Count == 0)
            {
                _clientDisconnect(new TimeoutException("Heartbeat timed out, closing connection"));
                return;
            }
            bytesRead = _pipe.Read(message, length, BUFFER_SIZE - length);

            // See if we have a line
            int pos = length;
            length += bytesRead;
            int eol = 0;
            for (int i = pos; i < length; i++)
            {
                if (message[i] == '\n')
                {

                    String line = Encoder.GetString(message, eol, i);

                    if (OnDataReceived != null)
                        heartbeatActive = OnDataReceived(this, line);

                    eol = i + 1;
                }
            }

            // Remove the lines that have been processed.
            if (eol > 0)
            {
                length = length - eol;
                // Shift the message array to remove the lines.
                if (length > 0)
                    Array.Copy(message, eol, message, 0, length);
            }
        }
        private void _clientDisconnect(Exception ex = null)
        {
            _clientToken?.Cancel();
            _clientToken?.Dispose();
            ClientId = null;
            OnDisconnected?.Invoke(this, ex);
        }
        private async void _listenForClients()
        {
            while (!_listenerToken.Token.IsCancellationRequested)
            {
                if (!_pipe.IsConnected)
                {
                    _clientDisconnect();
                    
                    await _pipe.WaitForConnectionAsync(_listenerToken.Token);

                    if (_pipe.IsConnected)
                    {
                        _clientConnect();
                    }
                } else if (_pipe.InBufferSize > 0 && _clientToken?.IsCancellationRequested == false)
                {
                    _clientRead();
                }
            }
        }

        public void Write(string message) => Write(Encoder.GetBytes(message));
        public void Write(byte[] message)
        {
            if (_pipe.IsConnected && !_clientToken.IsCancellationRequested)
            {
                try
                {
                    _pipe.Write(message, 0, message.Length);
                }
                catch (Exception ex)
                {
                    _clientToken.Cancel();
                    _pipe.Disconnect();
                }
            }
        }

        public void Flush() => _pipe.Flush();


        public void Dispose()
        {
            Flush();

            _listenerToken.Cancel();
            _listenerToken.Dispose();
            _clientToken.Cancel();
            _clientToken.Dispose();
            _listenerThread?.Dispose();
            _pipe.Disconnect();
            _pipe.Close();
            _pipe.Dispose();
        }
    }
}
