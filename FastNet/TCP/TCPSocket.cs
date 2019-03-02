using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FastNet.TCP
{
    public delegate void TCPReceiveListener(Package pkg, TCPSocket socket);

    public class TCPSocket
    {
        private static int _id = 0;
        public int Id { get; private set; }
        protected Socket connectSocket;
        protected TCPReceiveListener m_AnyListener;
        protected ConcurrentQueue<Package> m_sendQueue = new ConcurrentQueue<Package>();
        protected ConcurrentQueue<Package> m_recvQueue = new ConcurrentQueue<Package>();
        protected long m_sending;
        protected long m_sendingActive;
        protected long m_needSending;
        #region events
        public Action<TCPSocket, SocketAsyncEventArgs> OnAcceptDone;
        public Action<TCPSocket, SocketAsyncEventArgs> OnSendDone;
        public Action<TCPSocket, SocketAsyncEventArgs> OnRecvDone;
        #endregion
        public TCPSocket()
        {
            Id = _id++;
        }

        public void AddReceiveListener(TCPReceiveListener listener)
        {
            m_AnyListener += listener;
        }

        public void RemoveReceiveListener(TCPReceiveListener listener)
        {
            m_AnyListener -= listener;
        }

        protected void OnIOCompleted(object sender, SocketAsyncEventArgs e)
        {
            e.Completed -= OnIOCompleted;
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    this.ReceiveDone(e);
                    break;
                case SocketAsyncOperation.Send:
                    this.SendDone(e);
                    break;
                case SocketAsyncOperation.Accept:
                    this.AcceptDone(e);
                    this.DoAccept();
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        #region accept

        protected long acceptStatus;
        public void DoAccept()
        {
            SocketAsyncEventArgs e = null;           
            Interlocked.Increment(ref acceptStatus);
            e = BufferManager.Instance.Pop();
            e.Completed += OnIOCompleted;
            if (!connectSocket.AcceptAsync(e))
            {
                this.OnIOCompleted(this, e);
            }
        }


        /// <summary>
        /// accept 操作完成时回调函数
        /// </summary>
        /// <param name="sender">Object who raised the event.</param>
        /// <param name="e">SocketAsyncEventArg associated with the completed accept operation.</param>
        protected void AcceptDone(SocketAsyncEventArgs e)
        {
            if (e.LastOperation == SocketAsyncOperation.Accept && e.SocketError == SocketError.Success)
            {
                if (this.OnAcceptDone != null)
                {
                    this.OnAcceptDone(this, e);
                }
                e.AcceptSocket = null;
                BufferManager.Instance.Push(e);
            }
            else if (e.SocketError != SocketError.Success)
            {
                e.AcceptSocket = null;
                Console.WriteLine(e.SocketError);
                this.CloseSocket(this.connectSocket, e);
            }
            Interlocked.Decrement(ref acceptStatus);
        }
        #endregion

        #region recv
        public void StartReceive(Socket socket)
        {
            this.connectSocket = socket;
            this.DoRecv();
        }

        private Package decodingPackage;
        private int incompleteSize;
        private byte[] incompleteBuff;

        protected void ReceiveDone(SocketAsyncEventArgs e)
        {
            if (e.LastOperation == SocketAsyncOperation.Receive && e.SocketError == SocketError.Success)
            {
                try
                {
                    var transferred = e.BytesTransferred;
                    if (transferred > 0)
                    {
                        int size = 0;
                        int offset = 0;
                        if (decodingPackage != null && incompleteSize > 0)
                        {
                            if (incompleteSize <= transferred)
                            {
                                Buffer.BlockCopy(e.Buffer, 0, decodingPackage.data, decodingPackage.size - incompleteSize, incompleteSize);
                                offset += incompleteSize;
                                m_recvQueue.Enqueue(decodingPackage);
                                decodingPackage = null;
                                incompleteSize = 0;
                            }
                            else
                            {
                                Buffer.BlockCopy(e.Buffer, 0, decodingPackage.data, decodingPackage.size - incompleteSize, transferred - offset);
                                incompleteSize = (incompleteSize - (transferred - offset));
                                offset = transferred; 
                            }
                        }
                        else if (decodingPackage == null && incompleteBuff != null)
                        {
                            var sizeBuffer = new byte[4];
                            Buffer.BlockCopy(incompleteBuff, 0, sizeBuffer, 0, incompleteBuff.Length);
                            Buffer.BlockCopy(e.Buffer, 0, sizeBuffer, incompleteBuff.Length, 4 - incompleteBuff.Length);
                            offset += 4 - incompleteBuff.Length;
                            incompleteBuff = null;
                            size = BitConverter.ToInt32(sizeBuffer, 0);
                            byte[] dst = new byte[size];
                            var pkg = new Package() { size = size, data = dst };
                            if (size > 0)
                            {
                                if (transferred - offset >= size)
                                {
                                    Buffer.BlockCopy(e.Buffer, offset, dst, 0, size);
                                    decodingPackage = null;
                                    m_recvQueue.Enqueue(pkg);
                                }
                                else if (transferred - offset > 0 && transferred - offset < size)
                                {
                                    incompleteSize = size - (transferred - offset);
                                    Buffer.BlockCopy(e.Buffer, offset, dst, 0, transferred - offset);
                                    decodingPackage = pkg;
                                }
                                else
                                {
                                    decodingPackage = pkg;
                                    incompleteSize = size - (transferred - offset);
                                }
                            }
                            else
                            {
                                if (size < 0)
                                {
                                    return;
                                }
                            }
                        }
                        for (; offset < transferred; offset += size)
                        {
                            if (offset > transferred - 4)
                            {
                                incompleteBuff = new byte[transferred - offset];
                                Buffer.BlockCopy(e.Buffer, offset, incompleteBuff, 0, transferred - offset);
                                break;
                            }
                            size = BitConverter.ToInt32(e.Buffer, offset);
                            offset += 4;
                            byte[] dst = new byte[size];
                            var pkg = new Package() { size = size, data = dst };
                            if (size > 0)
                            {
                                if (transferred - offset >= size)
                                {
                                    Buffer.BlockCopy(e.Buffer, offset, dst, 0, size);
                                    decodingPackage = null;
                                    m_recvQueue.Enqueue(pkg);
                                }
                                else if (transferred - offset > 0 && transferred - offset < size)
                                {
                                    incompleteSize = size - (transferred - offset);
                                    Buffer.BlockCopy(e.Buffer, offset, dst, 0, transferred - offset);
                                    decodingPackage = pkg;
                                }
                                else
                                {
                                    decodingPackage = pkg;
                                    incompleteSize = size - (transferred - offset);
                                }
                            }
                            else
                            {
                                if (size < 0)
                                {
                                    break;
                                }
                                //skip empty package
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("no data");
                    }
                    if (OnRecvDone != null)
                    {
                        this.OnRecvDone(this, e);
                    }
                    Task.Factory.StartNew(()=> { this.DispatchPackage(); });
                    e.Completed += OnIOCompleted;
                    if (!this.connectSocket.ReceiveAsync(e))
                    {
                        this.OnIOCompleted(this, e);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            else if (e.SocketError != SocketError.Success)
            {
                Console.WriteLine(e.SocketError);
                this.CloseSocket(this.connectSocket, e);
            }
        }

        public void DoRecv()
        {
            if (this.connectSocket.Connected)
            {
                var e = BufferManager.Instance.Pop();
                e.Completed += OnIOCompleted;
                if (!this.connectSocket.ReceiveAsync(e))
                {
                    OnIOCompleted(this.connectSocket, e);
                }

            }
        }

        public bool DispatchPackage()
        {
            if (m_recvQueue.IsEmpty) return false;
            while (m_recvQueue.TryDequeue(out Package pkg))
            {
                try
                {
                    if (m_AnyListener != null)
                    {
                        m_AnyListener(pkg, this);
                    }
                }
                catch (Exception ex) { Console.WriteLine("deal pkg error:" + ex.Message); }
            }
            return true;
        }
        #endregion

        #region Send
        public void Send(byte[] datas)
        {            
            this.Send(new Package() { data = datas, size = datas.Length });            
        }

        public void Send(Package pkg)
        {
            m_sendQueue.Enqueue(pkg);
            Interlocked.Increment(ref m_sending);
            this.m_needSending += (pkg.size + 4);
            if (Environment.TickCount - m_sendingActive >= 15 || this.m_needSending >= BufferManager.Instance.BufferSize)
            {
                this.m_needSending = 0;
                DoSend();
            }
        }

        public bool HasSend()
        {
            return m_sending > 0;
        }

        public void DoSend()
        {
            if (Interlocked.Read(ref m_sending) > 0)
            {
                if (Interlocked.Increment(ref m_sendingStatus) == 1)
                {
                    m_sendingActive = Environment.TickCount;
                    var e = BufferManager.Instance.Pop();
                    Sending(e);
                }
            }
        }

        private int lastSendSize = 0;
        private Package lastSend = null;
        private long m_sendingStatus = 0;
        protected void Sending(SocketAsyncEventArgs e)
        {
            int offset = 0;
            if (lastSend != null)
            {
                if(lastSendSize == 0)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(lastSend.size), 0, e.Buffer, offset, 4);
                    offset += 4;
                }
                if (lastSend.size - lastSendSize <= BufferManager.Instance.BufferSize - offset)
                {                    
                    Buffer.BlockCopy(lastSend.data, 0, e.Buffer, offset, lastSend.size - lastSendSize);
                    offset = lastSend.size - lastSendSize;
                    lastSendSize = 0;
                    lastSend = null;
                    Interlocked.Decrement(ref m_sending);
                }
                else
                {
                    Buffer.BlockCopy(lastSend.data, 0, e.Buffer, offset, BufferManager.Instance.BufferSize - offset);
                    lastSendSize += BufferManager.Instance.BufferSize - offset;
                    offset = BufferManager.Instance.BufferSize;
                    Interlocked.Exchange(ref m_sendingStatus, 0);
                    e.SetBuffer(0, offset);                    
                    e.Completed += OnIOCompleted;
                    if (!this.connectSocket.Connected) { this.CloseSocket(this.connectSocket, e); return; }
                    if (!this.connectSocket.SendAsync(e))
                    {
                        OnIOCompleted(this.connectSocket, e);
                    }
                    return;
                }
            }
            while (m_sendQueue.TryDequeue(out Package pkg))
            {
                if (offset + 4 + pkg.size > BufferManager.Instance.BufferSize)
                {
                    lastSend = pkg;
                    break;
                }
                Buffer.BlockCopy(BitConverter.GetBytes(pkg.size), 0, e.Buffer, offset, 4);
                offset += 4;
                Buffer.BlockCopy(pkg.data, 0, e.Buffer, offset, pkg.size);
                offset += pkg.size;
                Interlocked.Decrement(ref m_sending);
            }
            Interlocked.Exchange(ref m_sendingStatus, 0);
            if (offset > 0)
            {
                e.SetBuffer(0, offset);
                if (!this.connectSocket.Connected) { this.CloseSocket(this.connectSocket, e); return; }
                e.Completed += OnIOCompleted;
                if (!this.connectSocket.SendAsync(e))
                {
                    OnIOCompleted(this.connectSocket, e);
                }
            }
            else
            {
                BufferManager.Instance.Push(e);
            }
        }

        /// <summary>
        /// 发送完成时处理函数
        /// </summary>
        /// <param name="e">与发送完成操作相关联的SocketAsyncEventArg对象</param>
        protected void SendDone(SocketAsyncEventArgs e)
        {
            if (e.LastOperation == SocketAsyncOperation.Send && e.SocketError == SocketError.Success)
            {
                if (this.OnSendDone != null)
                {
                    this.OnSendDone(this, e);
                }
                BufferManager.Instance.Push(e);
            }
            else if (e.SocketError != SocketError.Success)
            {
                Console.WriteLine(e.SocketError);
                this.CloseSocket(this.connectSocket, e);
            }
        }
        #endregion

        protected virtual void CloseSocket(Socket s, SocketAsyncEventArgs e)
        {
            BufferManager.Instance.Push(e);
            try
            {
                s.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                // Throw if client has closed, so it is not necessary to catch.
            }
            finally
            {
                s.Close();
            }
        }

    }
}
