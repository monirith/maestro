using System;
using System.Collections.Generic;
using Maestro;
using ZeroMQ;
using System.IO;

namespace ZMQExample
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Main Workflow Component");
            var p = new Process(File.OpenRead("flow.bpmn"));
            var processInstance = p.NewProcessInstance();
            processInstance.SetDefaultHandlers();
            processInstance.SetHandler("task", new ZMQTaskHandler());
            var processVar = new Dictionary<string, object>() { { "processVar1", "value" }, { "processVar2", 50 }, { "Task2BPMN", File.ReadAllText("microflow.bpmn") } };
            processInstance.Start(processVar);
            Console.ReadLine();
        }

        private class ZMQTaskHandler : INodeHandler
        {
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                string endpoint = "tcp://127.0.0.1:5555";

                // Create
                using (var context = new ZContext())
                using (var requester = new ZSocket(context, ZSocketType.REQ))
                {
                    // Connect
                    requester.Connect(endpoint);

                    string requestText;
                    //testing for NodeName because retrieving node variables is not yet implemented.
                    if (currentNode.InputParameters.ContainsKey("Task2BPMN") && currentNode.NodeName == "Task_2")
                    {
                        requestText = (string)currentNode.InputParameters["Task2BPMN"];
                        Console.WriteLine("Request {0}", "Remote BPMN start");
                    }
                    else
                    {
                        requestText = "Do some work";
                        Console.WriteLine("Request {0}", requestText);
                    }


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
