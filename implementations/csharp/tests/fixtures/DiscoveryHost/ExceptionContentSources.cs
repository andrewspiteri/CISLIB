using System.Collections;

namespace CIS.Tests.DiscoveryFixture;

internal sealed record ExceptionContentSource(Exception Root, IReadOnlyList<ExceptionContentProbe> Probes);

internal static class ExceptionContentSources
{
    internal const string PrivateSentinel = "CIS_EXCEPTION_PRIVATE_CONTENT";
    internal static string[] MarkerCases => ["complete", "message", "stack", "both", "child-message", "child-stack", "child-both", "root-child", "depth", "depth-sibling", "nodes", "mixed-nodes", "nodes-last-branch", "combined", "empty-stack", "null-message"];
    internal static IEnumerable<string> Cases => MarkerCases.Concat(["data-single", "data-inner", "data-aggregate", "retention"]);

    internal static ExceptionContentSource Create(string id)
    {
        var probes = new List<ExceptionContentProbe>();
        Exception root = id switch
        {
            "depth" => Chain(5, probes),
            "depth-sibling" => Aggregate("root", [Chain(5, probes), Leaf("tail", probes)], probes),
            "nodes" => Aggregate("root", Enumerable.Range(0, 17).Select(index => Leaf($"n{index}", probes)), probes),
            "mixed-nodes" => Aggregate("root", [Branch("a", 8, probes), Branch("b", 8, probes), Leaf("tail", probes)], probes),
            "nodes-last-branch" => Aggregate("root", [Branch("a", 8, probes), Branch("b", 8, probes)], probes),
            "combined" => Aggregate("root", [Chain(4, probes), Branch("b", 13, probes), Leaf("tail", probes)], probes),
            "child-message" or "child-stack" or "child-both" or "root-child" => Leaf("root", probes, Leaf("child", probes)),
            _ => Leaf("root", probes)
        };
        foreach (ExceptionContentProbe probe in probes)
        {
            if (((id is "message" or "both" or "root-child" or "combined") && probe.Label == "root") ||
                ((id is "child-message" or "child-both" or "root-child") && probe.Label == "child") ||
                (id == "combined" && probe.Label == "b5"))
            {
                probe.Text = new string('m', 4097);
            }
            if (((id is "stack" or "both" or "combined") && probe.Label == "root") ||
                ((id is "child-stack" or "child-both") && probe.Label == "child") ||
                (id == "combined" && probe.Label == "b5"))
            {
                probe.Trace = new string('s', 16385);
            }
        }
        if (id == "empty-stack")
        {
            probes[0].Trace = string.Empty;
        }
        if (id == "null-message")
        {
            probes[0].Text = null;
        }
        return new(root, probes);
    }

    internal static Exception CreateBcl(string kind)
    {
        Exception root = kind switch
        {
            "single" => new InvalidOperationException("single"),
            "inner" => new InvalidOperationException("outer", new ArgumentException("inner")),
            "aggregate" => new AggregateException("aggregate", new Exception("left"), new Exception("right")),
            _ => throw new InvalidDataException("Unknown native data shape.")
        };
        foreach (Exception node in BclNodes(root))
        {
            FillData(node.Data);
        }
        return root;
    }

    internal static IEnumerable<Exception> BclNodes(Exception root)
    {
        yield return root;
        IEnumerable<Exception> children;
        if (root is AggregateException aggregate)
        {
            children = aggregate.InnerExceptions;
        }
        else
        {
            children = root.InnerException is { } inner ? [inner] : [];
        }
        foreach (Exception child in children)
        {
            foreach (Exception descendant in BclNodes(child))
            {
                yield return descendant;
            }
        }
    }

    internal static void FillData(IDictionary data)
    {
        data["body"] = PrivateSentinel;
        data["headers"] = new Dictionary<string, object> { ["Authorization"] = PrivateSentinel };
        data["environment"] = new Dictionary<string, object> { ["PRIVATE_TOKEN"] = PrivateSentinel };
        data["attachment"] = new ExceptionPrivatePayload();
        data["cycle"] = data;
    }

    internal static void TouchForbidden(Exception root, string member)
    {
        _ = member switch
        {
            "data" => root.Data,
            "source" => root.Source,
            "help" => root.HelpLink,
            "format" => root.ToString(),
            "body" => ((IExceptionMetadataTrap)root).Body,
            "headers" => ((IExceptionMetadataTrap)root).Headers,
            "environment" => ((IExceptionMetadataTrap)root).Environment,
            _ => throw new InvalidDataException("Unknown forbidden getter control.")
        };
    }

    private static Exception Chain(int depth, List<ExceptionContentProbe> probes)
    {
        Exception? child = null;
        for (int index = depth; index > 0; index--)
        {
            child = Leaf($"d{index}", probes, child);
        }
        return child!;
    }

    private static Exception Branch(string label, int count, List<ExceptionContentProbe> probes) =>
        Aggregate(label, Enumerable.Range(0, count).Select(index => Leaf($"{label}{index}", probes)), probes);

    private static Exception Leaf(string label, List<ExceptionContentProbe> probes, Exception? inner = null)
    {
        var probe = new ExceptionContentProbe(label);
        probes.Add(probe);
        return new TrapException(probe, inner);
    }

    private static Exception Aggregate(string label, IEnumerable<Exception> children, List<ExceptionContentProbe> probes)
    {
        var probe = new ExceptionContentProbe(label);
        probes.Add(probe);
        return new TrapAggregate(probe, children);
    }

    private sealed class TrapException : Exception, IExceptionMetadataTrap
    {
        private readonly ExceptionContentProbe probe;
        internal TrapException(ExceptionContentProbe probe, Exception? inner) : base(string.Empty, inner)
        {
            this.probe = probe;
            probe.RawData = base.Data;
            FillData(probe.RawData);
        }
        public override string Message => probe.Message();
        public override string? StackTrace => probe.Stack();
        public override IDictionary Data => probe.Deny<IDictionary>();
        public override string? Source { get => probe.Deny<string>(); set => throw new NotSupportedException(); }
        public override string? HelpLink { get => probe.Deny<string>(); set => throw new NotSupportedException(); }
        public override string ToString() => probe.Deny<string>();
        public object Body => probe.Deny<object>();
        public object Headers => probe.Deny<object>();
        public object Environment => probe.Deny<object>();
    }

    private sealed class TrapAggregate : AggregateException, IExceptionMetadataTrap
    {
        private readonly ExceptionContentProbe probe;
        internal TrapAggregate(ExceptionContentProbe probe, IEnumerable<Exception> children) : base(string.Empty, children)
        {
            this.probe = probe;
            probe.RawData = base.Data;
            FillData(probe.RawData);
        }
        public override string Message => probe.Message();
        public override string? StackTrace => probe.Stack();
        public override IDictionary Data => probe.Deny<IDictionary>();
        public override string? Source { get => probe.Deny<string>(); set => throw new NotSupportedException(); }
        public override string? HelpLink { get => probe.Deny<string>(); set => throw new NotSupportedException(); }
        public override string ToString() => probe.Deny<string>();
        public object Body => probe.Deny<object>();
        public object Headers => probe.Deny<object>();
        public object Environment => probe.Deny<object>();
    }
}
