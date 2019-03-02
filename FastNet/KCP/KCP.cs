using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
namespace FastNet
{
    public abstract class KCP
    {
        protected byte[] buffer;
        public Action<KCP> OnDeadLink;
        public abstract void Update(long currentTimeMS);
        public abstract long Check(long currentTimeMS);
        public abstract int Recv(byte[] recvBuffer);
        public abstract int PeekSize();
        public abstract int Input(byte[] recvBufferRaw);
        public abstract int Send(byte[] buff);
        public abstract void Dispose();
        public abstract int SetMtu(Int32 mtu_);
        public abstract int Interval(Int32 interval_);
        public abstract int NoDelay(int nodelay_, int interval_, int resend_, int nc_);
        public abstract int WndSize(int sndwnd, int rcvwnd);
        public abstract int WaitSnd();
        public abstract void Reset();

        public byte[] OutputBuffer { get { return buffer; } set{ buffer = value; } }
    }    
}
