using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CWM.RoslynNavigator.Caching;

// The on-disk shape of the load cache. Plain records so System.Text.Json reads and writes
// them without custom converters; every list is required (a null list means a corrupt file,
// see LoadCacheFile.IsWellFormed).

/// <summary>
/// One cached load: what it was keyed on, when it ran, and what it produced. Written after
/// every successful MSBuild load, read on the next start.
/// </summary>
internal sealed record LoadCacheFile(
    int Format,
    LoadCacheHeader Header,
    IReadOnlyList<InputFingerprint> Inputs,
    DateTimeOffset LoadStartedUtc,
    IReadOnlyList<CachedSolutionFile> Solutions,
    IReadOnlyList<CachedDiagnostic> Diagnostics,
    IReadOnlyList<CachedRemovedAnalyzers> RemovedAnalyzers,
    CachedSnapshot Snapshot)
{
    /// <summary>
    /// Bumped whenever the shape or meaning of any record here changes. Also part of the file
    /// name, so an older build never even parses a newer build's file.
    /// </summary>
    public const int CurrentFormat = 1;

    /// <summary>
    /// Every required member present and every index in range. A truncated or hand-edited file
    /// can deserialize "successfully" with nulls or dangling indexes; it must read as a miss.
    /// </summary>
    public bool IsWellFormed()
    {
        if (Format != CurrentFormat || Header?.SolutionPaths is null || Inputs is null
            || Solutions is null || Diagnostics is null || RemovedAnalyzers is null
            || Snapshot?.MetadataReferences is null || Snapshot.AnalyzerReferences is null || Snapshot.Projects is null)
        {
            return false;
        }

        var projects = Snapshot.Projects;
        return Inputs.All(i => i?.Path is not null && i.Fingerprint is not null)
            && Solutions.All(s => s?.Path is not null && s.ProjectPaths is not null)
            && Diagnostics.All(d => d?.Kind is not null && d.Message is not null)
            && RemovedAnalyzers.All(r => r?.Paths is not null && r.Project >= 0 && r.Project < projects.Count)
            && projects.All(p => p is not null && p.IsWellFormed(projects.Count,
                Snapshot.MetadataReferences.Count, Snapshot.AnalyzerReferences.Count));
    }
}

/// <summary>
/// Everything about the environment a cached load depends on that is not a file: the
/// navigator build (its own loading code), the SDK, and the options that change what loads.
/// </summary>
internal sealed record LoadCacheHeader(
    string NavigatorBuild,
    string Sdk,
    bool AllTargetFrameworks,
    IReadOnlyList<string> SolutionPaths);

/// <summary>
/// A file the load read, and its state then: a content hash, a size-and-timestamp stamp
/// (large generated files), or "absent" (a file whose later appearance changes the load).
/// </summary>
internal sealed record InputFingerprint(string Path, string Fingerprint);

/// <summary>A solution file and the projects it declared (<see cref="LoadedSolution"/>).</summary>
internal sealed record CachedSolutionFile(string Path, IReadOnlyList<string> ProjectPaths, string? Error);

/// <summary>A workspace load diagnostic, replayed into the restored snapshot's health record.</summary>
internal sealed record CachedDiagnostic(string Kind, string Message);

/// <summary>Analyzer paths the load stripped as unresolved (<see cref="UnresolvedAnalyzerReferences"/>), by project index.</summary>
internal sealed record CachedRemovedAnalyzers(int Project, IReadOnlyList<string> Paths);

/// <summary>
/// The projects of a loaded solution, as Roslyn received them from MSBuild — after target
/// framework pruning and unresolved-analyzer stripping. Metadata and analyzer reference paths
/// are interned: hundreds of projects share the same few hundred assemblies.
/// </summary>
internal sealed record CachedSnapshot(
    IReadOnlyList<string> MetadataReferences,
    IReadOnlyList<string> AnalyzerReferences,
    IReadOnlyList<CachedProject> Projects);

internal sealed record CachedProject(
    string Name,
    string AssemblyName,
    string Language,
    string FilePath,
    string? OutputFilePath,
    string? OutputRefFilePath,
    string? CompilationOutputAssemblyPath,
    string? GeneratedFilesOutputDirectory,
    string? DefaultNamespace,
    CachedCompilationOptions? CompilationOptions,
    CachedParseOptions? ParseOptions,
    IReadOnlyList<CachedDocument> Documents,
    IReadOnlyList<CachedDocument> AdditionalDocuments,
    IReadOnlyList<CachedDocument> AnalyzerConfigDocuments,
    IReadOnlyList<CachedProjectReference> ProjectReferences,
    IReadOnlyList<CachedMetadataReference> MetadataReferences,
    IReadOnlyList<int> AnalyzerReferences)
{
    public bool IsWellFormed(int projectCount, int metadataCount, int analyzerCount) =>
        Name is not null && AssemblyName is not null && Language is not null && FilePath is not null
        && Documents is not null && Documents.All(d => d?.FilePath is not null && d.Folders is not null)
        && AdditionalDocuments is not null && AdditionalDocuments.All(d => d?.FilePath is not null && d.Folders is not null)
        && AnalyzerConfigDocuments is not null && AnalyzerConfigDocuments.All(d => d?.FilePath is not null && d.Folders is not null)
        && ProjectReferences is not null && ProjectReferences.All(r => r is not null && r.Project >= 0 && r.Project < projectCount)
        && MetadataReferences is not null && MetadataReferences.All(r => r is not null && r.Path >= 0 && r.Path < metadataCount)
        && AnalyzerReferences is not null && AnalyzerReferences.All(i => i >= 0 && i < analyzerCount)
        && (CompilationOptions is null || CompilationOptions.IsWellFormed())
        && (ParseOptions is null || ParseOptions.IsWellFormed());
}

/// <summary>A document; <see cref="Name"/> is null when it is the file name, which it nearly always is.</summary>
internal sealed record CachedDocument(string FilePath, string? Name, IReadOnlyList<string> Folders, SourceCodeKind Kind);

internal sealed record CachedProjectReference(int Project, IReadOnlyList<string>? Aliases, bool EmbedInteropTypes);

internal sealed record CachedMetadataReference(int Path, MetadataImageKind Kind, IReadOnlyList<string>? Aliases, bool EmbedInteropTypes);

/// <summary>
/// The public, settable state of <see cref="CSharpCompilationOptions"/>. The resolvers MSBuild
/// attaches (XML, source, strong name, identity comparer) are recreated on restore from the
/// project directory, as the MSBuild loader creates them.
/// </summary>
internal sealed record CachedCompilationOptions(
    OutputKind OutputKind,
    string? ModuleName,
    string? MainTypeName,
    string? ScriptClassName,
    IReadOnlyList<string> Usings,
    OptimizationLevel OptimizationLevel,
    bool CheckOverflow,
    bool AllowUnsafe,
    string? CryptoKeyContainer,
    string? CryptoKeyFile,
    string? CryptoPublicKey,
    bool? DelaySign,
    Platform Platform,
    ReportDiagnostic GeneralDiagnosticOption,
    int WarningLevel,
    IReadOnlyList<KeyValuePair<string, ReportDiagnostic>> SpecificDiagnosticOptions,
    bool ConcurrentBuild,
    bool Deterministic,
    bool PublicSign,
    NullableContextOptions NullableContextOptions,
    bool ReportSuppressedDiagnostics,
    MetadataImportOptions MetadataImportOptions)
{
    public bool IsWellFormed() => Usings is not null && SpecificDiagnosticOptions is not null;
}

internal sealed record CachedParseOptions(
    LanguageVersion LanguageVersion,
    DocumentationMode DocumentationMode,
    SourceCodeKind Kind,
    IReadOnlyList<string> PreprocessorSymbols,
    IReadOnlyList<KeyValuePair<string, string>> Features)
{
    public bool IsWellFormed() => PreprocessorSymbols is not null && Features is not null;
}
