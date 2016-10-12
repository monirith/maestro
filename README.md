# Maestro BPMN

## Synopsis

Lightweight BPMN Interpreter in C#.Net

## Goal

An architecture with a workflow engine means either all called services return to the engine to continue the current process (SOA) or services have knowledge of other services (microservice type architecture) meaning you have interactions which are managed outside of the scope of the engine.

Primary goal is to have both business processes and service to service interactions defined with BPMN in one place but executed by the relevant service.

Usual BPMN engine are heavy and offer BPMN features which are not always needed. They are not suitable to be deployed along with a service. This project provides a lightweight component leaving the element handling to the target service.

![alt tag](https://github.com/monirith/maestro/blob/master/maestro.png)

Another issue with usual BPMN engines is dealing with process variables. Usual engines use process variables as a holder of inputs and outputs. Nodes map their variables from the process inputs and map their outputs back to the process variables. This introduces racing and state issues, especially when there are multiple branches sharing the same process variables.

[Current work in progress]

A way to deal with this is to consider nodes as functions which take inputs and return outputs avoiding side effects from using shared variables. For multibranch workflows, diverging gateways will spawn clones of the last node outputs and converging gateways will merge or aggregate the sets of outputs.

![alt tag](https://github.com/monirith/maestro/blob/master/variables.png)


## Features

- Start a BPMN workflow with custom handlers for any element.

- Dispatch a BPMN workflow to be executed by another service.

- A default implementation of InclusiveGateway element handling, waiting for all branch to reach before continuing. Know defect: if a branch has a conditional sequence, it is possible that it will never reach the gateway. Possible fix: branch executions resulting in a premature endEvent could notify the next inclusiveGateway.

- A default implementation of ScriptTask element handling running C# code via Roslyn Scripting

- A default implementation of conditional Sequence element handling evaluating a C# condition via Roslyn Scripting.

- InclusiveGateway process variables merge/aggregate (TODO)

- Parameters serialization (TODO)


## Examples

Demo BPMN workflow

![alt tag](https://github.com/monirith/maestro/blob/master/examples/WorkflowConsoleApp/WorkflowConsoleApp/flow.bpmn.png)

### Example 1: Running as local workflow

#### Result
![alt tag](https://github.com/monirith/maestro/blob/master/examples/WorkflowConsoleApp/result_local.png)

#### Local workflow execution
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


### Example 2: Running as SOA/microservice workflow with remote BPMN execution

Using the previous flow. Task 2 is set to send this flow to be executed remotely

![alt tag](https://github.com/monirith/maestro/blob/master/examples/ZMQExample/micro.bpmn.png)

This flow contains a task to execute on a third service

Service1 runs => Service1 execute Task2 which sends a flow for Service2 to execute => Service2 executes the flow, sends work to Service3


#### Result
![alt tag](https://github.com/monirith/maestro/blob/master/examples/ZMQExample/result.png)

#### Initial workflow service
```c#
using Maestro;

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

#### Remote Service receiving a workflow to execute
```c#
using Maestro;

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

#### Remote Service called by the previous service workflow
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
