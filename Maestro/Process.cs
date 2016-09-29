using Microsoft.CodeAnalysis.CSharp.Scripting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using t = System.Threading.Tasks;

namespace Maestro
{
    public class Process
    {
        public IEnumerable<Property> Properties { get; set; }
        public XElement ProcessXML { get; set; }
        public XNamespace NS { get; set; }
        private Process()
        {
        }

        public Process(string bpmnFile)
        {
            if (!File.Exists(bpmnFile))
                throw new Exception("File " + bpmnFile + " not found");

            XDocument doc = XDocument.Load(bpmnFile);
            NS = @"http://www.omg.org/spec/BPMN/20100524/MODEL";
            ProcessXML = doc.Root.Element(NS + "process");
            Properties = PropertyInitializer(ProcessXML, NS);
        }

        public ProcessInstance NewProcessInstance()
        {
            var current = ProcessXML.Element(NS + "startEvent");
            var node = new ProcessNode(current.Attribute("id").Value, current.Name.LocalName);
            var nodes = BuildNodes(ProcessXML);
            var processInstance = new ProcessInstance(this);
            BuildLinkedNodes(current, ref node, nodes, processInstance);
            processInstance.Id = Guid.NewGuid().ToString();
            processInstance.StartNode = node;
            processInstance.Nodes = nodes;

            return processInstance;
        }

        private IDictionary<string, ProcessNode> BuildNodes(XElement processXML)
        {
            var nodes = processXML.Elements().ToDictionary(e => e.Attribute("id").Value, e => new ProcessNode(e.Attribute("id").Value, e.Name.LocalName));
            nodes.Where(e => e.Value.NodeType == "property").Select(e => e.Key).ToList().ForEach(k => nodes.Remove(k));
            var scripts = processXML.Elements().Elements(NS + "script")
                .Select(s => new { id = s.Parent.Attribute("id").Value, expression = s.Value });
            foreach (var s in scripts) nodes[s.id].Expression = s.expression;

            var conditionExpressions = processXML.Elements().Elements(NS + "conditionExpression")
                .Select(c => new { id = c.Parent.Attribute("id").Value, expression = c.Value });
            foreach (var c in conditionExpressions) nodes[c.id].Expression = c.expression;

            return nodes;
        }

        private Func<XElement, XElement, XNamespace, IEnumerable<XElement>> NextSequences =
            (e, ProcessXML, NS) => ProcessXML.Elements(NS + "sequenceFlow")?
            .Where(s => s.Attribute("sourceRef")?.Value == e.Attribute("id").Value);

        private Func<XElement, XElement, IEnumerable<XElement>> NextElement =
            (s, ProcessXML) => ProcessXML.Elements()
            .Where(e => e.Attribute("id").Value == s.Attribute("targetRef")?.Value);

        private void BuildLinkedNodes(XElement current, ref ProcessNode node, IDictionary<string, ProcessNode> nodes, ProcessInstance processInstance)
        {
            node.ProcessInstance = processInstance;
            var seq = NextSequences(current, ProcessXML, NS);
            var next = (seq.Any() ? seq : NextElement(current, ProcessXML));
            node.NextNodes = new List<ProcessNode>();
            
            foreach (var n in next)
            {
                var nextNode = nodes[n.Attribute("id").Value];
                if (nextNode.PreviousNodes == null) nextNode.PreviousNodes = new List<ProcessNode>();
                if (!nextNode.PreviousNodes.Contains(node)) nextNode.PreviousNodes.Add(node);
                node.NextNodes.Add(nextNode);
                BuildLinkedNodes(n, ref nextNode, nodes, processInstance);
            }
        }

        private IEnumerable<Property> PropertyInitializer(XElement process, XNamespace ns)
        {
            var itemDefinitions = process.Parent.Elements(ns + "itemDefinition");
            var properties = process.Elements(ns + "property").ToList();
            var propertyList = new List<Property>();
            foreach (var property in properties)
            {
                string id = property.Attribute("id").Value;
                string name = property.Attribute("name").Value;
                string itemSubjectRef = property.Attribute("itemSubjectRef").Value;
                string structureRef = itemDefinitions
                    .Where(i => i.Attribute("id").Value == itemSubjectRef)
                    .FirstOrDefault()
                    .Attribute("structureRef")
                    .Value;
                bool isCollection = Convert.ToBoolean(itemDefinitions
                    .Where(i => i.Attribute("id").Value == itemSubjectRef)
                    .FirstOrDefault()
                    .Attribute("isCollection")
                    .Value);
                propertyList.Add(new Property(id, name, structureRef, isCollection));
            }

            return propertyList;
        }
    }

    public class Property
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StructureRef { get; set; }
        public bool IsCollection { get; set; }

        public Property(string id, string name, string structureRef, bool isCollection)
        {
            Id = id;
            Name = name;
            StructureRef = structureRef;
            IsCollection = isCollection;
        }
    }

    public class ProcessInstance
    {
        public string Id { get; set; }
        public Process Process { get; }
        private IDictionary<string, object> inputParameters;
        public IDictionary<string, object> InputParameters
        {
            get
            {
                return inputParameters;
            }

            set
            {
                if (ValidParameters(value))
                    inputParameters = value;
                else
                    throw new Exception("Parameter type does not match process definition");
            }
        }
        public IDictionary<string, object> OutputParameters { get; set; }
        public ProcessNode StartNode { get; internal set; }
        public IDictionary<string, ProcessNode> Nodes { get; set; }
        public IDictionary<string, INodeHandler> nodeHandlers;
        public IDictionary<string, INodeHandler> NodeHandlers
        {
            get
            {
                return nodeHandlers;
            }

            set
            {
                if (ValidHandlers(value))
                    nodeHandlers = value;
                else
                    throw new Exception("Unhandled node type");
            }
        }

        public ProcessInstance(Process process)
        {
            Process = process;
        }

        public void Start()
        {
            StartNode.Execute(StartNode, null);
        }

        public void SetDefaultHandlers()
        {
            var defaultNodeHandlers = new Dictionary<string, INodeHandler>()
            {
                { "startEvent", new DefaultStartHandler()},
                { "endEvent", new DefaultEndHandler()},
                { "task", new DefaultTaskHandler()},
                { "sequenceFlow", new DefaultSequenceHandler()},
                { "businessRuleTask", new DefaultBusinessRuleHandler()},
                { "exclusiveGateway", new DefaultExclusiveGatewayHandler()},
                { "inclusiveGateway", new DefaultInclusiveGatewayHandler()},
                { "scriptTask", new DefaultScriptTaskHandler()}
            };

            if (Nodes.All(t => defaultNodeHandlers.ContainsKey(t.Value.NodeType)))
            {
                nodeHandlers = new Dictionary<string, INodeHandler>();
                foreach (string n in Nodes.Values.Select(n => n.NodeType).Distinct())
                {
                    nodeHandlers.Add(n, defaultNodeHandlers[n]);
                }
            }
            else
                throw new Exception("Process contains an unknown node type");
        }

        public void SetHandler(string nodeType, INodeHandler nodeHandler)
        {
            if (nodeHandlers == null)
                nodeHandlers = new Dictionary<string, INodeHandler>();

            if (nodeHandlers.ContainsKey(nodeType))
                nodeHandlers[nodeType] = nodeHandler;
            else
                nodeHandlers.Add(nodeType, nodeHandler);
        }

        private bool ValidHandlers(IDictionary<string, INodeHandler> handlers)
        {
            var nodeTypes = Nodes.Values.Select(n => n.NodeType).Distinct();
            return nodeTypes.All(t => handlers.Keys.Contains(t));
        }

        private bool ValidParameters(IDictionary<string, object> parameters)
        {
            var propertyMap = Process.Properties.ToDictionary(p => p.Name, p => p.StructureRef);
            return parameters.All(p => p.Value.GetType().Name.ToLower() == propertyMap[p.Key].ToLower());
        }

        public void Start(IDictionary<string, object> parameters)
        {
            InputParameters = parameters;
            StartNode.InputParameters = parameters;
            Start();
        }

        internal void SetOutputParameters(IDictionary<string, object> result)
        {
            OutputParameters = result;
        }
    }


    public interface INodeHandler
    {
        void Execute(ProcessNode currentNode, ProcessNode previousNode);
    }

    public class ProcessNode
    {
        public string NodeName { get; set; }
        public string NodeType { get; set; }
        public ProcessInstance ProcessInstance { get; set; }
        public IDictionary<string, object> InputParameters { get; set; }
        public IDictionary<string, object> OutputParameters { get; set; }
        public INodeHandler NodeHandler { get; set; }
        public ICollection<ProcessNode> NextNodes { get; set; }
        public ICollection<ProcessNode> PreviousNodes { get; set; }
        private t.Task Task { get; set; }
        public string Expression { get; set; }

        public ProcessNode()
        {
        }

        public ProcessNode(INodeHandler nodeHandler)
        {
            NodeHandler = nodeHandler;
        }

        public ProcessNode(string name, string type)
        {
            NodeName = name;
            NodeType = type;
        }

        public void Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            NodeHandler = ProcessInstance.NodeHandlers[NodeType];
            //NodeHandler.Execute(processNode, Parameters);
            if (processNode.InputParameters == null) processNode.InputParameters = ProcessInstance.InputParameters;
            Task = new t.Task(() => NodeHandler.Execute(processNode, previousNode));
            Task.Start();
        }
        public void Done()
        {
            //TODO: Multicontext
            //Currently ProcessInstance hold a single parameters context
            //It can be modified by multiple branches of the flow possibly causing unexpected results

            //ProcessInstance.SetOutputParameters(Task.Result);
            foreach (var node in NextNodes)
            {
                node.InputParameters = OutputParameters;
                node.Execute(node, this);
            }
        }
    }

    internal class DefaultTaskHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing Task");
            processNode.Done();
        }
    }

    internal class DefaultStartHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing Start");
            processNode.Done();
        }
    }

    internal class DefaultExclusiveGatewayHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName);
            processNode.Done();
        }
    }

    internal class DefaultBusinessRuleHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing BusinessRule");
            processNode.Done();
        }
    }
    internal class DefaultSequenceHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing Sequence");
            bool result = true;
            if (processNode.Expression != null)
            {
                Console.WriteLine(processNode.NodeName + " Conditional Sequence");
                var globals = new Globals(processNode.InputParameters);
                try
                {
                    result = CSharpScript.EvaluateAsync<bool>(processNode.Expression, globals: globals).Result;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }

            if (result)
                processNode.Done();
        }
    }

    internal class DefaultEndHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing End");
            processNode.ProcessInstance.SetOutputParameters(processNode.OutputParameters);
            processNode.Done();
        }
    }

    internal class DefaultScriptTaskHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing Script");

            if (processNode.Expression != null)
            {
                var globals = new Globals(processNode.InputParameters);
                try
                {
                    processNode.OutputParameters = CSharpScript.EvaluateAsync<IDictionary<string, object>>(processNode.Expression, globals: globals).Result;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            processNode.Done();
        }
    }

    public class Globals
    {
        public IDictionary<string, object> globals;
        public Globals(IDictionary<string, object> parameters)
        {
            globals = parameters;
        }
    }
    internal class DefaultInclusiveGatewayHandler : INodeHandler
    {
        ConcurrentDictionary<ProcessNode, ICollection<ProcessNode>> sequenceWait = new ConcurrentDictionary<ProcessNode, ICollection<ProcessNode>>();

        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName);
            sequenceWait.GetOrAdd(processNode, new List<ProcessNode>(processNode.PreviousNodes));
            lock (sequenceWait[processNode])
            {
                sequenceWait[processNode].Remove(previousNode);
            }
            if (sequenceWait[processNode].Count == 0)
            {
                processNode.Done();
            }
        }
    }
    
}


