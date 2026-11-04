using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host;

namespace CWM.RoslynNavigator.Caching;

/// <summary>
/// Converts between a loaded <see cref="Solution"/> and its cacheable description
/// (<see cref="CachedSnapshot"/>). Capture reads only project metadata, never compilations,
/// so it costs milliseconds even on a 700-project solution. Restore rebuilds the solution the
/// way MSBuildWorkspace builds it: references through the workspace's metadata service (lazy,
/// memory-mapped, shared), analyzers through its analyzer loader (the shadow-copying one),
/// and document text loaded lazily from disk.
/// </summary>
internal static class SnapshotMapper
{
    /// <summary>
    /// Describes <paramref name="solution"/>, or returns null with <paramref name="notCacheable"/>
    /// set when it holds something this cache cannot recreate (a non-C# project, an in-memory
    /// reference, a document without a file).
    /// </summary>
    public static CachedSnapshot? Capture(Solution solution, out string? notCacheable)
    {
        notCacheable = null;
        var projectIndex = new Dictionary<ProjectId, int>();
        foreach (var id in solution.ProjectIds)
            projectIndex[id] = projectIndex.Count;

        var metadata = new Interner();
        var analyzers = new Interner();
        var projects = new List<CachedProject>(projectIndex.Count);

        foreach (var id in solution.ProjectIds)
        {
            var project = solution.GetProject(id)!;
            if (project.Language != LanguageNames.CSharp || project.FilePath is null)
            {
                notCacheable = $"project {project.Name} is not a C# project file";
                return null;
            }

            var documents = CaptureDocuments(project.Documents.Select(d => (TextDocument)d), withKind: true);
            var additional = CaptureDocuments(project.AdditionalDocuments, withKind: false);
            var configs = CaptureDocuments(project.AnalyzerConfigDocuments, withKind: false);
            if (documents is null || additional is null || configs is null)
            {
                notCacheable = $"project {project.Name} has a document without a file";
                return null;
            }

            List<CachedMetadataReference> references = [];
            foreach (var reference in project.MetadataReferences)
            {
                if (reference is not PortableExecutableReference { FilePath: { } path } pe)
                {
                    notCacheable = $"project {project.Name} has an in-memory metadata reference";
                    return null;
                }

                references.Add(new CachedMetadataReference(
                    metadata.Intern(path), pe.Properties.Kind, NullIfEmpty(pe.Properties.Aliases), pe.Properties.EmbedInteropTypes));
            }

            List<int> analyzerIndexes = [];
            foreach (var reference in project.AnalyzerReferences)
            {
                if (reference is not AnalyzerFileReference file)
                {
                    notCacheable = $"project {project.Name} has an analyzer that is not a file";
                    return null;
                }

                analyzerIndexes.Add(analyzers.Intern(file.FullPath));
            }

            projects.Add(new CachedProject(
                project.Name,
                project.AssemblyName,
                project.Language,
                project.FilePath,
                project.OutputFilePath,
                project.OutputRefFilePath,
                project.CompilationOutputInfo.AssemblyPath,
                project.CompilationOutputInfo.GeneratedFilesOutputDirectory,
                project.DefaultNamespace,
                CaptureCompilationOptions(project.CompilationOptions as CSharpCompilationOptions),
                CaptureParseOptions(project.ParseOptions as CSharpParseOptions),
                documents,
                additional,
                configs,
                [.. project.ProjectReferences
                    .Where(r => projectIndex.ContainsKey(r.ProjectId))
                    .Select(r => new CachedProjectReference(projectIndex[r.ProjectId], NullIfEmpty(r.Aliases), r.EmbedInteropTypes))],
                references,
                analyzerIndexes));
        }

        return new CachedSnapshot(metadata.Values, analyzers.Values, projects);
    }

    /// <summary>
    /// <paramref name="snapshot"/> without the documents whose file is not in
    /// <paramref name="existing"/>: a file deleted since the cache was written (glob-included
    /// files disappear from the next evaluation), or an explicit item for a file that never
    /// existed, which loads as an empty document either way.
    /// </summary>
    public static CachedSnapshot WithoutMissingFiles(CachedSnapshot snapshot, IReadOnlySet<string> existing, out int dropped)
    {
        var count = 0;
        List<CachedDocument> Keep(IReadOnlyList<CachedDocument> documents)
        {
            List<CachedDocument> kept = [.. documents.Where(d => existing.Contains(d.FilePath))];
            count += documents.Count - kept.Count;
            return kept;
        }

        var projects = snapshot.Projects
            .Select(p => p with
            {
                Documents = Keep(p.Documents),
                AdditionalDocuments = Keep(p.AdditionalDocuments),
                AnalyzerConfigDocuments = Keep(p.AnalyzerConfigDocuments),
            })
            .ToList();

        dropped = count;
        return count == 0 ? snapshot : snapshot with { Projects = projects };
    }

    /// <summary>
    /// Null when <paramref name="expected"/> and <paramref name="actual"/> describe the same
    /// projects, documents, references, and options (in the same order); otherwise the first
    /// difference, for the log and get_workspace_status.
    /// </summary>
    public static string? Compare(CachedSnapshot expected, CachedSnapshot actual)
    {
        if (LoadCacheStore.Serialize(expected).AsSpan().SequenceEqual(LoadCacheStore.Serialize(actual)))
            return null;

        if (expected.Projects.Count != actual.Projects.Count)
            return $"{actual.Projects.Count} projects instead of {expected.Projects.Count}";

        for (var i = 0; i < expected.Projects.Count; i++)
        {
            var e = expected.Projects[i];
            var a = actual.Projects[i];
            if (e.Name != a.Name || e.FilePath != a.FilePath)
                return $"project {i + 1} is {a.Name} instead of {e.Name}";

            string? aspect =
                !SameDocuments(e.Documents, a.Documents) ? $"documents ({e.Documents.Count} cached, {a.Documents.Count} now)"
                : !SameDocuments(e.AdditionalDocuments, a.AdditionalDocuments) ? "additional files"
                : !SameDocuments(e.AnalyzerConfigDocuments, a.AnalyzerConfigDocuments) ? "analyzer config files"
                : !e.MetadataReferences.Select(r => (expected.MetadataReferences[r.Path], r.Kind, r.EmbedInteropTypes, Join(r.Aliases)))
                    .SequenceEqual(a.MetadataReferences.Select(r => (actual.MetadataReferences[r.Path], r.Kind, r.EmbedInteropTypes, Join(r.Aliases))))
                    ? "metadata references"
                : !e.AnalyzerReferences.Select(r => expected.AnalyzerReferences[r])
                    .SequenceEqual(a.AnalyzerReferences.Select(r => actual.AnalyzerReferences[r]), StringComparer.Ordinal)
                    ? "analyzer references"
                : !e.ProjectReferences.Select(r => (expected.Projects[r.Project].FilePath, expected.Projects[r.Project].Name, Join(r.Aliases)))
                    .SequenceEqual(a.ProjectReferences.Select(r => (actual.Projects[r.Project].FilePath, actual.Projects[r.Project].Name, Join(r.Aliases))))
                    ? "project references"
                : !LoadCacheStore.Serialize(e with { Documents = [], AdditionalDocuments = [], AnalyzerConfigDocuments = [], MetadataReferences = [], AnalyzerReferences = [], ProjectReferences = [] })
                    .AsSpan().SequenceEqual(LoadCacheStore.Serialize(a with { Documents = [], AdditionalDocuments = [], AnalyzerConfigDocuments = [], MetadataReferences = [], AnalyzerReferences = [], ProjectReferences = [] }))
                    ? "options or output paths"
                : null;
            if (aspect is not null)
                return $"{e.Name}: {aspect} differ";
        }

        return "reference order differs";
    }

    private static bool SameDocuments(IReadOnlyList<CachedDocument> expected, IReadOnlyList<CachedDocument> actual) =>
        expected.Count == actual.Count
        && expected.Zip(actual).All(pair => pair.First.FilePath == pair.Second.FilePath
                                            && pair.First.Name == pair.Second.Name
                                            && pair.First.Kind == pair.Second.Kind
                                            && pair.First.Folders.SequenceEqual(pair.Second.Folders, StringComparer.Ordinal));

    private static string Join(IReadOnlyList<string>? values) => values is null ? "" : string.Join(',', values);

    /// <summary>Every document path in <paramref name="snapshot"/>.</summary>
    public static IEnumerable<string> DocumentPaths(CachedSnapshot snapshot) =>
        snapshot.Projects.SelectMany(p => p.Documents.Concat(p.AdditionalDocuments).Concat(p.AnalyzerConfigDocuments))
            .Select(d => d.FilePath);

    /// <summary>
    /// Adds <paramref name="snapshot"/> to <paramref name="workspace"/> (which must hold no
    /// solution yet) as the solution at <paramref name="solutionPath"/>, and returns it with
    /// the project id minted for each cached project, in cache order.
    /// </summary>
    public static (Solution Solution, IReadOnlyList<ProjectId> ProjectIds) Restore(
        CachedSnapshot snapshot, AdhocWorkspace workspace, string solutionPath)
    {
        var loader = workspace.Services.GetRequiredService<IAnalyzerService>().GetLoader();

        // One reference object per distinct (file, properties) and one analyzer reference per
        // file, shared by every project — as many projects reference the same assemblies.
        var files = CreateMetadataReferences(snapshot);
        var references = new Dictionary<(int, MetadataImageKind, string, bool), MetadataReference?>();
        var analyzers = new AnalyzerReference?[snapshot.AnalyzerReferences.Count];

        var ids = snapshot.Projects.Select(p => ProjectId.CreateNewId(p.Name)).ToList();
        var infos = new List<ProjectInfo>(snapshot.Projects.Count);

        for (var i = 0; i < snapshot.Projects.Count; i++)
        {
            var cached = snapshot.Projects[i];
            var id = ids[i];
            var directory = Path.GetDirectoryName(cached.FilePath) ?? "";

            var metadataReferences = cached.MetadataReferences.Select(r =>
            {
                var key = (r.Path, r.Kind, Join(r.Aliases), r.EmbedInteropTypes);
                if (!references.TryGetValue(key, out var reference))
                {
                    var file = files[(r.Path, r.Kind)];
                    references[key] = reference = r.Aliases is null && !r.EmbedInteropTypes
                        ? file
                        : file?.WithProperties(new MetadataReferenceProperties(r.Kind, [.. r.Aliases ?? []], r.EmbedInteropTypes));
                }

                return reference;
            }).OfType<MetadataReference>().ToList();

            var analyzerReferences = cached.AnalyzerReferences
                .Select(index => analyzers[index] ??= new AnalyzerFileReference(snapshot.AnalyzerReferences[index], loader))
                .ToList();

            var info = ProjectInfo.Create(
                    id,
                    VersionStamp.Create(),
                    cached.Name,
                    cached.AssemblyName,
                    cached.Language,
                    filePath: cached.FilePath,
                    outputFilePath: cached.OutputFilePath,
                    compilationOptions: RestoreCompilationOptions(cached.CompilationOptions, directory),
                    parseOptions: RestoreParseOptions(cached.ParseOptions),
                    documents: RestoreDocuments(id, cached.Documents),
                    projectReferences: cached.ProjectReferences.Select(r => new ProjectReference(
                        ids[r.Project], r.Aliases is null ? default : [.. r.Aliases], r.EmbedInteropTypes)),
                    metadataReferences: metadataReferences,
                    analyzerReferences: analyzerReferences,
                    additionalDocuments: RestoreDocuments(id, cached.AdditionalDocuments),
                    outputRefFilePath: cached.OutputRefFilePath)
                .WithAnalyzerConfigDocuments(RestoreDocuments(id, cached.AnalyzerConfigDocuments))
                .WithCompilationOutputInfo(default(CompilationOutputInfo).WithAssemblyPath(cached.CompilationOutputAssemblyPath).WithGeneratedFilesOutputDirectory(cached.GeneratedFilesOutputDirectory))
                .WithDefaultNamespace(cached.DefaultNamespace);
            infos.Add(info);
        }

        var solution = workspace.AddSolution(SolutionInfo.Create(
            SolutionId.CreateNewId(solutionPath), VersionStamp.Create(), solutionPath, infos));
        return (solution, ids);
    }

    /// <summary>
    /// One reference per distinct (file, kind), read in parallel — what MSBuildWorkspace's
    /// metadata service does per file: the image is read once and the file closed, so the
    /// navigator never locks an assembly a build wants to overwrite, and the XML documentation
    /// beside it is read on demand. A file that no longer exists (a cleared package cache) is
    /// left out, as MSBuildWorkspace leaves out a reference it cannot resolve.
    /// </summary>
    private static ConcurrentDictionary<(int Path, MetadataImageKind Kind), PortableExecutableReference?> CreateMetadataReferences(
        CachedSnapshot snapshot)
    {
        var needed = snapshot.Projects.SelectMany(p => p.MetadataReferences).Select(r => (r.Path, r.Kind)).Distinct().ToArray();
        var created = new ConcurrentDictionary<(int, MetadataImageKind), PortableExecutableReference?>();
        Parallel.ForEach(needed, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8) }, key =>
        {
            var path = snapshot.MetadataReferences[key.Path];
            try
            {
                created[key] = MetadataReference.CreateFromFile(
                    path, new MetadataReferenceProperties(key.Kind), XmlDocumentationProvider.CreateFromFile(Path.ChangeExtension(path, ".xml")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
                created[key] = null;
            }
        });

        return created;
    }

    private static List<CachedDocument>? CaptureDocuments(IEnumerable<TextDocument> documents, bool withKind)
    {
        List<CachedDocument> result = [];
        foreach (var document in documents)
        {
            if (document.FilePath is not { } path) return null;
            result.Add(new CachedDocument(
                path,
                document.Name == Path.GetFileName(path) ? null : document.Name,
                [.. document.Folders],
                withKind && document is Document { SourceCodeKind: var kind } ? kind : SourceCodeKind.Regular));
        }

        return result;
    }

    private static IEnumerable<DocumentInfo> RestoreDocuments(ProjectId projectId, IReadOnlyList<CachedDocument> documents) =>
        documents.Select(d => DocumentInfo.Create(
            DocumentId.CreateNewId(projectId, d.FilePath),
            d.Name ?? Path.GetFileName(d.FilePath),
            d.Folders,
            d.Kind,
            new FileTextLoader(d.FilePath, defaultEncoding: null),
            d.FilePath));

    private static CachedCompilationOptions? CaptureCompilationOptions(CSharpCompilationOptions? options) =>
        options is null
            ? null
            : new CachedCompilationOptions(
                options.OutputKind,
                options.ModuleName,
                options.MainTypeName,
                options.ScriptClassName,
                [.. options.Usings],
                options.OptimizationLevel,
                options.CheckOverflow,
                options.AllowUnsafe,
                options.CryptoKeyContainer,
                options.CryptoKeyFile,
                options.CryptoPublicKey.IsDefaultOrEmpty ? null : Convert.ToBase64String(options.CryptoPublicKey.AsSpan()),
                options.DelaySign,
                options.Platform,
                options.GeneralDiagnosticOption,
                options.WarningLevel,
                [.. options.SpecificDiagnosticOptions.OrderBy(o => o.Key, StringComparer.Ordinal)],
                options.ConcurrentBuild,
                options.Deterministic,
                options.PublicSign,
                options.NullableContextOptions,
                options.ReportSuppressedDiagnostics,
                options.MetadataImportOptions);

    /// <summary>
    /// The options, plus the resolvers the MSBuild loader attaches: doc-comment includes and
    /// key files resolve against the project directory, and assembly identities unify the way
    /// the desktop compiler unifies them.
    /// </summary>
    private static CSharpCompilationOptions? RestoreCompilationOptions(CachedCompilationOptions? o, string projectDirectory) =>
        o is null
            ? null
            : new CSharpCompilationOptions(o.OutputKind)
                .WithModuleName(o.ModuleName)
                .WithMainTypeName(o.MainTypeName)
                .WithScriptClassName(o.ScriptClassName ?? WellKnownMemberNames.DefaultScriptClassName)
                .WithUsings(o.Usings)
                .WithOptimizationLevel(o.OptimizationLevel)
                .WithOverflowChecks(o.CheckOverflow)
                .WithAllowUnsafe(o.AllowUnsafe)
                .WithCryptoKeyContainer(o.CryptoKeyContainer)
                .WithCryptoKeyFile(o.CryptoKeyFile)
                .WithCryptoPublicKey(o.CryptoPublicKey is null ? [] : [.. Convert.FromBase64String(o.CryptoPublicKey)])
                .WithDelaySign(o.DelaySign)
                .WithPlatform(o.Platform)
                .WithGeneralDiagnosticOption(o.GeneralDiagnosticOption)
                .WithWarningLevel(o.WarningLevel)
                .WithSpecificDiagnosticOptions(o.SpecificDiagnosticOptions)
                .WithConcurrentBuild(o.ConcurrentBuild)
                .WithDeterministic(o.Deterministic)
                .WithPublicSign(o.PublicSign)
                .WithNullableContextOptions(o.NullableContextOptions)
                .WithReportSuppressedDiagnostics(o.ReportSuppressedDiagnostics)
                .WithMetadataImportOptions(o.MetadataImportOptions)
                .WithXmlReferenceResolver(new XmlFileResolver(projectDirectory))
                .WithSourceReferenceResolver(new SourceFileResolver([], projectDirectory))
                .WithStrongNameProvider(new DesktopStrongNameProvider([projectDirectory], Path.GetTempPath()))
                .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);

    private static CachedParseOptions? CaptureParseOptions(CSharpParseOptions? options) =>
        options is null
            ? null
            : new CachedParseOptions(
                options.SpecifiedLanguageVersion,
                options.DocumentationMode,
                options.Kind,
                [.. options.PreprocessorSymbolNames],
                [.. options.Features.OrderBy(f => f.Key, StringComparer.Ordinal)]);

    private static CSharpParseOptions? RestoreParseOptions(CachedParseOptions? o) =>
        o is null
            ? null
            : new CSharpParseOptions(o.LanguageVersion, o.DocumentationMode, o.Kind, o.PreprocessorSymbols)
                .WithFeatures(o.Features);

    private static List<string>? NullIfEmpty(ImmutableArray<string> values) =>
        values.IsDefaultOrEmpty ? null : [.. values];

    /// <summary>Assigns each distinct string an index, in first-seen order.</summary>
    private sealed class Interner
    {
        private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

        public List<string> Values { get; } = [];

        public int Intern(string value)
        {
            if (!_indexes.TryGetValue(value, out var index))
            {
                index = Values.Count;
                _indexes[value] = index;
                Values.Add(value);
            }

            return index;
        }
    }
}
