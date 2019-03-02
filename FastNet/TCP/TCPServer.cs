using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.Threading.Tasks;

namespace FastNet.TCP
{
    public class TCPServer : TCPSocket
    {
        /// <summary>
        /// 服务器上连接的客户端总数
        /// </summary>
        public int CurrentConnects;

        /// <summary>
        /// 服务器能接受的最大连接数量
        /// </summary>
        private int MaxConnections;

        /// <summary>
        /// clients
        /// </summary>
        private List<TCPSocket> clients;

        /// <summary>
        /// 构造函数，建立一个未初始化的服务器实例
        /// </summary>
        /// <param name="maxConnect">服务器的最大连接数据</param>
        /// <param name="bufferSize"></param>
        public TCPServer(Int32 maxConnect)
        {
            this.CurrentConnects = 0;
            this.MaxConnections = maxConnect;
            this.clients = new List<TCPSocket>(this.MaxConnections);
            // 创建监听socket
            this.connectSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            connectSocket.Blocking = false;
            connectSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            connectSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        }

        /// <summary>
        /// 启动服务，开始监听
        /// </summary>
        /// <param name="port">Port where the server will listen for connection requests.</param>
        public void Listen(Int32 port)
        {
            // 获得主机相关信息
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), port);
            if (localEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // 配置监听socket为 dual-mode (IPv4 & IPv6) 
                // 27 is equivalent to IPV6_V6ONLY socket option in the winsock snippet below,
                connectSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                connectSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, localEndPoint.Port));
            }
            else
            {
                connectSocket.Bind(localEndPoint);
            }
            // 开始监听
            connectSocket.Listen(this.MaxConnections);            
            // 在监听Socket上投递一个接受请求。                
            this.OnAcceptDone += (t, e) =>
            {
                Interlocked.Increment(ref this.CurrentConnects);
                
                //Console.WriteLine("connects:" + this.CurrentConnects.ToString());
                
                Socket s = e.AcceptSocket;
                s.NoDelay = true;
                s.Blocking = false;
                s.ReceiveBufferSize = 32 * 1024;
                s.SendBufferSize = 4 * 1024;
                Socket listenSocket = (Socket)e.UserToken;
                if (s.Connected)
                {
                    try
                    {
                        if (this.CurrentConnects > this.MaxConnections)
                        {
                            s.Close();
                        }
                        //Console.WriteLine("connected:"+this.CurrentConnects);
                        var client = new TCPSocket();
                        client.AddReceiveListener(m_AnyListener);
                        client.StartReceive(s);                        
                        clients.Add(client);
                    }
                    catch (SocketException ex)
                    {
                        this.CloseSocket(s, e);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
                else
                {
                    this.CloseSocket(s, e);
                }
            };
            for (int i = 0; i < 100; i++)
            {
                this.DoAccept();
            }
            Console.WriteLine("start accept");
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public void Stop()
        {

            try
            {
                this.connectSocket.Shutdown(SocketShutdown.Both);
            }
            catch { }
            finally
            { connectSocket.Close(); }
        }

        protected override void CloseSocket(Socket s, SocketAsyncEventArgs e)
        {
            Interlocked.Decrement(ref this.CurrentConnects);
            base.CloseSocket(s, e);
        }
    }
}
