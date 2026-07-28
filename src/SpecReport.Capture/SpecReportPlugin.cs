using Reqnroll.Plugins;
using Reqnroll.UnitTestProvider;
using SpecReport.Capture;

[assembly: RuntimePlugin(typeof(SpecReportPlugin))]

namespace SpecReport.Capture;

/// <summary>
/// Auto-loads when a consumer merely references the SpecReport.Capture package - Reqnroll
/// discovers runtime plugins by scanning for assemblies whose FILE NAME ends in
/// `.ReqnrollPlugin.dll`, which is why this assembly is named
/// SpecReport.Capture.ReqnrollPlugin (see docs/reqnroll-api-notes.md 5).
///
/// It does two things:
///   1. Registers <see cref="ScenarioCapture"/> per scenario, so each concurrently running
///      scenario gets its own buffer.
///   2. Adds this assembly to the binding sources, so <see cref="CaptureHooks"/> is discovered
///      without the consumer having to add `bindingAssemblies` to their reqnroll.json. Zero
///      config is the difference between "reference the package" and "reference the package
///      and also read the docs and edit a JSON file".
/// </summary>
public sealed class SpecReportPlugin : IRuntimePlugin
{
    public void Initialize(
        RuntimePluginEvents runtimePluginEvents,
        RuntimePluginParameters runtimePluginParameters,
        UnitTestProviderConfiguration unitTestProviderConfiguration)
    {
        runtimePluginEvents.ConfigurationDefaults += (_, args) =>
        {
            var assemblyName = typeof(SpecReportPlugin).Assembly.GetName().Name;
            if (assemblyName != null &&
                !args.ReqnrollConfiguration.AdditionalStepAssemblies.Contains(assemblyName))
            {
                args.ReqnrollConfiguration.AdditionalStepAssemblies.Add(assemblyName);
            }
        };

        runtimePluginEvents.CustomizeScenarioDependencies += (_, args) =>
        {
            // Per-scenario lifetime is what keeps parallel scenarios from sharing a buffer.
            args.ObjectContainer.RegisterTypeAs<ScenarioCapture, ScenarioCapture>();
        };
    }
}
