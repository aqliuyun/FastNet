using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FastNet
{
    public class BufferManager
    {
        public static readonly BufferManager Instance = new BufferManager();
        public ConcurrentQueue<SocketAsyncEventArgs> Pools = new ConcurrentQueue<SocketAsyncEventArgs>();
        public int BufferSize { get; private set; }
        public void Init(int count, int size = 2048)
        {
            BufferSize = size;
            for (int i = 0; i < count; i++)
            {
                var args = new SocketAsyncEventArgs();
                args.SetBuffer(new byte[BufferSize], 0, BufferSize);
                Pools.Enqueue(args);
            }
        }

        private void Args_Completed(object sender, SocketAsyncEventArgs e)
        {
            e.Completed -= Args_Completed;
            if(e.LastOperation == SocketAsyncOperation.SendTo)
            {
                this.Push(e);
            }
        }

        public SocketAsyncEventArgs Pop()
        {
            if (Pools.TryDequeue(out SocketAsyncEventArgs args))
            {
                args.Completed += Args_Completed;
                return args;

            }
            args = new SocketAsyncEventArgs();
            args.Completed += Args_Completed;
            args.SetBuffer(new byte[BufferSize], 0, BufferSize);
            return args;
        }        

        public void Push(SocketAsyncEventArgs args)
        {
            Pools.Enqueue(args);
        }

        public void ClearBuffer(SocketAsyncEventArgs m_SendEventArgs)
        {
            Array.Clear(m_SendEventArgs.Buffer, 0, BufferSize);
        }
    }
}
