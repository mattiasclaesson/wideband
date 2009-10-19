using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WidebandSupport;

namespace WidebandReader
{
    class Program
    {

        static void Main(string[] args)
        {

            WidebandFactory wbFactory = null;
            IWidebandReader reader = null;
            if (args.Length == 3)
            {
                wbFactory = new WidebandFactory(args[0], args[1], Boolean.Parse(args[2]));
            }
            else
            {
                wbFactory = new WidebandFactory("LM2", "COM4", true);
            }

            reader = wbFactory.CreateInstance();

            reader.Start();

            while (true)
            {
                Console.WriteLine("{0:o},{1:F2}", DateTime.Now, reader.LatestReading);
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }

        }

    }

}
