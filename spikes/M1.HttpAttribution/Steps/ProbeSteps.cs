using M1.HttpAttribution.Support;
using Reqnroll;

namespace M1.HttpAttribution.Steps;

[Binding]
public sealed class ProbeSteps(ScenarioState state)
{
    /// <summary>
    /// Every step makes an async HTTP call whose URL encodes the scenario and step it belongs
    /// to. That is what makes attribution checkable: the handler can compare what each ambient
    /// mechanism claims against ground truth carried in the request itself.
    /// </summary>
    [Given(@"step (.*) makes a call")]
    [When(@"step (.*) makes a call")]
    [Then(@"step (.*) makes a call")]
    public async Task StepMakesACall(string stepId)
    {
        await Task.Yield();
        var response = await state.Client!.GetAsync($"/probe/{state.ScenarioId}/{stepId}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Interleaves scenarios harder, so parallel flows genuinely overlap in time
    /// rather than each finishing before the next starts.</summary>
    [When(@"the scenario pauses briefly")]
    public static async Task PauseBriefly() => await Task.Delay(15);
}
