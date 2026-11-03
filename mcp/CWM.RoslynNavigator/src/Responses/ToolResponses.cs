using System.Text.Json.Serialization;

namespace CWM.RoslynNavigator.Responses;

// Token-optimized response records for MCP tools.
// All responses use minimal property names and avoid unnecessary nesting.
//
// List-returning tools share one paging contract, produced by Responses.Paging:
//   Count      entries in this response
//   TotalFound entries that matched in total
//   Truncated  true when the cap dropped entries — the single field to branch on
//   Limit      the cap that was applied; re-query above it to see the rest
// Truncated is stated rather than left to a Count/TotalFound comparison so a caller
// never has to infer whether it is looking at a complete answer.
//
// Navigation results carry IsGenerated so a caller can skip source-generator and
// tooling output rather than trying to edit a file that will be regenerated.
//
// Note: intentionally many records in one file — these are pure DTOs and a file per
// two-line record would hurt navigability more than it helps.

/// <summary>
/// Stable, enumerated failure codes. Callers branch on <see cref="ErrorResponse.Error"/>
/// rather than parsing prose, and — critically — can tell "this symbol does not exist"
/// apart from "this symbol exists and has no results".
/// </summary>
internal static class ErrorCodes
{
    public const string SymbolNotFound = "SymbolNotFound";
    public const string AmbiguousMatch = "AmbiguousMatch";
    public const string WrongSymbolKind = "WrongSymbolKind";
    public const string FileNotFound = "FileNotFound";
    public const string NoSource = "NoSource";
    public const string InvalidArgument = "InvalidArgument";
}

/// <summary>
/// One of several symbols a qualified name could have meant. <see cref="SymbolId"/> can be
/// passed back as the name to pick exactly this one.
/// </summary>
internal sealed record SymbolCandidate(
    string Qualified,
    string Kind,
    string File,
    int Line,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SymbolId = null);

/// <summary>
/// Structured failure. <see cref="Candidates"/> is populated for AmbiguousMatch so the
/// caller can re-query with a fully-qualified name instead of guessing.
/// <see cref="DidYouMean"/> is populated for SymbolNotFound with up to five close names.
/// </summary>
internal sealed record ErrorResponse(
    string Error,
    string Message,
    List<SymbolCandidate>? Candidates = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<SymbolCandidate>? DidYouMean = null);

/// <summary>
/// <see cref="Match"/> (exact / prefix / camelhump / substring) appears only for a fuzzy
/// search, where results are ranked by it.
/// </summary>
internal sealed record SymbolLocation(
    string Name,
    string Kind,
    string File,
    int Line,
    string Namespace,
    bool IsGenerated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Match = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContainingType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SymbolId = null);

internal sealed record SymbolSearchResult(List<SymbolLocation> Symbols, int Count, int TotalFound, bool Truncated, int Limit);

internal sealed record ReferenceLocation(string File, int Line, string Snippet, string Kind, bool IsGenerated);

internal sealed record ReferencesResult(List<ReferenceLocation> References, int Count, int TotalFound, bool Truncated, int Limit);

internal sealed record ImplementationInfo(string Type, string File, int Line, bool IsGenerated);

internal sealed record ImplementationsResult(List<ImplementationInfo> Implementations, int Count, int TotalFound, bool Truncated, int Limit);

internal sealed record TypeHierarchyResult(
    List<string> BaseTypes,
    List<string> Interfaces,
    List<string> DerivedTypes,
    int TotalDerived,
    bool Truncated,
    int Limit,
    string? File = null,
    int Line = 0);

internal sealed record ProjectInfo(
    string Name,
    string Path,
    string TargetFramework,
    List<string> References,
    // Multi-solution only: the solution(s) declaring this project. Omitted for one solution.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Solutions = null);

/// <summary>A loaded solution as reported by get_project_graph when several are loaded.</summary>
internal sealed record SolutionSummary(
    string Name,
    string Path,
    int ProjectCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null);

/// <summary>
/// <see cref="Solution"/> is the primary solution's file name (unchanged for one solution).
/// <see cref="Solutions"/> appears only when several solutions are loaded.
/// </summary>
internal sealed record ProjectGraphResult(
    string Solution,
    List<ProjectInfo> Projects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<SolutionSummary>? Solutions = null);

internal sealed record MemberInfo(string Kind, string Signature, string Accessibility);

internal sealed record PublicApiResult(
    string Type,
    List<MemberInfo> Members,
    int Count,
    int TotalFound,
    bool Truncated,
    int Limit,
    string? File = null,
    int Line = 0);

internal sealed record DiagnosticInfo(string Id, string Severity, string Message, string File, int Line);

internal sealed record DiagnosticsResult(
    List<DiagnosticInfo> Diagnostics,
    int Count,
    int TotalFound,
    int Errors,
    int Warnings,
    int Info,
    bool Truncated,
    int Limit);

internal sealed record StatusResponse(string State, string Message);

// Workspace health
internal sealed record ProjectHealthInfo(string Name, string Path, int Documents, List<string> Issues);

internal sealed record LoadDiagnosticInfo(
    string Kind,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Project = null);

/// <summary>
/// What the server has loaded and how completely. <see cref="Healthy"/> is false when any
/// project loaded or compiled incompletely, or the workspace reported a load failure — the
/// case in which an empty answer from another tool may be a false negative.
/// Count/TotalFound describe <see cref="IncompleteProjects"/>. <see cref="Reloading"/> is true while a
/// background reload builds the next snapshot (answers meanwhile come from the current one);
/// <see cref="LastReloadError"/> is set when the last reload failed and the previous snapshot was kept.
/// </summary>
internal sealed record WorkspaceStatusResult(
    string State,
    string Message,
    bool Healthy,
    int SnapshotVersion,
    int LoadCount,
    List<SolutionSummary> Solutions,
    int ProjectCount,
    int DocumentCount,
    WarmUpProgress WarmUp,
    List<ProjectHealthInfo> IncompleteProjects,
    int Count,
    int TotalFound,
    List<LoadDiagnosticInfo> LoadDiagnostics,
    int LoadDiagnosticCount,
    bool Truncated,
    int Limit,
    bool Reloading = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReloadReason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastReloadError = null);

/// <summary>
/// <see cref="Via"/> is "direct" when the call binds to the requested method itself, or
/// "interface" / "base" / "implementation" when it binds to a related member that dispatches
/// to it — <see cref="ViaSymbol"/> then names that member ("IOrderRepository.GetByIdAsync").
/// </summary>
internal sealed record CallerInfo(
    string Method,
    string ContainingType,
    string File,
    int Line,
    bool IsGenerated,
    string Via = "direct",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ViaSymbol = null);

internal sealed record CallersResult(List<CallerInfo> Callers, int Count, int TotalFound, bool Truncated, int Limit);

internal sealed record OverrideInfo(string Method, string ContainingType, string File, int Line, bool IsGenerated);

internal sealed record OverridesResult(List<OverrideInfo> Overrides, int Count, int TotalFound, bool Truncated, int Limit);

internal sealed record ParameterDetail(string Name, string Type, string? DefaultValue);

internal sealed record SymbolDetail(
    string Name,
    string Kind,
    string Signature,
    string? ReturnType,
    string Accessibility,
    string? Namespace,
    List<ParameterDetail>? Parameters,
    string? XmlDoc,
    string File,
    int Line);

internal sealed record AntiPatternInfo(
    string Id,
    string Severity,
    string Message,
    string File,
    int Line,
    string Snippet,
    string Suggestion,
    string Confidence = "high",
    string? Member = null,
    string SourceKind = "production");

/// <summary>Per-detector counts, complete even when the violation list is truncated.</summary>
internal sealed record AntiPatternIdSummary(
    string Id,
    int High,
    int Medium,
    int Suppressed,
    int Total);

/// <summary>
/// The composition of a scan. Lets a caller judge signal without sampling the violation list:
/// grade on <see cref="High"/>, review <see cref="Medium"/>, ignore <see cref="Suppressed"/>.
/// </summary>
internal sealed record AntiPatternSummary(
    int High,
    int Medium,
    int Suppressed,
    List<AntiPatternIdSummary> ById,
    int ScannedFiles,
    int ProductionFiles,
    int TestFiles,
    int GeneratedFiles,
    int MigrationFiles,
    string? SuppressionConfig,
    string GradeOn = "high-confidence findings only");

internal sealed record AntiPatternsResult(
    List<AntiPatternInfo> Violations,
    int Count,
    int TotalFound,
    bool Truncated,
    int Limit,
    AntiPatternSummary? Summary = null);

// Dead code detection
internal sealed record DeadCodeInfo(
    string Name,
    string Kind,
    string File,
    int Line,
    string? ContainingType,
    string Confidence = "high",
    string? Note = null);

internal sealed record DeadCodeResult(
    List<DeadCodeInfo> Symbols,
    int Count,
    int TotalFound,
    bool Truncated,
    int Limit,
    int ConventionFiltered = 0,
    bool AssemblyScanningDetected = false);

// Circular dependency detection
internal sealed record CircularDependencyChain(List<string> Chain, string Level);

internal sealed record CircularDependenciesResult(List<CircularDependencyChain> Cycles, int Count);

// Dependency graph
internal sealed record DependencyNode(string Symbol, string ContainingType, string File, int Line, int Depth);

internal sealed record DependencyGraphResult(
    string RootSymbol,
    List<DependencyNode> Dependencies,
    int TotalNodes,
    bool Truncated);

// Test coverage map
internal sealed record TestCoverageEntry(string Type, string File, bool HasTests, string? TestFile);

internal sealed record TestCoverageMapResult(
    List<TestCoverageEntry> Coverage,
    int TotalTypes,
    int TestedTypes,
    int Percentage,
    bool Applicable = true,
    string? NotApplicableReason = null,
    int TestMethodCount = 0,
    int TestClassCount = 0);

// Token savings
internal sealed record ToolUsage(
    string Tool,
    int Calls,
    int Saved,
    long MedianMs,
    int ZeroResultPct);

/// <summary>
/// Methodology disclosure. Travels with the numbers rather than living only in a README,
/// because a savings figure means nothing without the assumptions behind it.
/// </summary>
internal sealed record SavingsMethod(
    string Baseline,
    double BytesPerToken,
    bool DivisorCalibrated,
    int CalibrationSampleSize,
    string Dedup,
    string Excluded);

internal sealed record TokenSavingsResult(
    bool Enabled,
    string Message,
    int TotalCalls,
    int SavedTokens,
    int ResponseTokens,
    int BaselineTokens,
    int FilesCounted,
    int RepoTokens,
    int ZeroResultCalls,
    int NotReadyCalls,
    int ErrorCalls,
    List<ToolUsage> ByTool,
    SavingsMethod? Method);

// Change impact analysis
internal sealed record ImpactProject(string Name, int References, bool IsTest);

internal sealed record ImpactFile(string File, int References, bool IsTest);

/// <summary>
/// The blast radius of changing one symbol. Answers "what breaks and how far does it
/// reach" in a single call, so a caller does not have to stitch together find_references,
/// find_implementations, find_overrides, and the project graph.
/// </summary>
internal sealed record ChangeImpactResult(
    string Symbol,
    string Kind,
    string Accessibility,
    bool CrossesAssemblyBoundary,
    int DirectReferences,
    int AffectedProjects,
    int AffectedFiles,
    int TestReferences,
    int ImplementationsToUpdate,
    int OverridesToUpdate,
    List<ImpactProject> Projects,
    List<ImpactFile> Files,
    List<string> TransitiveCallers,
    int TransitiveCallerCount,
    bool Truncated,
    int Limit,
    string Risk,
    string Rationale,
    // Of TransitiveCallerCount, how many reach the symbol only through an interface or base member.
    int IndirectCallers = 0);

// Stack trace resolution
internal sealed record StackFrameInfo(
    int Index,
    string Method,
    bool InSolution,
    string? File,
    int? Line,
    int? DeclarationLine,
    string? Snippet);

internal sealed record StackTraceResult(
    string? ExceptionType,
    string? Message,
    List<StackFrameInfo> Frames,
    int Count,
    int TotalFound,
    bool Truncated,
    int Limit,
    int SolutionFrames,
    int? FirstSolutionFrame);

// Position lookup
/// <summary>
/// The symbol at a file position. <see cref="Token"/>/<see cref="Line"/>/<see cref="Column"/>
/// echo what was actually resolved, so a caller can confirm it hit the identifier it meant.
/// Type fields: <see cref="DeclaredType"/> is the symbol's own type (a local's, a property's,
/// a method's return type) with <see cref="DeclaredNullability"/>; <see cref="ExpressionType"/>
/// is the compiler's type for the expression at the position, with its flow-state nullability.
/// </summary>
internal sealed record SymbolAtResult(
    string Token,
    int Line,
    int Column,
    string Name,
    string Kind,
    string Signature,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContainingType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Namespace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SymbolId,
    bool IsDeclaration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeclaredType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeclaredNullability,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpressionType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpressionNullability,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ConvertedType,
    string DefinitionFile,
    int DefinitionLine,
    bool IsGenerated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Assembly = null);

// Rename preview
/// <summary>One changed line: its text before and after the rename, trimmed.</summary>
internal sealed record RenameEdit(string File, int Line, string Old, string New);

/// <summary>
/// What a rename would change. Count/TotalFound/Truncated describe <see cref="Changes"/>;
/// <see cref="Conflicts"/> are compiler errors the rename would introduce (capped at 20,
/// <see cref="ConflictCount"/> is the full number).
/// </summary>
internal sealed record RenamePreviewResult(
    string Symbol,
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SymbolId,
    string OldName,
    string NewName,
    List<RenameEdit> Changes,
    int Count,
    int TotalFound,
    int Files,
    bool Truncated,
    int Limit,
    List<DiagnosticInfo> Conflicts,
    int ConflictCount,
    string Note);

// Symbol source
internal sealed record SymbolSourceResult(
    string Name,
    string Kind,
    string File,
    int StartLine,
    int EndLine,
    string Source,
    bool Truncated);

// File outline
internal sealed record MemberOutline(string Kind, string Signature, int Line);

internal sealed record TypeOutline(
    string Name,
    string Kind,
    int Line,
    List<MemberOutline> Members,
    List<TypeOutline> NestedTypes);

internal sealed record FileOutlineResult(
    string File,
    string? Namespace,
    int UsingCount,
    List<TypeOutline> Types,
    int Count,
    int TotalFound,
    bool Truncated,
    int Limit);

// NuGet packages
internal sealed record PackageRef(string Id, string? Version);

internal sealed record ProjectPackages(string Name, string TargetFramework, bool Cpm, List<PackageRef> Packages);

internal sealed record NugetPackagesResult(List<ProjectPackages> Projects, int Count, int TotalFound, bool Truncated, int Limit);

// Endpoint map
internal sealed record EndpointEntry(string Method, string Route, string Auth, string Kind, string File, int Line);

internal sealed record EndpointMapResult(List<EndpointEntry> Endpoints, int Count, int TotalFound, bool Truncated, int Limit);

// DI registrations
internal sealed record DiRegistration(
    string Service,
    string Implementation,
    string Lifetime,
    bool Keyed,
    bool TryAdd,
    string File,
    int Line);

internal sealed record DiDuplicate(string Service, int Count);

internal sealed record DiCaptiveRisk(string Service, string Implementation, string DependsOn, string File, int Line);

internal sealed record DiRegistrationsResult(
    List<DiRegistration> Registrations,
    List<DiDuplicate> Duplicates,
    List<DiCaptiveRisk> CaptiveRisks,
    int Count,
    int TotalFound,
    bool Truncated,
    int Limit,
    string? Note = null);
