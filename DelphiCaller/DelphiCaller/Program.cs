using System;
using System.Runtime.InteropServices;

namespace DelphiCaller
{
    class Program
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int MyCall(int x);

        [DllImport("DelphiLib.dll", CallingConvention = CallingConvention.StdCall)]
        extern static int DoCall(MyCall callback);
        
        static void Main()
        {
            Console.WriteLine("Test program");
            var x = DoCall(val =>
            {
                Console.WriteLine("Inside lambda:");
                Console.WriteLine(val);
                return val * 2;
            });
            Console.WriteLine("Outside lambda: " + x);
        }
    }
}