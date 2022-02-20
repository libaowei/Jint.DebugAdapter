﻿using System.Net;
using System.Net.Sockets;

namespace Jint.DebugAdapter
{
    public class TcpAdapter : Adapter
    {
        private readonly int port;

        public TcpAdapter(int port = 4711)
        {
            this.port = port;
        }

        protected override void StartListening()
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            Logger.Log($"Listening on {listener.LocalEndpoint}");
            var client = listener.AcceptTcpClient();
            var stream = client.GetStream();
            InitializeStreams(stream, stream);
        }
    }
}
