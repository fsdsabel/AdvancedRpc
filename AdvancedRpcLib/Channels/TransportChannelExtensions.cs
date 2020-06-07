using System;
using AdvancedRpcLib.Channels.NamedPipe;

namespace AdvancedRpcLib.Channels
{
    public static class TransportChannelExtensions
    {
        public static string GetImpersonationUserName(this ITransportChannel transportChannel)
        {
            if (transportChannel == null) throw new ArgumentNullException(nameof(transportChannel));
            if (transportChannel is NamedPipeTransportChannel nptc)
            {
                return nptc.GetImpersonationUserName();
            }
            throw new NotSupportedException($"Cannot obtain impersonation user name from channel type {transportChannel.GetType()}");
        }


        public static void RunAsClient(this ITransportChannel transportChannel, Action action)
        {
            if (transportChannel == null) throw new ArgumentNullException(nameof(transportChannel));
            if (transportChannel is NamedPipeTransportChannel nptc)
            {
                nptc.RunAsClient(action);
            }
            else
            {
                throw new NotSupportedException(
                    $"Cannot impersonate user for channel type {transportChannel.GetType()}");
            }
        }
    }
}