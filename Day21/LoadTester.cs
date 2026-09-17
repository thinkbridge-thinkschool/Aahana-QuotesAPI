using System.Diagnostics;

namespace Day21;

public sealed record LoadTestResult(
    int RequestCount,
    double TotalSeconds,
    double RequestsPerSecond,
    double P50Ms,
    double P99Ms);

// Fires N requests at the same URL, all starting essentially at once
// (Task.WhenAll over already-created tasks, not a loop that awaits one
// at a time) - this is what makes it a genuine concurrency/stampede
// scenario rather than N sequential calls that happen to add up.
public static class LoadTester
{
    public static async Task<LoadTestResult> RunConcurrentAsync(
        HttpClient client,
        string path,
        int concurrency)
    {
        var latenciesMs = new double[concurrency];

        var overallStopwatch = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            var sw = Stopwatch.StartNew();
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            sw.Stop();
            latenciesMs[i] = sw.Elapsed.TotalMilliseconds;
        });

        await Task.WhenAll(tasks);

        overallStopwatch.Stop();

        Array.Sort(latenciesMs);

        return new LoadTestResult(
            RequestCount: concurrency,
            TotalSeconds: overallStopwatch.Elapsed.TotalSeconds,
            RequestsPerSecond: concurrency / overallStopwatch.Elapsed.TotalSeconds,
            P50Ms: Percentile(latenciesMs, 0.50),
            P99Ms: Percentile(latenciesMs, 0.99));
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        index = Math.Clamp(index, 0, sortedValues.Length - 1);
        return sortedValues[index];
    }
}
