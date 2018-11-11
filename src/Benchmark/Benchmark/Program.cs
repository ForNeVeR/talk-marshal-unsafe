using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Benchmark
{
    public class Program
    {
        [CoreJob]
        public class StringBenchmark
        {
            [DllImport("StringConsumer.dll", CharSet = CharSet.Ansi)]
            private static extern void PassAnsiString(string str);
            
            [DllImport("StringConsumer.dll", CharSet = CharSet.Unicode)]
            private static extern void PassUnicodeString(string str);
            
            [Params(10, 100, 1000)]
            public int N;

            private string stringToPass;

            [GlobalSetup]
            public void Setup() =>
                stringToPass = new string('x', N);

            [Benchmark]
            public void PassAnsiString() => PassAnsiString(stringToPass);

            [Benchmark]
            public void PassUnicodeString() => PassUnicodeString(stringToPass);
        }

        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<StringBenchmark>();
        }
    }
}