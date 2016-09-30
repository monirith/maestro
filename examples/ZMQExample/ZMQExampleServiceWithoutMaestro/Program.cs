using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZeroMQ;

namespace ZMQExampleServiceWithoutMaestro
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("10 seconds worker server");

            // Create
            using (var context = new ZContext())
            using (var responder = new ZSocket(context, ZSocketType.REP))
            {
                // Bind
                responder.Bind("tcp://*:5556");

                while (true)
                {
                    // Receive
                    using (ZFrame request = responder.ReceiveFrame())
                    {
                        Console.WriteLine("Received {0}", request.ReadString());

                        // Do some work
                        Thread.Sleep(10000);

                        // Send
                        Console.WriteLine("Sending Work Done");
                        responder.Send(new ZFrame("Work Done"));
                    }
                }
            }
        }
    }
}
