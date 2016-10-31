﻿//----------------------------------------------------------------------- 
// ETP DevKit, 1.1
//
// Copyright 2016 Energistics
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//   
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Energistics.Common;
using Energistics.Protocol.Core;

namespace Energistics
{
    /// <summary>
    /// An ETP server session implementation that can be used with .NET WebSockets.
    /// </summary>
    /// <seealso cref="Energistics.Common.EtpSession" />
    public class EtpServerHandler : EtpSession
    {
        private const int BufferSize = 4096;
        private WebSocket _socket;

        /// <summary>
        /// Initializes the <see cref="EtpServerHandler"/> class.
        /// </summary>
        static EtpServerHandler()
        {
            Clients = new ConcurrentDictionary<string, EtpServerHandler>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EtpServerHandler"/> class.
        /// </summary>
        /// <param name="webSocket">The web socket.</param>
        /// <param name="application">The server application name.</param>
        /// <param name="version">The server application version.</param>
        /// <param name="headers">The WebSocket or HTTP headers.</param>
        public EtpServerHandler(WebSocket webSocket, string application, string version, IDictionary<string, string> headers) : base(application, version, headers)
        {
            _socket = webSocket;
            Register<ICoreServer, CoreServerHandler>();
        }

        /// <summary>
        /// Gets the collection of active clients.
        /// </summary>
        /// <value>The clients.</value>
        public static ConcurrentDictionary<string, EtpServerHandler> Clients { get; }

        /// <summary>
        /// Gets a value indicating whether the connection is open.
        /// </summary>
        /// <value>
        ///   <c>true</c> if the connection is open; otherwise, <c>false</c>.
        /// </value>
        public bool IsOpen => (_socket?.State ?? WebSocketState.None) == WebSocketState.Open;

        /// <summary>
        /// Closes the WebSocket connection for the specified reason.
        /// </summary>
        /// <param name="reason">The reason.</param>
        public override void Close(string reason)
        {
            if (!IsOpen) return;
            _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
            Logger.Debug(Log("[{0}] Socket session closed.", SessionId));
        }

        /// <summary>
        /// Accepts the WebSocket connection represented by the specified context.
        /// </summary>
        /// <param name="context">The WebSocket context.</param>
        /// <returns>An awaitable <see cref="Task"/>.</returns>
        public async Task Accept(WebSocketContext context)
        {
            SessionId = Guid.NewGuid().ToString();

            Logger.Debug(Log("[{0}] Socket session connected.", SessionId));

            try
            {
                // keep track of connected clients
                Clients.AddOrUpdate(SessionId, this, (id, client) => this);

                using (var stream = new MemoryStream())
                {
                    var token = new CancellationToken();

                    while (_socket.State == WebSocketState.Open)
                    {
                        var buffer = new ArraySegment<byte>(new byte[BufferSize]);
                        var result = await _socket.ReceiveAsync(buffer, token);

                        // transfer received data to MemoryStream
                        stream.Write(buffer.Array, 0, result.Count);

                        // do not process data until EndOfMessage received
                        if (!result.EndOfMessage || result.CloseStatus.HasValue)
                            continue;

                        // filter null bytes from data buffer
                        var bytes = stream.GetBuffer();

                        if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            OnDataReceived(bytes);
                        }
                        else // json encoding
                        {
                            var message = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                            OnMessageReceived(message);
                        }

                        // clear and reuse MemoryStream
                        stream.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Error: {0}", ex.Message);
                Logger.Error(ex);
                throw;
            }
            finally
            {
                EtpServerHandler item;

                // remove client after connection ends
                if (Clients.TryRemove(SessionId, out item))
                {
                    if (item != this)
                    {
                        Clients.AddOrUpdate(item.SessionId, item, (id, client) => item);
                    }
                }
            }
        }

        /// <summary>
        /// Sends the specified data.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The length.</param>
        protected override void Send(byte[] data, int offset, int length)
        {
            CheckDisposed();

            var buffer = new ArraySegment<byte>(data, offset, length);
            _socket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None).Wait();
        }

        /// <summary>
        /// Sends the specified messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void Send(string message)
        {
            CheckDisposed();

            var buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
            _socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None).Wait();
        }
    }
}
