using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FastNet
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
        static void Main(string[] args)
        {
            Console.WriteLine("server cpu:16,32,64,128");
            SetProcessAffinityMask(GetCurrentProcess(), new UIntPtr(Convert.ToUInt32(Console.ReadLine())));
            Console.WriteLine("server");
            ThreadPool.SetMinThreads(50, 50);
            ThreadPool.SetMaxThreads(60, 60);
            int clientnum = 10000;
            BufferManager.Instance.Init(clientnum * 2 * 2);
            Console.WriteLine("enter start port");
            int port = Convert.ToInt32(Console.ReadLine());
            var list = new List<KCPSocket>();
            int count = 0;
            ulong delta = 0;
            for (int i = 0; i < clientnum / 100; i++)
            {
                try
                {
                    var socket = new KCPSocket(1);
                    //socket.EnableReuseAddress();
                    socket.Bind(port++);
                    socket.AddReceiveListener((b, s, r) =>
                    {
                        count++;
                        var str = Encoding.UTF8.GetString(b, 0, s);
                        var time = GetTickCount() - Convert.ToUInt64(str);
                        delta += time;
                        if (count > clientnum * 5)
                        {
                            Console.WriteLine(delta * 1.0f / count);
                            delta = 0;
                            count = 0;
                        }
                        socket.SendTo(b, s, r);
                    });
                    list.Add(socket);
                }
                catch(Exception ex) { Console.WriteLine(ex); }                
            }
            var socket1 = new KCPSocket(1);
            while (true)
            {
                KCPSocket.Update();
                SpinWait.SpinUntil(() => { return false; }, 5);
            }
        }
    }
}
