using FastNet;
using FastNet.TCP;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TCP.IOCPClient
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
        public static int clientnum = 5000;
        public static int msgsend = clientnum * 14;
        static void Main(string[] args)
        {
            Thread.Sleep(2000);
            Console.WriteLine("client cpu:1,2,4,8");
            Console.WriteLine("enter cpu mask:");
            //SetProcessAffinityMask(GetCurrentProcess(), new UIntPtr(Convert.ToUInt32(Console.ReadLine())));
            SetProcessAffinityMask(GetCurrentProcess(), new UIntPtr(6));
            BufferManager.Instance.Init(clientnum * 2);
            ulong delta = 0;
            var count = 0;
            ThreadPool.SetMinThreads(20, 20);
            ThreadPool.SetMaxThreads(30, 30);
            ConcurrentBag<TCPClient> clients = new ConcurrentBag<TCPClient>();
            int connected = 0;
            Console.WriteLine("enter port:");
            //int port = Convert.ToInt32(Console.ReadLine());
            int port = 9200;
            var rand = new Random();
            Parallel.For(0, clientnum, i =>
            {
                try
                {
                    var client = new TCPClient("127.0.0.1", port);
                    client.OnConnected += (s) =>
                    {
                        if (Interlocked.Increment(ref connected) == clientnum)
                        {
                            Console.WriteLine("all connected");
                            new Thread((x) => {
                                while (true)
                                {                                    
                                    foreach (var socket in clients)
                                    {
                                        var tosend = new byte[10];
                                        var data = BitConverter.GetBytes(Environment.TickCount);
                                        Buffer.BlockCopy(data, 0, tosend, 0, data.Length);
                                        socket.Send(data);
                                    }
                                    Thread.Sleep(70);
                                }
                            }).Start();
                        }
                    };
                    client.AddReceiveListener((pkg, r) =>
                    {                        
                       
                        var time = GetTickCount() - BitConverter.ToUInt32(pkg.data,0);
                        delta += time; ;
                        if (Interlocked.Increment(ref count) == msgsend)
                        {                            
                            Console.WriteLine("recv:"+delta * 1.0f / count);
                            delta = 0;
                            Interlocked.Exchange(ref count, 0);
                        }
                    });
                    clients.Add(client);
                    client.Connect();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            });            
            Console.ReadLine();
        }

    }
}
