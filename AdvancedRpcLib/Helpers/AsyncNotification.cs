using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib.Helpers
{
    class AsyncNotification
    {
        public delegate bool DataReceivedDelegate(byte[] data, RpcMessage message);

        private readonly List<Tuple<DataReceivedDelegate, bool>> _callbacks = new List<Tuple<DataReceivedDelegate, bool>>();
        private readonly ILogger _logger;

        public AsyncNotification(ILogger logger)
        {
            _logger = logger;
        }

        public void Register(DataReceivedDelegate callback, bool autoremove)
        {
            lock (_callbacks)
            {
                _callbacks.Add(Tuple.Create(callback, autoremove));
            }
        }

        public bool Notify(byte[] data, IRpcSerializer serializer)
        {
            Tuple<DataReceivedDelegate, bool>[] callbacks;
            lock (_callbacks)
            {
                callbacks = _callbacks.ToArray();
            }

            var msg = serializer.DeserializeMessage<RpcMessage>(data);
            for (int i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    if (callbacks[i].Item1.Invoke(data, msg))
                    {
                        if (callbacks[i].Item2)
                        {
                            lock (_callbacks)
                            {
                                _callbacks.Remove(callbacks[i]);
                            }
                        }
                        return true;
                    }
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in message notification");
                    throw;
                }
            }
            return false;
        }
    }
}
