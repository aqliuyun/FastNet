using FastNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KCPClient
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
            try
            {
                Console.WriteLine("client cpu:1,2,4,8,15");
                SetProcessAffinityMask(GetCurrentProcess(), new UIntPtr(Convert.ToUInt32(Console.ReadLine())));
                Console.WriteLine("client:" + GetTickCount().ToString());
                Console.WriteLine("enter start port");
                int port = Convert.ToInt32(Console.ReadLine());
                int clientnum = 5000;
                int[] ports = new int[clientnum / 100];
                Console.WriteLine($"ports:{ports.Length}");
                for (int i = 0; i < ports.Length; i++)
                {
                    ports[i] = port+i;                    
                }                
                BufferManager.Instance.Init(clientnum * 10);
                var list = new List<KCPSocket>();
                ulong delta = 0;
                var count = 0;
                var last = GetTickCount();
                for (int i = 0; i < clientnum; i++)
                {
                    KCPSocket socket = new KCPSocket(1);
                    socket.Bind(0);
                    socket.AddReceiveListener((b, s, r) =>
                    {
                        try
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
                        }
                        catch
                        {
                            Console.WriteLine(GetTickCount());
                            Console.WriteLine(Encoding.UTF8.GetString(b, 0, s));
                        }
                    });
                    list.Add(socket);
                    Console.WriteLine(DateTime.Now.ToString("HH:mm:ss =>") + "init " + i);
                }
                Console.WriteLine("enter any key to start send");
                Console.ReadLine();
                new Thread(() =>
                {
                    while (true)
                    {
                        KCPSocket.Update();
                        SpinWait.SpinUntil(() => { return false; }, 5);
                    }
                }).Start();
                var rand = new Random(Environment.TickCount);
                var c = 1;
                while (true)
                {
                    try
                    {
                        var buffer = Encoding.UTF8.GetBytes(GetTickCount().ToString());
                        foreach (var socket in list)
                        {
                            socket.SendTo(buffer, buffer.Length, new IPEndPoint(IPAddress.Parse("127.0.0.1"), ports[rand.Next(ports.Length)]));
                        }
                    }
                    catch { }
                    SpinWait.SpinUntil(() => { return false; }, 50);
                }
            }
            catch { }
        }
    }
}
