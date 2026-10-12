using System.Diagnostics;
using System.Text.Json;
var rows = new List<object>();
foreach (string mode in new[] { "unstarted-w3c", "unstarted-parent-w3c", "started-w3c", "parent-w3c", "parent-zero-trace", "parent-zero-span", "hierarchical", "stopped-current" })
{
    Activity.Current = null;
    using var activity = new Activity(mode);
    activity.SetIdFormat(mode == "hierarchical" ? ActivityIdFormat.Hierarchical : ActivityIdFormat.W3C);
    if (mode is "parent-w3c" or "unstarted-parent-w3c") { activity.SetParentId(ActivityTraceId.CreateFromString("11111111111111111111111111111111"), ActivitySpanId.CreateFromString("2222222222222222")); }
    if (mode == "parent-zero-trace") { activity.SetParentId(default(ActivityTraceId), ActivitySpanId.CreateFromString("2222222222222222")); }
    if (mode == "parent-zero-span") { activity.SetParentId(ActivityTraceId.CreateFromString("11111111111111111111111111111111"), default(ActivitySpanId)); }
    if (mode is not ("unstarted-w3c" or "unstarted-parent-w3c")) { activity.Start(); }
    if (mode == "stopped-current") { activity.Stop(); }
    Activity.Current = activity;
    rows.Add(new { mode, currentIsSource = ReferenceEquals(Activity.Current, activity), format = activity.IdFormat.ToString(), traceId = activity.TraceId.ToHexString(), spanId = activity.SpanId.ToHexString(), duration = activity.Duration.Ticks, parentId = activity.ParentSpanId.ToHexString() });
    Activity.Current = null;
}
Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
