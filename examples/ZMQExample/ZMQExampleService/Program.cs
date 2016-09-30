using Maestro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZeroMQ;

namespace ZMQExampleService
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("BPMN worker server");

            // Create
            using (var context = new ZContext())
            using (var responder = new ZSocket(context, ZSocketType.REP))
            {
                // Bind
                responder.Bind("tcp://*:5555");

                while (true)
                {
                    // Receive
                    using (ZFrame request = responder.ReceiveFrame())
                    {
                        var flow = request.ReadString();
                        if (flow.StartsWith("<?xml")) //Just for demo purposes
                        {
                            var p = new Process(new MemoryStream(Encoding.UTF8.GetBytes(flow)));
                            var processInstance = p.NewProcessInstance();
                            processInstance.SetDefaultHandlers();
                            processInstance.SetHandler("task", new ZMQTaskHandler());
                            //processInstance.SetHandler("endEvent", new ZMQEndHandler(responder));
                            processInstance.Start(new Dictionary<string, object>());
                        }
                        responder.Send(new ZFrame("Work Done"));
                    }
                }
            }
        }

        private class ZMQEndHandler : INodeHandler
        {
            ZSocket Responder;
            public ZMQEndHandler(ZSocket responder)
            {
                Responder = responder;
            }
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                Responder.Send(new ZFrame("Work Done"));
                currentNode.Done();
            }
        }

        private class ZMQTaskHandler : INodeHandler
        {
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                string endpoint = "tcp://127.0.0.1:5556";

                // Create
                using (var context = new ZContext())
                using (var requester = new ZSocket(context, ZSocketType.REQ))
                {
                    // Connect
                    requester.Connect(endpoint);

                    string requestText = "Do some work";
                    Console.WriteLine("Request {0}", requestText);

                    // Send
                    requester.Send(new ZFrame(requestText));

                    // Receive
                    using (ZFrame reply = requester.ReceiveFrame())
                    {
                        Console.WriteLine("Received: {0} ", reply.ReadString());
                    }

                    currentNode.Done();
                }
            }
        }
    }
}
