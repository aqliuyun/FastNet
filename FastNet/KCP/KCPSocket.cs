//#define BigEndian
#define LittleEndian
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace FastNet
{
    public delegate void KCPReceiveListener(byte[] buff, int size, IPEndPoint remotePoint);
    public class KCPSocket
    {
        private static bool m_IsRunning = false;
        protected Socket m_SystemSocket;
        protected IPEndPoint m_LocalEndPoint;
        private AddressFamily m_AddrFamily;

        //KCP参数
        protected static readonly IDictionary<IPEndPoint, KCPProxy> m_ListKcp = new ConcurrentDictionary<IPEndPoint, KCPProxy>();
        protected uint m_KcpKey = 0;
        protected KCPReceiveListener m_AnyEPListener;
        private bool isLisenter = false;
        private int m_Port = 0;
        private long startTime = DateTimeOffset.UtcNow.Ticks / 10000L - 62135596800000L;
        private int byteCount = 0;

        public KCPSocket(uint kcpKey, AddressFamily family = AddressFamily.InterNetwork)
        {
            m_AddrFamily = family;
            m_KcpKey = kcpKey;

            m_SystemSocket = new Socket(m_AddrFamily, SocketType.Dgram, ProtocolType.Udp);
            m_IsRunning = true;
            uint IOC_IN = 0x80000000;
            uint IOC_VENDOR = 0x18000000;
            uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            m_SystemSocket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
        }

        public void Bind(int bindPort)
        {
            IPEndPoint ipep = KCPProxy.GetIPEndPointAny(m_AddrFamily, bindPort);
            m_SystemSocket.Bind(ipep);
            m_Port = bindPort;
            isLisenter = bindPort > 0;
            StartRecv();
        }

        public void EnableReuseAddress()
        {
            m_SystemSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        }

        public void Dispose()
        {
            m_IsRunning = false;
            m_AnyEPListener = null;


            foreach (var key in m_ListKcp.Keys)
            {
                m_ListKcp[key].Dispose();
            }
            m_ListKcp.Clear();
            if (m_SystemSocket != null)
            {
                try
                {
                    m_SystemSocket.Shutdown(SocketShutdown.Both);
                }
                catch (Exception e)
                {
                }

                m_SystemSocket.Close();
                m_SystemSocket = null;
            }
        }


        public int LocalPort
        {
            get { return (m_SystemSocket.LocalEndPoint as IPEndPoint).Port; }
        }

        public string LocalIP
        {
            get { return (m_SystemSocket.LocalEndPoint as IPEndPoint).Address.ToString(); }
        }

        public IPEndPoint LocalEndPoint
        {
            get
            {
                if (m_LocalEndPoint == null ||
                    m_LocalEndPoint.Address.ToString() != (m_SystemSocket.LocalEndPoint as IPEndPoint).Address.ToString())
                {
                    IPAddress ip = IPAddress.Parse(LocalIP);
                    m_LocalEndPoint = new IPEndPoint(ip, LocalPort);
                }

                return m_LocalEndPoint;
            }
        }

        public Socket SystemSocket { get { return m_SystemSocket; } }

        public bool EnableBroadcast
        {
            get { return m_SystemSocket.EnableBroadcast; }
            set { m_SystemSocket.EnableBroadcast = value; }
        }

        private KCPProxy GetKcp(IPEndPoint ipep)
        {
            if (ipep == null || ipep.Port == 0 ||
                ipep.Address.Equals(IPAddress.Any) ||
                ipep.Address.Equals(IPAddress.IPv6Any))
            {
                return null;
            }
            KCPProxy proxy = null;
            if (m_ListKcp.ContainsKey(ipep))
            {
                return m_ListKcp[ipep];
            }
            proxy = new KCPProxy(m_KcpKey, (IPEndPoint)ipep, m_SystemSocket, true);
            proxy.AddReceiveListener(OnReceiveAny);

            m_ListKcp.Add(ipep, proxy);
            return proxy;
        }

        public virtual bool SendTo(byte[] buffer, int size, IPEndPoint remotePoint)
        {
            if (remotePoint.Address == IPAddress.Broadcast)
            {
                int cnt = m_SystemSocket.SendTo(buffer, size, SocketFlags.None, remotePoint);
                return cnt > 0;
            }
            else
            {
                KCPProxy proxy = GetKcp(remotePoint);
                if (proxy != null)
                {
                    return proxy.DoSend(buffer, size);
                }
            }

            return false;
        }

        public bool SendTo(string message, IPEndPoint remotePoint)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            return SendTo(buffer, buffer.Length, remotePoint);
        }

        public void SendTo(string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            foreach (var key in m_ListKcp.Keys)
            {
                ThreadPool.QueueUserWorkItem((x) =>
                {
                    m_ListKcp[key].DoSend(buffer, buffer.Length);
                });
            }
        }

        public static void Update()
        {

            if (m_IsRunning)
            {
                //获取时钟
                long current = KCPProxy.GetClockMS();
                foreach (var key in m_ListKcp.Keys)
                {
                    m_ListKcp[key].Update(current);

                }
            }
        }

        public void AddReceiveListener(IPEndPoint remotePoint, KCPReceiveListener listener)
        {
            KCPProxy proxy = GetKcp(remotePoint);
            if (proxy != null)
            {
                proxy.AddReceiveListener(listener);
            }
            else
            {
                m_AnyEPListener += listener;
            }
        }

        public void RemoveReceiveListener(IPEndPoint remotePoint, KCPReceiveListener listener)
        {
            KCPProxy proxy = GetKcp(remotePoint);
            if (proxy != null)
            {
                proxy.RemoveReceiveListener(listener);
            }
            else
            {
                m_AnyEPListener -= listener;
            }
        }

        public void AddReceiveListener(KCPReceiveListener listener)
        {
            m_AnyEPListener += listener;
        }

        public void RemoveReceiveListener(KCPReceiveListener listener)
        {
            m_AnyEPListener -= listener;
        }


        private void OnReceiveAny(byte[] buffer, int size, IPEndPoint remotePoint)
        {
            if (m_AnyEPListener != null)
            {
                m_AnyEPListener(buffer, size, remotePoint);
            }
        }


        /// <summary>
        /// 开始接收数据
        /// </summary>
        public void StartRecv()
        {
            var receiveSocketArgs = BufferManager.Instance.Pop();
            receiveSocketArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, m_Port);
            receiveSocketArgs.Completed += new EventHandler<SocketAsyncEventArgs>(receiveSocketArgs_Completed);
            m_SystemSocket.ReceiveFromAsync(receiveSocketArgs);
        }


        void receiveSocketArgs_Completed(object sender, SocketAsyncEventArgs e)
        {
            e.Completed -= receiveSocketArgs_Completed;
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.ReceiveFrom:
                    if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                    {
                        KCPProxy proxy = GetKcp((IPEndPoint)e.RemoteEndPoint);
                        if (proxy != null)
                        {
                            byteCount += e.BytesTransferred;
                            proxy.DoReceiveInThread(e.Buffer, e.BytesTransferred);
                        }
                    }
                    break;
                default:
                    break;
            }
            e.Completed += receiveSocketArgs_Completed;
            Array.Clear(e.Buffer, 0, e.BytesTransferred);
            m_SystemSocket.ReceiveFromAsync(e);
        }
    }

}