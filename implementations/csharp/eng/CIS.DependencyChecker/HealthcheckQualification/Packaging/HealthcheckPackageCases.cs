using System.Net;
using System.Text.Json.Nodes;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal sealed partial class HealthcheckPackageVerification
{
    private async Task CountryListAsync()
    {
        Consumer consumer = consumers["country-list"];
        foreach ((string option, string mode) in new[]
        {
            ("", "none"), ("--fail-prime", "priming"),
            ("--fail-refresh-registration", "registration"), ("--fail-later-startup", "later-startup")
        })
        {
            string[] arguments = option.Length == 0 ? ["--self-test"] : ["--self-test", option];
            ProcessObservation child = await RunChildAsync("country-list-" + mode, Execute(consumer, arguments), consumer.Output).ConfigureAwait(false);
            Require(child.ExitCode == 0 && !child.TimedOut, "Actual packaged CountryList failed its " + mode + " scenario.");
            JsonObject origin = ValidateOrigin(child, consumer);
            Require(RemoveOrigin(child.StandardError).Length == 0, "CountryList produced unexpected stderr.");
            JsonObject observed = JsonNode.Parse(child.StandardOutput)!.AsObject();
            Require((string?)observed["kind"] == "countryListSelfTest" && (string?)observed["runtime"] == ".NET 10.0.12"
                && (string?)observed["failureMode"] == mode && (bool?)observed["reportingBeforePriming"] == true, "CountryList did not report the selected actual scenario.");
            bool success = mode == "none";
            Require((bool?)observed["primeSucceeded"] == (mode != "priming") && (bool?)observed["startupSucceeded"] == success
                && (bool?)observed["refreshRegistered"] == success && (bool?)observed["actualOwnerDrain"] == success, "CountryList startup/owner outcomes differ.");
            JsonObject[] observations = observed["observations"]!.AsArray().Select(node => node!.AsObject()).ToArray();
            string[] stages = mode switch
            {
                "none" => ["before-prime", "priming", "after-prime", "startup-completed", "after-refresh-failures", "critical-flagged", "critical-cleared", "stopping"],
                "priming" => ["before-prime", "priming", "prime-failed", "stopping"],
                _ => ["before-prime", "priming", "after-prime", "startup-failed", "stopping"]
            };
            Require(observations.Select(row => (string)row["stage"]!).SequenceEqual(stages), "CountryList did not observe every expected real startup/shutdown boundary.");
            Dictionary<string, JsonObject> rows = observations.ToDictionary(row => (string)row["stage"]!, StringComparer.Ordinal);
            foreach (string stage in new[] { "before-prime", "priming" })
            {
                JsonObject row = rows[stage];
                Require((bool?)row["ready"] == false && (string?)row["healthStatus"] == "Healthy" && (string?)row["availabilityState"] == "Pending"
                    && (int?)row["pending"] == 1 && (int?)row["countries"] == 0 && (int?)row["businessRequests"] == 0,
                    "Reporting or business admission crossed required priming: " + stage);
            }
            if (mode != "priming")
            {
                JsonObject row = rows["after-prime"];
                Require((int?)row["countries"] == 3 && (string?)row["availabilityState"] == "Healthy"
                    && (bool?)row["ready"] == false && (int?)row["businessRequests"] == 0, "Usable data prematurely completed required startup.");
            }
            if (success)
            {
                foreach (string stage in new[] { "startup-completed", "after-refresh-failures", "critical-cleared" })
                {
                    JsonObject row = rows[stage];
                    Require((bool?)row["ready"] == true && (string?)row["healthStatus"] == "Healthy" && (string?)row["availabilityState"] == "Healthy"
                        && (int?)row["countries"] == 3 && (int?)row["businessRequests"] == 1, "Usable data or reporting admission changed after startup: " + stage);
                }
                Require((string?)rows["after-refresh-failures"]["refreshFailed"] == "3" && (string?)rows["after-refresh-failures"]["refreshState"] == "Unhealthy"
                    && (bool?)rows["critical-flagged"]["ready"] == false && (string?)rows["critical-flagged"]["healthStatus"] == "Unhealthy"
                    && (int?)rows["critical-flagged"]["criticalFailing"] == 1, "Refresh and independent critical condition did not retain their separate effects.");
            }
            else
            {
                JsonObject failed = rows[mode == "priming" ? "prime-failed" : "startup-failed"];
                Require((string?)failed["healthStatus"] == "Unhealthy" && (bool?)failed["ready"] == false && (int?)failed["criticalFailing"] == 1
                    && (int?)failed["pending"] == 0 && (int?)failed["countries"] == (mode == "priming" ? 0 : 3)
                    && (int?)failed["businessRequests"] == 0 && failed["refreshState"] is null, "Failed required startup lost its retained critical failure or admitted business traffic.");
                Require(mode == "priming" ? (int?)failed["active"] == 1 && failed["availabilityState"] is null : (string?)failed["availabilityState"] == "Healthy",
                    "Failed child withdrawal erased the independent application failure or later failure erased actually usable data.");
            }
            Require((bool?)rows["stopping"]["ready"] == false, "Stopping remained ready.");
            JsonObject drain = observed["ownerDrain"]!.AsObject();
            Require((bool?)drain["applicable"] == success && (bool?)drain["disposalPending"] == success
                && (bool?)drain["sourceDisposedBeforeRelease"] == false && (bool?)drain["sourceDisposedAfterRelease"] == success
                && (bool?)observed["passed"] == true, "Actual owner lease did not retain dependencies through settlement.");
            Passed("country-list-" + mode, new JsonObject
            {
                ["origin"] = origin,
                ["observed"] = observed,
                ["sourceTagNote"] = "The actual copied source retains its development-example label; package consumption is independently proven by assets and loaded origin."
            });
        }
    }

    private async Task TransportAsync()
    {
        Consumer host = consumers["http-host"];
        await using (var fixture = new OwnedProcess(this, "fixture-live", Execute(host, "pipe-live"), host.Output))
        {
            JsonObject ready = await fixture.ReadObjectAsync().ConfigureAwait(false);
            (Uri address, string pipe) = FixtureAddress(ready);
            using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15) };
            await HttpAsync("live-health", client, "/_health", HttpStatusCode.OK).ConfigureAwait(false);
            await HttpAsync("live-readiness", client, "/_health/ready", HttpStatusCode.OK).ConfigureAwait(false);
            await HttpAsync("live-details", client, "/_health/details", HttpStatusCode.OK).ConfigureAwait(false);
            await HttpAsync("live-details-denied", client, "/_health/details", HttpStatusCode.Forbidden, deny: true).ConfigureAwait(false);
            await HttpAsync("live-head", client, "/_health", HttpStatusCode.OK, head: true).ConfigureAwait(false);
            JsonObject initial = await fixture.CommandAsync("state").ConfigureAwait(false);
            Require((int?)initial["captures"] == 4 && (int?)initial["businessCalls"] == 0 && (int?)initial["authorizationCalls"] == 2,
                "Reporting did not capture exactly once or bypass business middleware/denied details capture.");
            foreach (string view in new[] { "health", "readiness", "details" }) { await ProbeAsync("live-healthy-" + view, pipe, view, 0, "Healthy", true).ConfigureAwait(false); }
            await fixture.CommandAsync("flag").ConfigureAwait(false);
            await HttpAsync("live-unhealthy", client, "/_health", HttpStatusCode.InternalServerError).ConfigureAwait(false);
            await HttpAsync("live-not-ready", client, "/_health/ready", HttpStatusCode.InternalServerError).ConfigureAwait(false);
            foreach (string view in new[] { "health", "readiness", "details" }) { await ProbeAsync("live-unhealthy-" + view, pipe, view, 1, "Unhealthy", false).ConfigureAwait(false); }
            await fixture.CommandAsync("clear").ConfigureAwait(false);
            await HttpAsync("live-cleared", client, "/_health/ready", HttpStatusCode.OK).ConfigureAwait(false);
            await fixture.CommandAsync("begin-stopping").ConfigureAwait(false);
            await HttpAsync("live-stopping-health", client, "/_health", HttpStatusCode.OK).ConfigureAwait(false);
            await HttpAsync("live-stopping-readiness", client, "/_health/ready", HttpStatusCode.InternalServerError).ConfigureAwait(false);
            await ProbeAsync("live-stopping-health", pipe, "health", 0, "Healthy", false).ConfigureAwait(false);
            await ProbeAsync("live-stopping-readiness", pipe, "readiness", 1, "Healthy", false).ConfigureAwait(false);
            JsonObject final = await fixture.CommandAsync("state").ConfigureAwait(false);
            Require((int?)final["businessCalls"] == 0, "Transport reads invoked business middleware.");
            Passed("live-transport-observations", new JsonObject { ["startup"] = ready, ["initialState"] = initial, ["finalState"] = final });
            await fixture.SendAsync("exit").ConfigureAwait(false);
            ProcessObservation stopped = await fixture.FinishAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Require(stopped.ExitCode == 0 && !stopped.TimedOut, "Actual packaged fixture failed normal shutdown.");
            Passed("live-fixture-origin-and-stop", new JsonObject { ["origin"] = ValidateOrigin(stopped, host) });
            await ProbeAsync("closed-real-pipe", pipe, "health", 2, null, null).ConfigureAwait(false);
        }
        await using (var fixture = new OwnedProcess(this, "fixture-parity", Execute(host, "parity"), host.Output))
        {
            JsonObject ready = await fixture.ReadObjectAsync().ConfigureAwait(false);
            (Uri address, string pipe) = FixtureAddress(ready);
            using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15) };
            foreach ((string view, string path) in new[] { ("health", "/_health"), ("readiness", "/_health/ready"), ("details", "/_health/details") })
            {
                JsonObject http = await HttpAsync("parity-" + view, client, path, HttpStatusCode.OK).ConfigureAwait(false);
                JsonObject probe = (await ProbeAsync("parity-" + view, pipe, view, 0, "Healthy", true).ConfigureAwait(false))!;
                Require(JsonNode.DeepEquals(http, probe), "The actual packaged HTTP and pipe/probe reports differ for the same frozen source: " + view);
                Passed("exact-report-parity-" + view);
            }
            await fixture.SendAsync("exit").ConfigureAwait(false);
            ProcessObservation stopped = await fixture.FinishAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Require(stopped.ExitCode == 0 && !stopped.TimedOut, "Parity fixture failed normal shutdown.");
            Passed("parity-fixture-origin-and-stop", new JsonObject { ["origin"] = ValidateOrigin(stopped, host), ["startup"] = ready });
        }
    }

    private static (Uri Address, string Pipe) FixtureAddress(JsonObject ready)
    {
        Require((string?)ready["status"] == "ready" && (string?)ready["runtime"] == ".NET 10.0.12", "Real fixture did not start on the pinned runtime.");
        var address = new Uri((string)ready["address"]!);
        string pipe = (string?)ready["pipeName"] ?? "";
        Require(address.Scheme == "http" && address.Host == "127.0.0.1" && address.Port > 0 && pipe.Length > 0,
            "Actual fixture did not expose a loopback HTTP endpoint and local pipe.");
        return (address, pipe);
    }

    private async Task<JsonObject> HttpAsync(string name, HttpClient client, string path, HttpStatusCode expected, bool deny = false, bool head = false)
    {
        using var request = new HttpRequestMessage(head ? HttpMethod.Head : HttpMethod.Get, path);
        if (deny) { request.Headers.Add("X-Auth", "deny"); }
        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
        byte[] body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        string file = Path.Combine(root, "logs", name + ".http.body");
        File.WriteAllBytes(file, body);
        var observation = new JsonObject
        {
            ["name"] = name,
            ["method"] = request.Method.Method,
            ["url"] = new Uri(client.BaseAddress!, path).ToString(),
            ["expectedStatus"] = (int)expected,
            ["actualStatus"] = (int)response.StatusCode,
            ["contentLength"] = response.Content.Headers.ContentLength,
            ["bodyLength"] = body.Length,
            ["bodySha256"] = Hash(file)
        };
        report["httpResponses"] ??= new JsonArray(); report["httpResponses"]!.AsArray().Add(observation); Save();
        Require(response.StatusCode == expected, "Actual packaged HTTP response differed: " + name);
        if (head)
        {
            Require(body.Length == 0 && response.Content.Headers.ContentLength > 0, "HEAD did not preserve representation length with an empty body.");
            return new JsonObject();
        }
        Require(response.Content.Headers.ContentType?.MediaType == "application/json" && response.Content.Headers.ContentLength == body.Length,
            "HTTP report was not a complete bounded JSON representation.");
        JsonObject observed = JsonNode.Parse(body)!.AsObject();
        if (deny)
        {
            Require(observed.Count == 3 && (int?)observed["schemaVersion"] == 1 && (string?)observed["kind"] == "error"
                && observed["error"] is JsonObject error && error.Count == 1 && (string?)error["code"] == "Forbidden",
                "Denied details must expose only the complete bounded Forbidden error schema.");
        }
        return observed;
    }

    private async Task<JsonObject?> ProbeAsync(string name, string pipe, string view, int expectedExit, string? health, bool? ready)
    {
        Consumer consumer = consumers["probe"];
        ProcessObservation child = await RunChildAsync("probe-" + name,
            Execute(consumer, "--pipe", pipe, "--view", view, "--timeout-ms", expectedExit == 2 ? "100" : "5000"), consumer.Output).ConfigureAwait(false);
        Require(child.ExitCode == expectedExit && !child.TimedOut, "Actual matching probe returned a different exit: " + name);
        JsonObject origin = ValidateOrigin(child, consumer);
        string errors = RemoveOrigin(child.StandardError);
        JsonObject? observed = null;
        if (expectedExit == 2)
        {
            Require(child.StandardOutput.Length == 0 && errors is "cis-health-probe: TimeoutOrCanceled" or "cis-health-probe: TransportOrProtocolFailure",
                "Closed real endpoint did not produce a documented bounded probe transport failure.");
        }
        else
        {
            Require(errors.Length == 0, "Successful protocol exchange emitted unexpected stderr.");
            observed = JsonNode.Parse(child.StandardOutput)!.AsObject();
            Require((string?)observed["healthStatus"] == health && (bool?)observed["ready"] == ready && (int?)observed["schemaVersion"] == 1,
                "Probe output did not reflect actual selected snapshot state.");
        }
        Passed("probe-" + name, new JsonObject { ["expectedExit"] = expectedExit, ["actualExit"] = child.ExitCode, ["origin"] = origin, ["snapshot"] = observed?.DeepClone() });
        return observed;
    }

    private static string RemoveOrigin(string stderr) => string.Join('\n', stderr.Split('\n')
        .Select(line => line.TrimEnd('\r')).Where(line => !line.StartsWith(OriginPrefix, StringComparison.Ordinal))).Trim();
}
