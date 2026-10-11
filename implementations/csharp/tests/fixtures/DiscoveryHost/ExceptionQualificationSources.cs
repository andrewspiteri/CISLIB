using System.Collections;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal sealed record ExceptionQualificationSource(Exception? Root, IReadOnlyList<ExceptionQualificationProbe> Probes);

internal static class ExceptionQualificationSources
{
    internal const string PrivateSentinel = "CIS_PRIVATE_QUALIFICATION_DATA";

    internal static ExceptionQualificationSource Create(JsonObject input)
    {
        var probes = new List<ExceptionQualificationProbe>();
        Exception? root = input["mode"]!.GetValue<string>() switch
        {
            "tree" => input["root"] is null ? null : Tree(input["root"]!.AsObject(), probes),
            "chain" => Chain(input["depth"]!.GetValue<int>(), probes),
            "flat" => Flat(input["children"]!.GetValue<int>(), false, probes),
            "repeated" => Flat(input["children"]!.GetValue<int>(), true, probes),
            _ => throw new InvalidDataException("Unknown exception input descriptor.")
        };
        return new(root, probes);
    }

    private static Exception Tree(JsonObject value, List<ExceptionQualificationProbe> probes)
    {
        Exception[] children = value["children"]?.AsArray().Select(child => Tree(child!.AsObject(), probes)).ToArray() ?? [];
        var probe = new ExceptionQualificationProbe(value["label"]!.GetValue<string>(), Text(value["message"]), Text(value["stack"]));
        probes.Add(probe);
        return value["kind"]!.GetValue<string>() switch
        {
            "single" when children.Length <= 1 => new Single(probe, children.SingleOrDefault()),
            "aggregate" => new Aggregate(probe, children),
            _ => throw new InvalidDataException("Single exception must have at most one child.")
        };
    }

    private static Exception Chain(int depth, List<ExceptionQualificationProbe> probes)
    {
        Exception? inner = null;
        for (int index = depth; index > 0; index--)
        {
            string label = "d" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var probe = new ExceptionQualificationProbe(label, label, null);
            probes.Add(probe);
            inner = new Single(probe, inner);
        }
        return inner ?? throw new InvalidDataException("Chain must have a node.");
    }

    private static Exception Flat(int count, bool repeated, List<ExceptionQualificationProbe> probes)
    {
        Exception[] children;
        if (repeated)
        {
            var shared = new ExceptionQualificationProbe("shared", "shared", null);
            probes.Add(shared);
            children = Enumerable.Repeat<Exception>(new Single(shared, null), count).ToArray();
        }
        else
        {
            children = Enumerable.Range(0, count).Select(index =>
            {
                string label = "n" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var probe = new ExceptionQualificationProbe(label, label, null);
                probes.Add(probe);
                return (Exception)new Single(probe, null);
            }).ToArray();
        }
        var root = new ExceptionQualificationProbe("root", "root", null);
        probes.Add(root);
        return new Aggregate(root, children);
    }

    // Input expansion is distinct from the expected-prefix materializer.
    internal static string? Text(JsonNode? value)
    {
        if (value is null) { return null; }
        if (value is JsonValue) { return value.GetValue<string>(); }
        JsonObject descriptor = value.AsObject();
        if (descriptor["parts"] is JsonArray parts)
        {
            return string.Concat(parts.Select(Text));
        }
        if (descriptor["codeUnits"] is JsonArray units)
        {
            return new string(units.Select(unit => checked((char)unit!.GetValue<int>())).ToArray());
        }
        Rune[] tokens = descriptor["pattern"]!.GetValue<string>().EnumerateRunes().ToArray();
        var output = new StringBuilder();
        for (int index = 0; index < descriptor["scalars"]!.GetValue<int>(); index++)
        {
            output.Append(tokens[index % tokens.Length].ToString());
        }
        return output.ToString();
    }

    private sealed class Single : Exception, IExceptionMetadataTrap
    {
        private readonly ExceptionQualificationProbe probe;
        internal Single(ExceptionQualificationProbe probe, Exception? inner) : base(string.Empty, inner)
        {
            this.probe = probe;
            probe.Attach(this, base.Data);
        }
        public override string Message => probe.ReadMessage();
        public override string? StackTrace => probe.ReadStack();
        public override IDictionary Data => probe.Deny<IDictionary>();
        public override string? Source { get => probe.Deny<string?>(); set => probe.SourceText = value; }
        public override string? HelpLink { get => probe.Deny<string?>(); set => probe.HelpText = value; }
        public object Body => probe.Deny<object>();
        public object Headers => probe.Deny<object>();
        public object Environment => probe.Deny<object>();
        public override string ToString() => probe.Deny<string>();
    }

    private sealed class Aggregate : AggregateException, IExceptionMetadataTrap
    {
        private readonly ExceptionQualificationProbe probe;
        internal Aggregate(ExceptionQualificationProbe probe, IEnumerable<Exception> children) : base(string.Empty, children)
        {
            this.probe = probe;
            probe.Attach(this, base.Data);
        }
        public override string Message => probe.ReadMessage();
        public override string? StackTrace => probe.ReadStack();
        public override IDictionary Data => probe.Deny<IDictionary>();
        public override string? Source { get => probe.Deny<string?>(); set => probe.SourceText = value; }
        public override string? HelpLink { get => probe.Deny<string?>(); set => probe.HelpText = value; }
        public object Body => probe.Deny<object>();
        public object Headers => probe.Deny<object>();
        public object Environment => probe.Deny<object>();
        public override string ToString() => probe.Deny<string>();
    }
}

internal sealed class ExceptionQualificationProbe(string label, string? text, string? stack)
{
    internal string Label { get; } = label;
    internal string? Text { get; set; } = text;
    internal string? Stack { get; set; } = stack;
    internal IDictionary RawData { get; private set; } = null!;
    internal Exception Attached { get; private set; } = null!;
    internal string? SourceText { get; set; }
    internal string? HelpText { get; set; }
    internal int MessageReads { get; private set; }
    internal int StackReads { get; private set; }
    internal int ForbiddenReads { get; private set; }
    internal void Attach(Exception source, IDictionary data)
    {
        Attached = source;
        RawData = data;
        data["self"] = source;
        data["cycle"] = data;
        data["body"] = ExceptionQualificationSources.PrivateSentinel;
        data["attachment"] = new QualificationPayload();
    }
    internal string ReadMessage() { MessageReads++; return Text!; }
    internal string? ReadStack() { StackReads++; return Stack; }
    internal T Deny<T>() { ForbiddenReads++; throw new InvalidOperationException("Excluded metadata accessor."); }
}

internal sealed class QualificationPayload
{
    internal byte[] Bytes { get; } = [1, 2, 3, 4];
    public override string ToString() => throw new InvalidOperationException("Excluded fixture payload formatting.");
}
