using System;
using System.Collections.Generic;
using System.Reflection;

namespace AdvancedRpcLib.Helpers
{
    class AsyncNotification
    {
        public delegate bool DataReceivedDelegate(byte[] data, RpcMessage message);

        private readonly List<Tuple<DataReceivedDelegate, bool>> _callbacks = new List<Tuple<DataReceivedDelegate, bool>>();

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
                    Console.WriteLine("Error in message notification: " + ex);
                    throw;
                }
            }
            return false;
        }
    }
}
