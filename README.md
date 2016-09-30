# Maestro BPMN Engine

## Synopsis

Simple BPMN Engine in C#.NET

## Goal

Having a usual BPMN Engine means either services returns to the running process or service to service interactions are managed outside of the scope of the engine.

The goal is to have both business processes and service to service interactions at the same place but executed where it's relevant.
![alt tag](https://github.com/monirith/maestro/blob/master/maestro.png)

## Examples

### Local workflow

```c#
using Maestro;

namespace WorkflowConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var p = new Process("flow.bpmn");
            var processInstance = p.NewProcessInstance();
            processInstance.SetDefaultHandlers();
            processInstance.SetHandler("task", new MyTaskHandler());
            processInstance.SetHandler("startEvent", new MyStartHandler());

            var processVar = new Dictionary<string, object>() { { "processVar1", "value" }, { "processVar2", 50 } };
            processInstance.Start(processVar);
            Console.ReadLine();
        }

        private class MyStartHandler : INodeHandler
        {
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                Console.WriteLine("Custom Start Handler");
                Console.WriteLine(currentNode.NodeName);
                currentNode.Done();
            }
        }

        private class MyTaskHandler : INodeHandler
        {
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                Console.WriteLine("Custom Task Handler");
                Console.WriteLine(currentNode.NodeName);
                currentNode.Done();
            }
        }
    }
}

```

### SOA/microservice workflow with remote BPMN execution

#### Result
![alt tag](https://github.com/monirith/maestro/blob/master/examples/ZMQExample/result.png)

#### Main Workflow service
```c#
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

                    requester.Send(new ZFrame(requestText));
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
```

#### Remote Service with Workflow Execution
```c#
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
                            processInstance.Start(new Dictionary<string, object>());
                        }
                        responder.Send(new ZFrame("Work Done"));
                    }
                }
            }
        }

        private class ZMQTaskHandler : INodeHandler
        {
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                string endpoint = "tcp://127.0.0.1:5556";
                using (var context = new ZContext())
                using (var requester = new ZSocket(context, ZSocketType.REQ))
                {
                    // Connect
                    requester.Connect(endpoint);
                    string requestText = "Do some work";
                    Console.WriteLine("Request {0}", requestText);
                    requester.Send(new ZFrame(requestText));

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
```

##### Remote Service
```c#
namespace ZMQExampleServiceWithoutMaestro
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("10 seconds worker server");
            using (var context = new ZContext())
            using (var responder = new ZSocket(context, ZSocketType.REP))
            {
                responder.Bind("tcp://*:5556");
                while (true)
                {
                    // Receive
                    using (ZFrame request = responder.ReceiveFrame())
                    {
                        Console.WriteLine("Received {0}", request.ReadString());

                        // Do some work
                        Thread.Sleep(10000);

                        Console.WriteLine("Sending Work Done");
                        responder.Send(new ZFrame("Work Done"));
                    }
                }
            }
        }
    }
}
```
