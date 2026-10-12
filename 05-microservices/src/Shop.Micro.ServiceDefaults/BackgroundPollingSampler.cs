using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Shop.Micro.ServiceDefaults;

/// <summary>
/// Keeps the dashboard readable by dropping two kinds of spans nobody asked to see:
/// <list type="bullet">
/// <item>a <b>client</b> call that would start a trace of its own: a database query from background work,
/// such as the outbox dispatcher checking for unsent rows every few seconds;</item>
/// <item>a child of a span that was not recorded: the database query of a <c>/health</c> probe, whose request
/// span is filtered out.</item>
/// </list>
/// Everything that belongs to a request or a message is kept. Without this the dashboard lists hundreds of
/// one-span "postgresql" traces and the order traces drown. Guide: §8.3.
/// </summary>
internal sealed class BackgroundPollingSampler : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        var parent = samplingParameters.ParentContext;
        var drop = parent.TraceId == default
            ? samplingParameters.Kind == ActivityKind.Client
            : !parent.TraceFlags.HasFlag(ActivityTraceFlags.Recorded);
        return new SamplingResult(drop ? SamplingDecision.Drop : SamplingDecision.RecordAndSample);
    }
}
