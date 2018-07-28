using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedRpcLib
{


 

   

    class AsyncNotification
    {
        public delegate bool DataReceivedDelegate(ReadOnlySpan<byte> data);

        private readonly List<Tuple<DataReceivedDelegate, bool>> _callbacks = new List<Tuple<DataReceivedDelegate, bool>>();

        public void Register(DataReceivedDelegate callback, bool autoremove)
        {
            lock (_callbacks)
            {
                _callbacks.Add(Tuple.Create<DataReceivedDelegate, bool>(callback, autoremove));
            }
        }



        public bool Notify(ReadOnlySpan<byte> data)
        {
            lock (_callbacks)
            {
                for(int i=0;i<_callbacks.Count;i++)
                {
                    try
                    {
                        if (_callbacks[i].Item1.Invoke(data))
                        {
                            if(_callbacks[i].Item2)
                            {
                                _callbacks.RemoveAt(i);
                            }
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error in message notification: " + ex);
                    }
                }
            }
            return false;
        }
    }

}
