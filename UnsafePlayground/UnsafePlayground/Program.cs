using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UnsafePlayground
{
    unsafe class Native
    {
        public static extern void Call(int* p);
        public static extern void Call(IntPtr p);
    }

    class MyHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public MyHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            /* CloseHandle(this.handle); */
            return true;
        }
    }

    public class MyMarshaller : ICustomMarshaler
    {
        public static ICustomMarshaler GetInstance(string cookie) => new MyMarshaller();

        public void CleanUpManagedData(object ManagedObj)
        {
        }

        public void CleanUpNativeData(IntPtr pNativeData)
        {
        }

        public int GetNativeDataSize()
        {
            throw new NotImplementedException();
        }

        public IntPtr MarshalManagedToNative(object ManagedObj)
        {
            throw new NotImplementedException();
        }

        public object MarshalNativeToManaged(IntPtr pNativeData)
        {
            throw new NotImplementedException();
        }
    }
    
    class Program
    {
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public class DBVariant1 { public byte type; public IntPtr Pointer; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class DBVariant2 { public byte type; public IntPtr Pointer; }
        
        unsafe struct X {
            fixed int Array[30];
        }
        
        struct Foo { public int x, y; }
        
        unsafe void Test1()
        {
            int[] x = new int[10];
            fixed (int* ptr = x) {
                Native.Call(ptr);
            }
        }
        
        unsafe static void Main(string[] args)
        {
            var f = new Foo();
            var offset1 = (byte*) &f - (byte*) &f.x;
            
            Console.WriteLine(Marshal.SizeOf<DBVariant1>());
            Console.WriteLine(Marshal.SizeOf<DBVariant2>());
        }
    }
}