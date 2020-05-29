using System;

namespace AdvancedRpcLib.Channels
{
    public class ChannelConnectedEventArgs<TChannel> : EventArgs
    {
        public TChannel TransportChannel { get; }

        internal ChannelConnectedEventArgs(TChannel transportChannel)
        {
            TransportChannel = transportChannel;
        }
    }
}