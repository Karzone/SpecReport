using System.Text.Json;
using SpecReport.Core.Model;

namespace SpecReport.Core.Parsing;

/// <summary>
/// Turns a Cucumber Messages NDJSON file into a <see cref="RunModel"/>.
///
/// Two passes over the stream, because execution envelopes reference definitions that may
/// appear later and holding every envelope in memory is exactly what SPEC §8.7 forbids:
///   1. definitions - meta, gherkinDocument, pickle, testCase, hook, testRunStarted/Finished
///   2. execution   - testCaseStarted/Finished, testStepFinished, attachment
/// </summary>
public sealed class RunParser(AttachmentSpool spool)
{
    /// <summary>The Cucumber Messages version this build was written against. A mismatch warns;
    /// it never fails (SPEC §7.1).</summary>
    private const string ExpectedProtocolMajor = "30";

    private readonly GherkinIndex _gherkin = new();
    private readonly Dictionary<string, PickleInfo> _pickles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TestCaseInfo> _testCases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HookInfo> _hooks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AttemptState> _attempts = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _runMeta = new(StringComparer.Ordinal);
    private RunMetadata _metadata = new();
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;

    public static RunModel Parse(string path, AttachmentSpool spool)
        => new RunParser(spool).ParseFile(path);

    private RunModel ParseFile(string path)
    {
        var diagnostics = new ParseDiagnostics();

        foreach (var document in NdjsonReader.Read(path, diagnostics))
        {
            using (document)
            {
                var root = document.RootElement;
                var type = root.EnvelopeType();
                if (type is null) continue;

                if (!ReadDefinition(type, root, diagnostics) &&
                    !ReadExecution(type, root))
                {
                    // Unknown envelope: recorded, never fatal, so a future Reqnroll release
                    // that adds envelope types does not break SpecReport.
                    diagnostics.UnknownEnvelopeTypes.Add(type);
                }
            }
        }

        diagnostics.RunDidNotComplete = _finishedAt is null;
        diagnostics.UnfinishedScenarios = _attempts.Values.Count(a => a.FinishedAt is null);

        var features = BuildFeatures();

        return new RunModel
        {
            StartedAt = _startedAt,
            FinishedAt = _finishedAt,
            // runmeta attachments arrive during the execution pass, after `meta`, so the
            // metadata record is finalised here rather than when `meta` was read.
            Metadata = _runMeta.Count == 0
                ? _metadata
                : WithCustom(_metadata, _runMeta),
            Features = features,
            Diagnostics = diagnostics
        };
    }

    // ---- pass 1: definitions ---------------------------------------------------------------

    private bool ReadDefinition(string type, JsonElement root, ParseDiagnostics diagnostics)
    {
        switch (type)
        {
            case "meta":
                ReadMeta(root.GetProperty("meta"), diagnostics);
                return true;

            case "testRunStarted":
                // The run clock. `meta` has no timestamp (measured - api-notes §7).
                _startedAt = root.GetProperty("testRunStarted").Timestamp("timestamp");
                return true;

            case "testRunFinished":
                _finishedAt = root.GetProperty("testRunFinished").Timestamp("timestamp");
                return true;

            case "gherkinDocument":
                _gherkin.Add(root.GetProperty("gherkinDocument"));
                return true;

            case "pickle":
                ReadPickle(root.GetProperty("pickle"));
                return true;

            case "testCase":
                ReadTestCase(root.GetProperty("testCase"));
                return true;

            case "hook":
                ReadHook(root.GetProperty("hook"));
                return true;

            // Known but not needed for the model.
            case "source":
            case "stepDefinition":
            case "parameterType":
            case "undefinedParameterType":
            case "suggestion":
            case "testRunHookStarted":
            case "testRunHookFinished":
                return true;

            default:
                return false;
        }
    }

    private void ReadMeta(JsonElement meta, ParseDiagnostics diagnostics)
    {
        var protocol = meta.Str("protocolVersion");

        if (protocol is not null && !protocol.StartsWith(ExpectedProtocolMajor + ".", StringComparison.Ordinal))
        {
            diagnostics.ProtocolVersionWarning =
                $"stream uses Cucumber Messages {protocol}, this build targets {ExpectedProtocolMajor}.x";
        }

        var ci = meta.Prop("ci");
        var git = ci?.Prop("git");

        _metadata = new RunMetadata
        {
            ProtocolVersion = protocol,
            Implementation = meta.Prop("implementation")?.Str("name"),
            ImplementationVersion = meta.Prop("implementation")?.Str("version"),
            Runtime = meta.Prop("runtime")?.Str("name"),
            RuntimeVersion = meta.Prop("runtime")?.Str("version"),
            OperatingSystem = meta.Prop("os") is { } os
                ? string.Join(" ", new[] { os.Str("name"), os.Str("version") }.Where(s => s is not null))
                : null,
            Cpu = meta.Prop("cpu")?.Str("name"),
            CiName = ci?.Str("name"),
            CiUrl = ci?.Str("url"),
            CommitSha = git?.Str("revision"),
            Branch = git?.Str("branch")
        };
    }

    private sealed record PickleInfo(
        string Id,
        string Name,
        string Uri,
        IReadOnlyList<string> AstNodeIds,
        IReadOnlyList<string> Tags,
        IReadOnlyList<PickleStep> Steps);

    private sealed record PickleStep(
        string Id, string Text, IReadOnlyList<string> AstNodeIds,
        DataTable? DataTable, string? DocString);

    private void ReadPickle(JsonElement pickle)
    {
        var id = pickle.Str("id");
        if (id is null) return;

        var steps = new List<PickleStep>();
        foreach (var step in pickle.Array("steps"))
        {
            var stepId = step.Str("id");
            if (stepId is null) continue;

            steps.Add(new PickleStep(
                Id: stepId,
                Text: step.Str("text") ?? string.Empty,
                AstNodeIds: step.StringArray("astNodeIds"),
                DataTable: ReadDataTable(step),
                DocString: step.Prop("argument")?.Prop("docString")?.Str("content")));
        }

        _pickles[id] = new PickleInfo(
            Id: id,
            Name: pickle.Str("name") ?? string.Empty,
            Uri: pickle.Str("uri") ?? string.Empty,
            AstNodeIds: pickle.StringArray("astNodeIds"),
            Tags: pickle.Array("tags")
                        .Select(t => t.Str("name"))
                        .Where(n => n is not null)
                        .Select(n => n!.TrimStart('@'))
                        .ToList(),
            Steps: steps);
    }

    private static DataTable? ReadDataTable(JsonElement step)
    {
        var table = step.Prop("argument")?.Prop("dataTable");
        if (table is null) return null;

        var rows = table.Value.Array("rows")
            .Select(r => (IReadOnlyList<string>)r.Array("cells")
                                                 .Select(c => c.Str("value") ?? string.Empty)
                                                 .ToList())
            .ToList();

        return rows.Count == 0 ? null : new DataTable { Rows = rows };
    }

    private sealed record TestCaseInfo(string Id, string PickleId, IReadOnlyList<TestStepInfo> Steps);
    private sealed record TestStepInfo(string Id, string? PickleStepId, string? HookId);

    private void ReadTestCase(JsonElement testCase)
    {
        var id = testCase.Str("id");
        var pickleId = testCase.Str("pickleId");
        if (id is null || pickleId is null) return;

        var steps = new List<TestStepInfo>();
        foreach (var step in testCase.Array("testSteps"))
        {
            var stepId = step.Str("id");
            if (stepId is null) continue;

            steps.Add(new TestStepInfo(
                Id: stepId,
                PickleStepId: Empty(step.Str("pickleStepId")),
                HookId: Empty(step.Str("hookId"))));
        }

        _testCases[id] = new TestCaseInfo(id, pickleId, steps);
    }

    private sealed record HookInfo(string Id, HookKind Kind, string? TagExpression);

    private void ReadHook(JsonElement hook)
    {
        var id = hook.Str("id");
        if (id is null) return;

        // `hook.name` is always empty (measured - api-notes §7.1), so type + tag expression is
        // all there is to label a hook row with.
        _hooks[id] = new HookInfo(
            Id: id,
            Kind: hook.Str("type") switch
            {
                "BEFORE_TEST_RUN" => HookKind.BeforeTestRun,
                "AFTER_TEST_RUN" => HookKind.AfterTestRun,
                "BEFORE_TEST_CASE" => HookKind.BeforeTestCase,
                "AFTER_TEST_CASE" => HookKind.AfterTestCase,
                "BEFORE_TEST_STEP" => HookKind.BeforeTestStep,
                "AFTER_TEST_STEP" => HookKind.AfterTestStep,
                _ => HookKind.Unknown
            },
            TagExpression: Empty(hook.Str("tagExpression")));
    }

    // ---- pass 2: execution -----------------------------------------------------------------

    private sealed class AttemptState
    {
        public required string TestCaseId { get; init; }
        public int AttemptNumber { get; init; }
        public DateTimeOffset? StartedAt { get; init; }
        public DateTimeOffset? FinishedAt { get; set; }
        public Dictionary<string, StepResult> Results { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<Evidence>> EvidenceByStep { get; } = new(StringComparer.Ordinal);
        public List<Evidence> OutsideStepEvidence { get; } = [];
    }

    private sealed record StepResult(StepStatus Status, TimeSpan Duration, StepError? Error);

    private bool ReadExecution(string type, JsonElement root)
    {
        switch (type)
        {
            case "testCaseStarted":
            {
                var started = root.GetProperty("testCaseStarted");
                var id = started.Str("id");
                var testCaseId = started.Str("testCaseId");
                if (id is null || testCaseId is null) return true;

                _attempts[id] = new AttemptState
                {
                    TestCaseId = testCaseId,
                    AttemptNumber = started.Int("attempt") ?? 0,
                    StartedAt = started.Timestamp("timestamp")
                };
                return true;
            }

            case "testCaseFinished":
            {
                var finished = root.GetProperty("testCaseFinished");
                var id = finished.Str("testCaseStartedId");
                if (id is not null && _attempts.TryGetValue(id, out var attempt))
                    attempt.FinishedAt = finished.Timestamp("timestamp");
                return true;
            }

            case "testStepFinished":
            {
                var finished = root.GetProperty("testStepFinished");
                var attemptId = finished.Str("testCaseStartedId");
                var stepId = finished.Str("testStepId");
                if (attemptId is null || stepId is null) return true;
                if (!_attempts.TryGetValue(attemptId, out var attempt)) return true;

                var result = finished.Prop("testStepResult");
                if (result is null) return true;

                attempt.Results[stepId] = new StepResult(
                    Status: ParseStatus(result.Value.Str("status")),
                    Duration: result.Value.Duration("duration"),
                    Error: ReadError(result.Value));
                return true;
            }

            case "attachment":
                ReadAttachment(root.GetProperty("attachment"));
                return true;

            case "testStepStarted":
                return true;   // start times are not used; durations come from the result

            default:
                return false;
        }
    }

    private static StepError? ReadError(JsonElement result)
    {
        var exception = result.Prop("exception");
        var message = result.Str("message");

        if (exception is null && message is null) return null;

        return new StepError
        {
            Type = exception?.Str("type"),
            // `message` on the result is only the exception message; the stack trace lives on
            // `exception` (measured), so both are read.
            Message = exception?.Str("message") ?? message,
            StackTrace = exception?.Str("stackTrace")
        };
    }

    private void ReadAttachment(JsonElement attachment)
    {
        var attemptId = attachment.Str("testCaseStartedId");
        if (attemptId is null || !_attempts.TryGetValue(attemptId, out var attempt)) return;

        var mediaType = attachment.Str("mediaType") ?? "application/octet-stream";
        var body = attachment.Str("body") ?? string.Empty;
        var isBase64 = attachment.Str("contentEncoding") == "BASE64";

        var evidence = EvidenceFactory.Create(
            mediaType, body, isBase64, attachment.Str("fileName"), _spool);

        // Run metadata describes the whole run, not a step - fold it into the header rather
        // than hanging it off whichever scenario happened to emit it (SPEC §3.1).
        if (evidence is { Kind: EvidenceKind.RunMeta, RunMeta: { } values })
        {
            foreach (var (key, value) in values) _runMeta[key] = value;
            return;
        }

        // An http/1 payload carries its OWN step index (Reqnroll's testStepId points at the
        // AfterStep hook, not the Gherkin step - api-notes §10), and out-of-step exchanges are
        // bucketed at scenario level rather than blamed on a step.
        if (evidence.Http is { Scope: "outside-steps" })
        {
            attempt.OutsideStepEvidence.Add(evidence);
            return;
        }

        var stepId = attachment.Str("testStepId");
        if (stepId is null)
        {
            attempt.OutsideStepEvidence.Add(evidence);
            return;
        }

        if (!attempt.EvidenceByStep.TryGetValue(stepId, out var list))
            attempt.EvidenceByStep[stepId] = list = [];

        list.Add(evidence);
    }

    private readonly AttachmentSpool _spool = spool;

    private static StepStatus ParseStatus(string? status) => status switch
    {
        "PASSED" => StepStatus.Passed,
        "SKIPPED" => StepStatus.Skipped,
        "PENDING" => StepStatus.Pending,
        "UNDEFINED" => StepStatus.Undefined,
        "AMBIGUOUS" => StepStatus.Ambiguous,
        "FAILED" => StepStatus.Failed,
        _ => StepStatus.Unknown
    };

    private static string? Empty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static RunMetadata WithCustom(RunMetadata source, Dictionary<string, string> custom) =>
        new()
        {
            ProtocolVersion = source.ProtocolVersion,
            Implementation = source.Implementation,
            ImplementationVersion = source.ImplementationVersion,
            Runtime = source.Runtime,
            RuntimeVersion = source.RuntimeVersion,
            OperatingSystem = source.OperatingSystem,
            Cpu = source.Cpu,
            CiName = source.CiName,
            CiUrl = source.CiUrl,
            CommitSha = source.CommitSha,
            Branch = source.Branch,
            Custom = custom
        };

    // ---- assembly --------------------------------------------------------------------------

    private IReadOnlyList<FeatureModel> BuildFeatures()
    {
        // Group attempts by test case, so retries collapse into one scenario (SPEC §4.5).
        var attemptsByTestCase = _attempts.Values
            .GroupBy(a => a.TestCaseId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.AttemptNumber).ToList(),
                          StringComparer.Ordinal);

        var scenarios = new List<(GherkinIndex.AstScenario? Ast, ScenarioModel Model)>();

        foreach (var (testCaseId, attempts) in attemptsByTestCase)
        {
            if (!_testCases.TryGetValue(testCaseId, out var testCase)) continue;
            if (!_pickles.TryGetValue(testCase.PickleId, out var pickle)) continue;

            var built = BuildScenario(pickle, testCase, attempts);
            if (built is not null) scenarios.Add(built.Value);
        }

        return GroupIntoFeatures(scenarios);
    }

    private (GherkinIndex.AstScenario? Ast, ScenarioModel Model)? BuildScenario(
        PickleInfo pickle, TestCaseInfo testCase, List<AttemptState> attempts)
    {
        // A pickle's astNodeIds is [scenarioNodeId] or, for an outline row,
        // [scenarioNodeId, exampleRowId] (measured).
        var astScenario = pickle.AstNodeIds.Count > 0 ? _gherkin.Scenario(pickle.AstNodeIds[0]) : null;
        var exampleRow = pickle.AstNodeIds.Count > 1 ? _gherkin.ExampleRow(pickle.AstNodeIds[1]) : null;

        var identity = new ScenarioIdentity(
            FeatureUri: pickle.Uri,
            RuleName: astScenario?.RuleName,
            Name: pickle.Name,
            // The example-row id distinguishes byte-identical rows, which a values-only key
            // would silently merge (SPEC §6.2).
            ExampleRowDiscriminator: exampleRow?.Id);

        var built = attempts
            .Select(a => BuildAttempt(pickle, testCase, a))
            .ToList();

        return (astScenario, new ScenarioModel
        {
            Identity = identity,
            Name = pickle.Name,
            Keyword = astScenario?.Keyword ?? "Scenario",
            Description = astScenario?.Description,
            Tags = pickle.Tags,
            ExampleValues = exampleRow?.Values ?? new Dictionary<string, string>(),
            Attempts = built
        });
    }

    private ScenarioAttempt BuildAttempt(
        PickleInfo pickle, TestCaseInfo testCase, AttemptState attempt)
    {
        var pickleStepsById = pickle.Steps.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var steps = new List<StepModel>();
        var sawFailure = false;

        // Evidence that must be re-homed onto a Gherkin step, keyed by that step's ordinal.
        //
        // Capture attaches from an [AfterStep] hook, so Reqnroll stamps the attachment with the
        // AFTER_TEST_STEP hook's id (api-notes §10). Those hook rows are dropped as noise - and
        // dropping them naively took their evidence with them, silently losing every
        // step-scoped HTTP exchange. The payload's own stepIndex is what puts it back.
        var rehomed = new Dictionary<int, List<Evidence>>();
        var gherkinOrdinal = 0;
        var lastGherkinOrdinal = -1;

        void Rehome(IReadOnlyList<Evidence> items)
        {
            foreach (var item in items)
            {
                var target = item.Http is { StepIndex: >= 0 } http ? http.StepIndex : lastGherkinOrdinal;
                if (target < 0) target = 0;

                if (!rehomed.TryGetValue(target, out var list)) rehomed[target] = list = [];
                list.Add(item);
            }
        }

        foreach (var testStep in testCase.Steps)
        {
            attempt.Results.TryGetValue(testStep.Id, out var result);
            attempt.EvidenceByStep.TryGetValue(testStep.Id, out var evidence);

            if (testStep.HookId is { } hookId)
            {
                var hook = _hooks.TryGetValue(hookId, out var h) ? h : null;

                // Per-step hook rows are noise in a timeline - they exist for every step and
                // carry nothing a reader wants. Scenario- and run-level hooks are kept because
                // a failing BeforeScenario is otherwise invisible (SPEC §4.3).
                if (hook?.Kind is HookKind.BeforeTestStep or HookKind.AfterTestStep &&
                    result?.Status != StepStatus.Failed)
                {
                    if (evidence is { Count: > 0 }) Rehome(evidence);
                    continue;
                }

                var hookStatus = result?.Status ?? StepStatus.Unknown;
                if (hookStatus == StepStatus.Failed) sawFailure = true;

                steps.Add(new StepModel
                {
                    Kind = StepKind.Hook,
                    HookKind = hook?.Kind ?? HookKind.Unknown,
                    HookTagExpression = hook?.TagExpression,
                    Status = hookStatus,
                    StatusSource = result is null ? StatusSource.Inferred : StatusSource.Reported,
                    Duration = result?.Duration ?? TimeSpan.Zero,
                    Error = result?.Error,
                    Evidence = evidence ?? []
                });

                continue;
            }

            if (testStep.PickleStepId is not { } pickleStepId ||
                !pickleStepsById.TryGetValue(pickleStepId, out var pickleStep))
            {
                continue;
            }

            var ast = pickleStep.AstNodeIds.Count > 0 ? _gherkin.Step(pickleStep.AstNodeIds[0]) : null;

            // The stream is authoritative. Inference is a fallback for truncated streams ONLY,
            // never an override, and is marked so it renders distinctly (SPEC §4.4).
            var status = result?.Status
                         ?? (sawFailure ? StepStatus.Skipped : StepStatus.Unknown);
            var source = result is not null ? StatusSource.Reported : StatusSource.Inferred;

            if (status == StepStatus.Failed) sawFailure = true;

            lastGherkinOrdinal = gherkinOrdinal;

            steps.Add(new StepModel
            {
                Kind = StepKind.Gherkin,
                Keyword = ast?.Keyword ?? string.Empty,
                KeywordType = ast?.KeywordType ?? string.Empty,
                Text = pickleStep.Text,
                IsBackground = ast?.IsBackground ?? false,
                Status = status,
                StatusSource = source,
                Duration = result?.Duration ?? TimeSpan.Zero,
                Error = result?.Error,
                DataTable = pickleStep.DataTable,
                DocString = pickleStep.DocString,
                Evidence = evidence ?? [],
                // Marks this row's position among Gherkin steps, so re-homed evidence can be
                // matched back to it below.
                Ordinal = gherkinOrdinal++
            });
        }

        if (rehomed.Count > 0) steps = MergeRehomedEvidence(steps, rehomed);

        return new ScenarioAttempt
        {
            AttemptNumber = attempt.AttemptNumber,
            StartedAt = attempt.StartedAt,
            FinishedAt = attempt.FinishedAt,
            Steps = steps,
            OutsideStepEvidence = attempt.OutsideStepEvidence
        };
    }

    /// <summary>
    /// Folds evidence recovered from dropped per-step hook rows onto the Gherkin steps that
    /// actually produced it, in capture order.
    /// </summary>
    private static List<StepModel> MergeRehomedEvidence(
        List<StepModel> steps, Dictionary<int, List<Evidence>> rehomed)
    {
        var merged = new List<StepModel>(steps.Count);

        foreach (var step in steps)
        {
            if (step.Kind != StepKind.Gherkin ||
                step.Ordinal < 0 ||
                !rehomed.TryGetValue(step.Ordinal, out var extra))
            {
                merged.Add(step);
                continue;
            }

            var combined = step.Evidence.Concat(
                extra.OrderBy(e => e.Http?.Sequence ?? int.MaxValue)).ToList();

            merged.Add(step.WithEvidence(combined));
        }

        return merged;
    }

    private IReadOnlyList<FeatureModel> GroupIntoFeatures(
        List<(GherkinIndex.AstScenario? Ast, ScenarioModel Model)> scenarios)
    {
        var features = new List<FeatureModel>();

        foreach (var group in scenarios.GroupBy(s => s.Model.Identity.FeatureUri, StringComparer.Ordinal))
        {
            var ast = _gherkin.Feature(group.Key);

            // Feature-file order, not execution order: under parallelism the stream arrives in
            // whatever order scenarios happened to finish (SPEC §4.2).
            var ordered = group.OrderBy(s => s.Ast?.Order ?? int.MaxValue).ToList();

            var ruleless = ordered
                .Where(s => s.Ast?.RuleName is null)
                .Select(s => s.Model)
                .ToList();

            var rules = ordered
                .Where(s => s.Ast?.RuleName is not null)
                .GroupBy(s => s.Ast!.RuleName!, StringComparer.Ordinal)
                .Select(g => new RuleModel
                {
                    Name = g.Key,
                    Scenarios = g.Select(s => s.Model).ToList()
                })
                .ToList();

            features.Add(new FeatureModel
            {
                Uri = group.Key,
                Name = ast?.Name ?? group.Key,
                Keyword = ast?.Keyword ?? "Feature",
                Language = ast?.Language ?? "en",
                Description = ast?.Description,
                Tags = ast?.Tags ?? [],
                Scenarios = ruleless,
                Rules = rules
            });
        }

        return features.OrderBy(f => f.Uri, StringComparer.Ordinal).ToList();
    }
}
