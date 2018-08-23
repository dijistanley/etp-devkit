﻿//----------------------------------------------------------------------- 
// ETP DevKit, 1.2
//
// Copyright 2018 Energistics
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

using System.Collections.Concurrent;
using System.Collections.Generic;
using Avro.IO;
using Energistics.Etp.Common;
using Energistics.Etp.Common.Datatypes;
using Energistics.Etp.Common.Protocol.Core;

namespace Energistics.Etp.v12.Protocol.DiscoveryQuery
{
    /// <summary>
    /// Base implementation of the <see cref="IDiscoveryQueryCustomer"/> interface.
    /// </summary>
    /// <seealso cref="Energistics.Etp.Common.EtpProtocolHandler" />
    /// <seealso cref="Energistics.Etp.v12.Protocol.DiscoveryQuery.IDiscoveryQueryCustomer" />
    public class DiscoveryQueryCustomerHandler : EtpProtocolHandler, IDiscoveryQueryCustomer
    {
        private readonly IDictionary<long, string> _requests;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscoveryQueryCustomerHandler"/> class.
        /// </summary>
        public DiscoveryQueryCustomerHandler() : base((int)Protocols.DiscoveryQuery, "customer", "store")
        {
            _requests = new ConcurrentDictionary<long, string>();
        }

        /// <summary>
        /// Sends a FindResources message to a store.
        /// </summary>
        /// <param name="uri">The URI.</param>
        /// <returns>The message identifier.</returns>
        public virtual long FindResources(string uri)
        {
            var header = CreateMessageHeader(Protocols.DiscoveryQuery, MessageTypes.DiscoveryQuery.FindResources);

            var findResources = new FindResources()
            {
                Uri = uri
            };
            
            return Session.SendMessage(header, findResources,
                h => _requests[h.MessageId] = uri // Cache requested URIs by message ID
            );
        }

        /// <summary>
        /// Handles the FindResourcesResponse event from a store.
        /// </summary>
        public event ProtocolEventHandler<FindResourcesResponse, string> OnFindResourcesResponse;

        /// <summary>
        /// Decodes the message based on the message type contained in the specified <see cref="IMessageHeader" />.
        /// </summary>
        /// <param name="header">The message header.</param>
        /// <param name="decoder">The message decoder.</param>
        /// <param name="body">The message body.</param>
        protected override void HandleMessage(IMessageHeader header, Decoder decoder, string body)
        {
            switch (header.MessageType)
            {
                case (int)MessageTypes.DiscoveryQuery.FindResourcesResponse:
                    HandleFindResourcesResponse(header, decoder.Decode<FindResourcesResponse>(body));
                    break;

                default:
                    base.HandleMessage(header, decoder, body);
                    break;
            }
        }

        /// <summary>
        /// Handles the Acknowledge message.
        /// </summary>
        /// <param name="header">The message header.</param>
        /// <param name="acknowledge">The Acknowledge message.</param>
        protected override void HandleAcknowledge(IMessageHeader header, IAcknowledge acknowledge)
        {
            // Handle case when "No Data" Acknowledge message was received
            if (header.MessageFlags == (int)MessageFlags.NoData)
            {
                GetRequestedUri(header);
            }

            base.HandleAcknowledge(header, acknowledge);
        }

        /// <summary>
        /// Handles the FindResourcesResponse message from a store.
        /// </summary>
        /// <param name="header">The message header.</param>
        /// <param name="findResourcesResponse">The FindResourcesResponse message.</param>
        protected virtual void HandleFindResourcesResponse(IMessageHeader header, FindResourcesResponse findResourcesResponse)
        {
            var uri = GetRequestedUri(header);
            var args = Notify(OnFindResourcesResponse, header, findResourcesResponse, uri);
            HandleFindResourcesResponse(args);
        }

        /// <summary>
        /// Handles the FindResourcesResponse message from a store.
        /// </summary>
        /// <param name="args">The <see cref="ProtocolEventArgs{FindResourcesResponse}"/> instance containing the event data.</param>
        protected virtual void HandleFindResourcesResponse(ProtocolEventArgs<FindResourcesResponse, string> args)
        {
        }

        /// <summary>
        /// Gets the requested URI from the internal cache of message IDs.
        /// </summary>
        /// <param name="header">The message header.</param>
        /// <returns>The requested URI.</returns>
        private string GetRequestedUri(IMessageHeader header)
        {
            string uri;

            if (_requests.TryGetValue(header.CorrelationId, out uri) && header.MessageFlags != (int)MessageFlags.MultiPart)
            {
                _requests.Remove(header.CorrelationId);
            }

            return uri;
        }
    }
}
