using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Stryker.Abstractions;
using Stryker.Configuration.Options;

namespace CIS.Tests.TypeManagerMutation;

internal static class ApplyNativeMutation
{
    private static void Main(string[] args)
    {
        if (args.Length != 4) { throw new ArgumentException("Expected tool directory, source directory, native report and output directory."); }
        string tools = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(tools, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string[] args)
    {
        string sourceDirectory = args[1];
        using var report = JsonDocument.Parse(File.ReadAllBytes(args[2]));
        string output = args[3];
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) { throw new ArgumentException("Use an empty syntax output directory."); }
        Directory.CreateDirectory(output);
        var trees = Directory.GetFiles(sourceDirectory, "*.cs").ToDictionary(path => Path.GetFileName(path)!,
            path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path));
        if (trees.Values.Any(tree => tree.GetRoot().DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsDirective)))
        {
            throw new InvalidDataException("This replay does not support preprocessor directives.");
        }
        var globals = CSharpSyntaxTree.ParseText("global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic; global using System.Threading; global using System.Threading.Tasks;");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("CIS.Core", trees.Values.Append(globals), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var options = new StrykerOptions { MutationLevel = MutationLevel.Complete };
        var mutators = typeof(Stryker.Core.Mutators.StatementMutator).Assembly.GetTypes()
            // This report has no regex mutations. Unsupported families fail the exact-match check below.
            .Where(type => !type.IsAbstract && type.Name != "RegexMutator" && typeof(IMutator).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
            .Select(type => (IMutator)Activator.CreateInstance(type)!).ToArray();
        var results = new Dictionary<string, object>(StringComparer.Ordinal);
        string controlsDirectory = Path.Combine(output, "controls");
        Directory.CreateDirectory(controlsDirectory);
        var controls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, tree) in trees)
        {
            string controlPath = Path.Combine(controlsDirectory, name);
            File.WriteAllText(controlPath, Serialize(tree.GetRoot(), "control " + name), new UTF8Encoding(false));
            controls.Add(name, Hash(controlPath));
        }
        foreach (var file in report.RootElement.GetProperty("files").EnumerateObject())
        {
            string name = file.Name.Replace('\\', '/').Split('/')[^1];
            var tree = trees[name];
            var root = tree.GetRoot();
            var source = tree.GetText();
            if (source.ToString() != file.Value.GetProperty("source").GetString()) { throw new InvalidDataException("Source differs: " + name); }
            foreach (var entry in file.Value.GetProperty("mutants").EnumerateArray())
            {
                string id = entry.GetProperty("id").GetString()!;
                var location = entry.GetProperty("location");
                int Start(JsonElement position) => source.Lines.GetPosition(new LinePosition(position.GetProperty("line").GetInt32() - 1, position.GetProperty("column").GetInt32() - 1));
                var span = TextSpan.FromBounds(Start(location.GetProperty("start")), Start(location.GetProperty("end")));
                string replacement = entry.GetProperty("replacement").GetString()!;
                string display = entry.GetProperty("mutatorName").GetString()!;
                var matches = root.FindNode(span, getInnermostNodeForTie: true).AncestorsAndSelf()
                    .SelectMany(node => mutators.SelectMany(mutator => mutator.Mutate(node, compilation.GetSemanticModel(tree), options)))
                    .Where(mutation => mutation.OriginalNode.Span == span && mutation.DisplayName == display && mutation.ReplacementNode.ToString() == replacement)
                    .ToArray();
                if (matches.Length != 1) { throw new InvalidDataException($"Expected one original mutator match for {id}; found {matches.Length}."); }
                var mutation = matches[0];
                var changed = root.ReplaceNode(mutation.OriginalNode, mutation.ReplacementNode);
                string text = Serialize(changed, id);
                File.WriteAllText(Path.Combine(output, id + ".cs"), text, new UTF8Encoding(false));
                results.Add(id, new { id, file = name, mutator = display, originalText = source.ToString(span), originalKind = mutation.OriginalNode.Kind().ToString(), replacementKind = mutation.ReplacementNode.Kind().ToString(), sha256 = Hash(Path.Combine(output, id + ".cs")) });
            }
        }
        var manifest = new
        {
            nativeReportSha256 = Hash(args[2]),
            generatorAssemblySha256 = Hash(Assembly.GetExecutingAssembly().Location),
            sourceFiles = Directory.GetFiles(sourceDirectory, "*.cs").ToDictionary(path => Path.GetFileName(path)!, Hash),
            toolFiles = Directory.GetFiles(args[0], "*.dll").ToDictionary(path => Path.GetFileName(path)!, Hash),
            mutants = results,
            controls,
            scope = "Actual pinned Stryker mutation nodes matched uniquely under the isolated TypeManagement semantic model; complete syntax-tree structure verified after parenthesized serialization; no preprocessor directives"
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Serialize(SyntaxNode tree, string description)
    {
        var protectedTree = new ParenthesizeOperators().Visit(tree)!;
        string text = protectedTree.NormalizeWhitespace().ToFullString();
        if (!SameTree(protectedTree, CSharpSyntaxTree.ParseText(text).GetRoot()))
        {
            throw new InvalidDataException("Serialized mutation changed syntax structure: " + description);
        }
        return text;
    }

    private static bool SameTree(SyntaxNodeOrToken left, SyntaxNodeOrToken right)
    {
        if (left.RawKind != right.RawKind || left.IsNode != right.IsNode) { return false; }
        if (left.IsToken)
        {
            var a = left.AsToken(); var b = right.AsToken();
            return a.Text == b.Text && a.ValueText == b.ValueText && a.IsMissing == b.IsMissing;
        }
        var first = left.ChildNodesAndTokens(); var second = right.ChildNodesAndTokens();
        return first.Count == second.Count && first.Zip(second).All(pair => SameTree(pair.First, pair.Second));
    }
    // Serialization must not change the precedence of the actual mutator's tree.
    private sealed class ParenthesizeOperators : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node) =>
            SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)base.VisitBinaryExpression(node)!);
        public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node) =>
            SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)base.VisitConditionalExpression(node)!);
        public override SyntaxNode? VisitIsPatternExpression(IsPatternExpressionSyntax node) =>
            SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)base.VisitIsPatternExpression(node)!);
        public override SyntaxNode? VisitBinaryPattern(BinaryPatternSyntax node) =>
            SyntaxFactory.ParenthesizedPattern((PatternSyntax)base.VisitBinaryPattern(node)!);
    }
}
