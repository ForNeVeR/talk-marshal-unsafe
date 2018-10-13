using System;
using System.Runtime.InteropServices;

namespace DelphiCaller
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("Test program");
            var x = DelphiCallerLib.Caller.WrapAndDoCall(val =>
            {
                Console.WriteLine("Inside lambda:");
                Console.WriteLine(val);
                return val * 2;
            });
            Console.WriteLine("Outside lambda: " + x);
        }
    }
}