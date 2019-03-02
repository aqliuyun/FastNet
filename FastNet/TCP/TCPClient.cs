using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FastNet.TCP
{
    public class TCPClient : TCPSocket
    {
        protected Socket socket;
        protected SocketAsyncEventArgs sendEvtArgs;
        protected SocketAsyncEventArgs recvEvtArgs;
        public Action<TCPSocket> OnConnected;
        public int Retry = 3;
        public int RetryTimeout = 2000;
        protected IPEndPoint remote;
        public TCPClient(string ip, int port)
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            remote = new IPEndPoint(IPAddress.Parse(ip), port);
            socket.Blocking = false;
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            socket.ReceiveBufferSize = 32 * 1024;
            socket.SendBufferSize = 4 * 1024;
            socket.SendTimeout = 10000;
            socket.ReceiveTimeout = 10000;
        }

        public void Connect()
        {            
            recvEvtArgs = BufferManager.Instance.Pop();
            var evt_connect = recvEvtArgs;
            evt_connect.RemoteEndPoint = remote;
            evt_connect.Completed += Evt_connect_Completed;
            if(!socket.ConnectAsync(evt_connect))
            {
                Evt_connect_Completed(this, evt_connect);
            }
        }

        private void Evt_connect_Completed(object sender, SocketAsyncEventArgs e)
        {
            e.Completed -= Evt_connect_Completed;
            if (e.LastOperation == SocketAsyncOperation.Connect && e.SocketError == SocketError.Success)
            {
                StartReceive(socket);
                if (OnConnected != null)
                {
                    OnConnected(this);
                }
                BufferManager.Instance.Push(e);                                
            }
            else if (e.SocketError != SocketError.Success)
            {                
                if (this.Retry-- > 0)
                {
                    BufferManager.Instance.Push(e);
                    Task.Factory.StartNew(() => {
                        Thread.Sleep(this.RetryTimeout);
                        Connect();
                    });
                }
                else
                {
                    this.CloseSocket(socket, e);
                }
            }
        }
    }
}
