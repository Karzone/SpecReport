using System.Text;

namespace SpecReport.Conformance;

/// <summary>
/// Writes a large Cucumber Messages stream for scale testing.
///
/// Generated at test time rather than committed: a 5,000-scenario stream is tens of megabytes,
/// and a fixture that large would dominate the repository while telling a reader nothing they
/// could not get from this file. The envelope shapes are copied from the recorded fixtures, so
/// it exercises the same parser paths.
/// </summary>
internal static class SyntheticRun
{
    /// <summary>Writes a stream with the given shape and returns the path. Caller deletes it.</summary>
    public static string Write(int scenarios, int stepsPerScenario, int featureCount = 50)
    {
        var path = Path.Combine(Path.GetTempPath(), $"specreport-scale-{Guid.NewGuid():N}.ndjson");
        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);

        writer.WriteLine("""{"meta":{"protocolVersion":"30.1.0","implementation":{"name":"Reqnroll","version":"3.3.4"},"runtime":{"name":"dotNet","version":"10.0.8"},"os":{"name":"Linux"},"cpu":{"name":"x64"}}}""");
        writer.WriteLine("""{"testRunStarted":{"timestamp":{"seconds":1785000000,"nanos":0},"id":"scale"}}""");

        var perFeature = (int)Math.Ceiling(scenarios / (double)featureCount);
        var scenarioIndex = 0;

        for (var f = 0; f < featureCount && scenarioIndex < scenarios; f++)
        {
            var uri = $"Features/Area{f:D3}.feature";
            var inThisFeature = Math.Min(perFeature, scenarios - scenarioIndex);

            // gherkinDocument for the whole feature, so the AST join has real work to do.
            var doc = new StringBuilder();
            doc.Append($$"""{"gherkinDocument":{"uri":"{{uri}}","feature":{"location":{"line":1},"language":"en","keyword":"Feature","name":"Area {{f}}","children":[""");

            for (var s = 0; s < inThisFeature; s++)
            {
                var id = scenarioIndex + s;
                if (s > 0) doc.Append(',');
                doc.Append($$"""{"scenario":{"id":"sc{{id}}","keyword":"Scenario","name":"Scenario {{id}} exercises the system","tags":[{"name":"@area{{f % 7}}"}],"steps":[""");

                for (var t = 0; t < stepsPerScenario; t++)
                {
                    if (t > 0) doc.Append(',');
                    var keyword = t == 0 ? "Given " : t == stepsPerScenario - 1 ? "Then " : "When ";
                    var type = t == 0 ? "Context" : t == stepsPerScenario - 1 ? "Outcome" : "Action";
                    doc.Append($$"""{"id":"st{{id}}_{{t}}","keyword":"{{keyword}}","keywordType":"{{type}}","text":"step {{t}} of scenario {{id}} does something measurable"}""");
                }

                doc.Append("]}}");
            }

            doc.Append("]}}}");
            writer.WriteLine(doc.ToString());

            for (var s = 0; s < inThisFeature; s++)
            {
                var id = scenarioIndex + s;

                var pickle = new StringBuilder();
                pickle.Append($$"""{"pickle":{"id":"pk{{id}}","uri":"{{uri}}","name":"Scenario {{id}} exercises the system","language":"en","tags":[{"name":"@area{{f % 7}}"}],"astNodeIds":["sc{{id}}"],"steps":[""");

                var testCase = new StringBuilder();
                testCase.Append($$"""{"testCase":{"id":"tc{{id}}","pickleId":"pk{{id}}","testSteps":[""");

                for (var t = 0; t < stepsPerScenario; t++)
                {
                    if (t > 0) { pickle.Append(','); testCase.Append(','); }
                    var type = t == 0 ? "Context" : t == stepsPerScenario - 1 ? "Outcome" : "Action";
                    pickle.Append($$"""{"id":"ps{{id}}_{{t}}","text":"step {{t}} of scenario {{id}} does something measurable","type":"{{type}}","astNodeIds":["st{{id}}_{{t}}"]}""");
                    testCase.Append($$"""{"id":"ts{{id}}_{{t}}","pickleStepId":"ps{{id}}_{{t}}"}""");
                }

                pickle.Append("]}}");
                testCase.Append("]}}");

                writer.WriteLine(pickle.ToString());
                writer.WriteLine(testCase.ToString());
            }

            scenarioIndex += inThisFeature;
        }

        // Execution. Every 13th scenario fails, so the failure paths - errors, stack traces,
        // expanded-by-default rendering - are exercised at scale rather than only the happy one.
        var clock = 1785000001L;

        // Built by concatenation rather than raw interpolated literals: these lines end in
        // several consecutive closing braces, which collide with `{{ }}` interpolation
        // delimiters and make the escaping harder to read than the string it produces.
        const string timestampSuffix = ",\"nanos\":0}}}";

        for (var id = 0; id < scenarios; id++)
        {
            var fails = id % 13 == 0;

            writer.WriteLine(
                "{\"testCaseStarted\":{\"id\":\"tcs" + id + "\",\"testCaseId\":\"tc" + id +
                "\",\"attempt\":0,\"timestamp\":{\"seconds\":" + clock + timestampSuffix);

            var failedYet = false;

            for (var t = 0; t < stepsPerScenario; t++)
            {
                var failingStep = fails && t == stepsPerScenario - 2;

                string result;
                if (failingStep)
                {
                    failedYet = true;
                    result =
                        "{\"status\":\"FAILED\",\"message\":\"assertion failed in scenario " + id +
                        "\",\"duration\":{\"seconds\":0,\"nanos\":2000000},\"exception\":{\"type\":" +
                        "\"AssertionException\",\"message\":\"assertion failed in scenario " + id +
                        "\",\"stackTrace\":\"   at Area.Steps.Then()\\n   at Runner.Invoke()\"}}";
                }
                else
                {
                    var status = failedYet ? "SKIPPED" : "PASSED";
                    result = "{\"status\":\"" + status +
                             "\",\"duration\":{\"seconds\":0,\"nanos\":1500000}}";
                }

                writer.WriteLine(
                    "{\"testStepFinished\":{\"testCaseStartedId\":\"tcs" + id +
                    "\",\"testStepId\":\"ts" + id + "_" + t + "\",\"testStepResult\":" + result +
                    ",\"timestamp\":{\"seconds\":" + clock + timestampSuffix);
            }

            writer.WriteLine(
                "{\"testCaseFinished\":{\"testCaseStartedId\":\"tcs" + id +
                "\",\"willBeRetried\":false,\"timestamp\":{\"seconds\":" + clock + timestampSuffix);

            clock++;
        }

        writer.WriteLine(
            "{\"testRunFinished\":{\"success\":false,\"timestamp\":{\"seconds\":" + clock +
            timestampSuffix);

        return path;
    }
}
