using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Test
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
        static void Main(string[] args)
        {
            SetProcessAffinityMask(GetCurrentProcess(), new UIntPtr(8));
            while (true)
            {
                Console.WriteLine("run");
                //Thread.SpinWait(50);
                Thread.Sleep(15);
                //SpinWait.SpinUntil(() => { return false; },50);
            }
        }

        public static int ikcp_encode32u1(byte[] p, int offset, UInt32 l)
        {
            p[0 + offset] = (byte)(l >> 0);
            p[1 + offset] = (byte)(l >> 8);
            p[2 + offset] = (byte)(l >> 16);
            p[3 + offset] = (byte)(l >> 24);
            return 4;
        }

        public static int ikcp_encode32u2(byte[] p, int offset, UInt32 l)
        {
            p = BitConverter.GetBytes(l);
            return 4;
        }
    }
}
