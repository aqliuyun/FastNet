using FastNet;
using FastNet.TCP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TCP.IOCPServer
{
    class Program
    {

        [DllImport("kernel32.dll")]
        static extern uint GetTickCount();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")]
        static extern UIntPtr SetThreadAffinityMask(IntPtr hThread,
         UIntPtr dwThreadAffinityMask);
        [DllImport("kernel32.dll")]
        static extern UIntPtr SetProcessAffinityMask(IntPtr hThread,
         UIntPtr dwThreadAffinityMask);
        static ulong SetCpuID(int id)
        {
            ulong cpuid = 0;
            if (id < 0 || id >= System.Environment.ProcessorCount)
            {
                id = 0;
            }
            cpuid |= 1UL << id;

            return cpuid;
        }

        static int clientnum = 10000;
        static ulong delta;
        static int msg_recvcount = 0;
        static void Main(string[] args)
        {
            Console.WriteLine("server cpu:1,2,4,8");
            Console.WriteLine("server cpu:16,32,64,128");
            Console.WriteLine("enter cpu mask:");
            //SetProcessAffinityMask(GetCurrentProcess(), new UIntPtr(Convert.ToUInt32(Console.ReadLine())));
            SetProcessAffinityMask(GetCurrentProcess(), new UIntPtr(9));
            Console.WriteLine("server");
            BufferManager.Instance.Init(clientnum * 4);

            var server = new TCPServer(clientnum);
            ThreadPool.SetMinThreads(20, 20);
            ThreadPool.SetMaxThreads(30, 30);
            server.AddReceiveListener((pkg, r) =>
            {               
                r.Send(pkg);
                var time = GetTickCount() - BitConverter.ToUInt32(pkg.data, 0);
                delta += time;
                if (Interlocked.Increment(ref msg_recvcount) == server.CurrentConnects * 14)
                {
                    Console.WriteLine(delta * 1.0f / msg_recvcount);
                    delta = 0;
                    Interlocked.Exchange(ref msg_recvcount, 0);
                }
            });
            Console.WriteLine("enter port:");
            //int port = Convert.ToInt32(Console.ReadLine());
            int port = 9200;
            Console.WriteLine(port);
            server.Listen(port);
            //server.Listen(port);
            Console.ReadLine();
        }
    }
}
