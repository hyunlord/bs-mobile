using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchitectureGuard;

internal sealed record AlgorithmConstant(string Symbol, string Value, string Reason);

internal static partial class Guard
{
    private static readonly HashSet<string> EngineNames = new(StringComparer.Ordinal)
        { "UnityEngine", "UnityEditor", "Godot", "Unreal", "UnrealEngine", "Stride", "MonoGame" };

    [GeneratedRegex(@"\b[a-z][a-z0-9_-]*:[a-z0-9][a-z0-9_.:-]*", RegexOptions.IgnoreCase)]
    private static partial Regex ContentId();

    internal static List<string> CheckRepository(string root)
    {
        var directory = Path.Combine(root, "core/src/SowSiege.Core");
        if (!Directory.Exists(directory))
        {
            throw new IOException($"Required Core directory missing: {directory}");
        }

        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal).ToDictionary(path => path, File.ReadAllText);
        if (files.Count == 0)
        {
            throw new IOException("Core contains no source files.");
        }

        var allowlist = JsonSerializer.Deserialize<AlgorithmConstant[]>(File.ReadAllText(
            Path.Combine(root, "tools/ArchitectureGuard/algorithm-constants.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        if (allowlist.Any(item => string.IsNullOrWhiteSpace(item.Reason)))
        {
            throw new IOException("Every algorithm constant exemption requires a review explanation.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                     .Where(path => path.Split(Path.DirectorySeparatorChar).Any(part => part is "data" or "tests")
                         && !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "node_modules")))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            CollectIds(json.RootElement, ids, path.Contains("hero", StringComparison.OrdinalIgnoreCase)
                || path.Contains("estate", StringComparison.OrdinalIgnoreCase));
        }
        var results = CheckSources(files, ids, allowlist);
        var projects = Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories);
        if (projects.Length == 0)
        {
            throw new IOException("Core project is missing.");
        }

        var projectFiles = projects.Concat(new[] { "Directory.Build.props", "Directory.Build.targets" }
            .SelectMany(name => Ancestors(directory, root).Select(parent => Path.Combine(parent, name)))
            .Where(File.Exists)).Distinct();
        foreach (var path in projectFiles)
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            if (document.Root?.Name.LocalName == "Project"
                && document.Root.Attribute("Sdk") is { } sdk && sdk.Value != "Microsoft.NET.Sdk")
            {
                results.Add($"{path}(1): AG001 Core must use only the BCL Microsoft.NET.Sdk.");
            }

            foreach (var element in document.Descendants().Where(element => element.Name.LocalName is
                         "PackageReference" or "ProjectReference" or "Reference" or "COMReference" or "FrameworkReference" or "Import"))
            {
                var line = ((System.Xml.IXmlLineInfo)element).LineNumber;
                results.Add($"{path}({line}): AG001 Core must be BCL-only; forbidden {element.Name.LocalName}.");
            }
        }
        return results;
    }

    private static IEnumerable<string> Ancestors(string directory, string root)
    {
        for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
        {
            yield return current.FullName;
            if (current.FullName == root)
            {
                yield break;
            }
        }
    }

    private static void CollectIds(JsonElement element, HashSet<string> ids, bool context)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var related = property.Name.Contains("hero", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("estate", StringComparison.OrdinalIgnoreCase);
                if (((context && property.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                        || (related && property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase)))
                    && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } id)
                {
                    ids.Add(id);
                }

                CollectIds(property.Value, ids, related);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectIds(child, ids, context);
            }
        }
    }

    internal static List<string> CheckSources(IReadOnlyDictionary<string, string> files, HashSet<string> ids,
        IReadOnlyList<AlgorithmConstant> allowlist)
    {
        var trees = files.SelectMany(file => ParseEveryBranch(file.Key, file.Value)).ToArray();
        var implicitUsings = CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.Linq;");
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("CoreArchitectureInspection", trees.Append(implicitUsings), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var results = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is IdentifierNameSyntax name && EngineNames.Contains(name.Identifier.ValueText))
                {
                    Report("AG001", "Engine names are forbidden in Core, including unused imports.", node);
                }

                if (node is ExpressionSyntax expression)
                {
                    var constant = model.GetConstantValue(expression);
                    if (constant.HasValue && constant.Value is string text && (ids.Contains(text) || ContentId().IsMatch(text)))
                    {
                        Report("AG002", "Concrete content ID belongs in data, not Core.", node);
                    }

                    var symbol = model.GetSymbolInfo(expression).Symbol;
                    var type = symbol?.ContainingType?.ToDisplayString();
                    if ((type is "System.DateTime" or "System.DateTimeOffset" && symbol?.Name is "Now" or "UtcNow" or "Today")
                        || (type == "System.Guid" && symbol?.Name == "NewGuid")
                        || (type == "System.Random" && symbol?.Name == "Shared")
                        || type == "System.Diagnostics.Stopwatch"
                        || (type == "System.Environment" && symbol?.Name is "TickCount" or "TickCount64"))
                    {
                        Report("AG004", "Nondeterministic clock/random source is forbidden in Core.", node);
                    }

                    if (node is BaseObjectCreationExpressionSyntax creation && type == "System.Random"
                        && (creation.ArgumentList?.Arguments.Count ?? 0) == 0)
                    {
                        Report("AG004", "Random requires an explicit deterministic seed.", node);
                    }
                }
                if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression)
                    && !AllowedNumber(literal, model, allowlist))
                {
                    Report("AG003", "Numeric gameplay value must come from data; only 0/1 or reviewed algorithm constants are allowed.", node);
                }
            }
        }
        return results.Order(StringComparer.Ordinal).ToList();

        void Report(string rule, string message, SyntaxNode node)
        {
            var line = node.GetLocation().GetLineSpan();
            results.Add($"{line.Path}({line.StartLinePosition.Line + 1}): {rule} {message}");
        }
    }

    private static IEnumerable<SyntaxTree> ParseEveryBranch(string path, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path);
        yield return tree;
        foreach (var disabled in tree.GetRoot().DescendantTrivia()
                     .Where(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia)))
        {
            var line = tree.GetLineSpan(disabled.Span).StartLinePosition.Line;
            foreach (var branch in ParseEveryBranch(path, new string('\n', line) + disabled.ToFullString()))
            {
                yield return branch;
            }
        }
    }

    private static bool AllowedNumber(LiteralExpressionSyntax literal, SemanticModel model,
        IReadOnlyList<AlgorithmConstant> allowlist)
    {
        decimal value;
        try { value = Convert.ToDecimal(literal.Token.Value, CultureInfo.InvariantCulture); }
        catch (OverflowException) { return false; }
        if (value is 0 or 1)
        {
            return true;
        }

        var declaration = literal.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        if (declaration == null || declaration.Initializer?.Value != literal
            || model.GetDeclaredSymbol(declaration) is not IFieldSymbol { IsConst: true } symbol)
        {
            return false;
        }

        return allowlist.Any(item => item.Symbol == symbol.ToDisplayString()
            && item.Value == Convert.ToString(literal.Token.Value, CultureInfo.InvariantCulture));
    }
}
