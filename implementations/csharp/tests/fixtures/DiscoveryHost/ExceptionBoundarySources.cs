using System.Globalization;
using System.Text;

namespace CIS.Tests.DiscoveryFixture;

internal sealed record ExceptionBoundarySource(Exception Root, IReadOnlyList<ExceptionReadProbe> Probes);

internal static class ExceptionBoundarySources
{
    internal static IEnumerable<string> Cases =>
        new[] { 0, 14, 15, 16, 17, 1000 }.Select(count => $"nodes-{count}")
        .Concat(new[] { 1, 3, 4, 5, 64 }.Select(depth => $"depth-{depth}"))
        .Concat(new[] { "message", "stack" }.SelectMany(textField =>
            new[] { "ascii", "astral", "combining" }.SelectMany(pattern =>
                new[] { -1, 0, 1 }.Select(delta => $"{textField}-{pattern}-{Limit(textField) + delta}"))))
        .Concat(["mixed-exact", "mixed-over", "depth-sibling", "shared", "empty-depth"]);

    internal static ExceptionBoundarySource Create(string id)
    {
        var probes = new List<ExceptionReadProbe>();
        Exception root;
        string[] parts = id.Split('-');
        if (parts[0] == "nodes")
        {
            int children = int.Parse(parts[1], CultureInfo.InvariantCulture);
            root = Aggregate("root", Enumerable.Range(0, children).Select(index => Leaf($"n{index}", probes)), probes);
        }
        else if (parts[0] == "depth" && id != "depth-sibling")
        {
            root = Chain(int.Parse(parts[1], CultureInfo.InvariantCulture), "d", probes);
        }
        else if (parts[0] is "message" or "stack")
        {
            var probe = Probe("text", probes);
            string text = Text(parts[1], int.Parse(parts[2], CultureInfo.InvariantCulture));
            if (parts[0] == "message")
            {
                probe.Message = text;
            }
            else
            {
                probe.Stack = text;
            }
            root = new TrackedException(probe);
        }
        else
        {
            root = Mixed(id, probes);
        }
        foreach (ExceptionReadProbe probe in probes)
        {
            probe.Reset();
        }
        return new(root, probes);
    }

    internal static int Limit(string field) => field == "message" ? 4096 : 16384;

    internal static string Text(string pattern, int scalars)
    {
        string token = pattern switch
        {
            "ascii" => "x",
            "astral" => "😀",
            "combining" => "e\u0301",
            _ => throw new InvalidDataException("Unknown scalar pattern.")
        };
        Rune[] runes = token.EnumerateRunes().ToArray();
        string[] pieces = runes.Select(rune => rune.ToString()).ToArray();
        var text = new StringBuilder();
        for (int index = 0; index < scalars; index++)
        {
            text.Append(pieces[index % runes.Length]);
        }
        return text.ToString();
    }

    private static Exception Mixed(string id, List<ExceptionReadProbe> probes) => id switch
    {
        "mixed-exact" => Aggregate("root", [Branch("a", 6, probes), Branch("b", 7, probes)], probes),
        "mixed-over" => Aggregate("root", [Branch("a", 8, probes), Branch("b", 8, probes), Leaf("tail", probes)], probes),
        "depth-sibling" => Aggregate("root", [Chain(5, "c", probes), Leaf("tail", probes)], probes),
        "shared" => Shared(probes),
        "empty-depth" => Leaf("d1", probes, Leaf("d2", probes, Leaf("d3", probes, Aggregate("empty", [], probes)))),
        _ => throw new InvalidDataException("Unknown boundary shape.")
    };

    private static Exception Shared(List<ExceptionReadProbe> probes)
    {
        Exception shared = Leaf("shared", probes);
        return Aggregate("root", Enumerable.Repeat(shared, 15), probes);
    }

    private static Exception Branch(string name, int leaves, List<ExceptionReadProbe> probes) =>
        Aggregate(name, Enumerable.Range(0, leaves).Select(index => Leaf($"{name}{index}", probes)), probes);

    private static Exception Chain(int depth, string prefix, List<ExceptionReadProbe> probes)
    {
        Exception? inner = null;
        for (int index = depth; index > 0; index--)
        {
            inner = Leaf(prefix + index.ToString(CultureInfo.InvariantCulture), probes, inner);
        }
        return inner!;
    }

    private static Exception Leaf(string label, List<ExceptionReadProbe> probes, Exception? inner = null) => new TrackedException(Probe(label, probes), inner);

    private static Exception Aggregate(string label, IEnumerable<Exception> children, List<ExceptionReadProbe> probes) => new TrackedAggregate(Probe(label, probes), children);

    private static ExceptionReadProbe Probe(string label, List<ExceptionReadProbe> probes)
    {
        var probe = new ExceptionReadProbe(label);
        probes.Add(probe);
        return probe;
    }

    private sealed class TrackedException(ExceptionReadProbe probe, Exception? inner = null) : Exception(string.Empty, inner)
    {
        public override string Message => probe.ReadMessage();
        public override string? StackTrace => probe.ReadStack();
    }

    private sealed class TrackedAggregate(ExceptionReadProbe probe, IEnumerable<Exception> children) : AggregateException(string.Empty, children)
    {
        public override string Message => probe.ReadMessage();
        public override string? StackTrace => probe.ReadStack();
    }
}

internal sealed class ExceptionReadProbe(string label)
{
    internal string Label { get; } = label;
    internal string? Message { get; set; } = label;
    internal string? Stack { get; set; }
    internal int MessageReads { get; private set; }
    internal int StackReads { get; private set; }
    internal bool FailIfRead { get; set; }

    internal string ReadMessage()
    {
        MessageReads++;
        if (FailIfRead)
        {
            throw new InvalidOperationException("Omitted exception accessor must not be read.");
        }
        return Message!;
    }

    internal string? ReadStack()
    {
        StackReads++;
        if (FailIfRead)
        {
            throw new InvalidOperationException("Omitted exception accessor must not be read.");
        }
        return Stack;
    }

    internal void Reset()
    {
        MessageReads = 0;
        StackReads = 0;
    }
}
