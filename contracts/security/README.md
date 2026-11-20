# Security contract v1

RM-01's portable contract defines caller provenance, complete scoped grants and
known failure categories. The current C# types live in `CIS.Security` inside
`CIS.Core`; native hosting/projection APIs live in `CIS.Security.Hosting`. Read the
[C# guide](../../docs/modules/security/README.md) and the preserved
[requirements](../../planning/security/README.md) alongside the implementation.

The fixture files are small language-neutral examples of these semantics. They
are not credentials, a context-exchange protocol, another policy engine or proof
of JWT signatures, TLS, AKS federation or certificate issuance.

The [structural schema](v1/contract.schema.json) publishes caller, subject, target,
grant, instance and failure definitions under `$defs`. Bindings select the
applicable definition and additionally enforce the text and trust rules below;
structural validation alone cannot prove well-formed identity or authenticate it.
The case catalogs are semantic test vectors, not a caller-context wire envelope.

## Identity and caller scope

Local metadata projects the existing `EnvironmentOptions.ApplicationName`,
`EnvironmentName` and `InstanceId`; it introduces no resolver or new defaults.
Remote identity is the exact pair `(validated issuer/trust relationship, stable
provider subject)`, after native authentication and trusted mapping. Matching is
ordinal and case sensitive. Remote subject/resource identifiers are opaque;
whitespace or case is never silently normalized into another identity.

Caller data is immutable and bounded. It contains the selected profile/method,
issuer-qualified subject, actor kind, trusted application/environment where known,
safe authentication evidence, policy version and optional independently validated
human/delegation provenance. It contains no credential, unrestricted claims bag,
mutable principal or retained HTTP context. Constructing/deserializing this data
does not establish an authenticated scope.

`NotProvided`, `Reported` and `Verified` instance evidence are different states.
Reported metadata remains separately identified even when verified evidence
exists. An application certificate or shared service-account identity does not
attest a replica or human. The [caller cases](v1/caller-cases.json) retain these
distinctions. A background owner uses an explicit independent host context; an
initiator record does not delegate authority to later work.

## Complete grants

A grant selects one issuer-qualified subject or one trusted mapped role, one
registered capability, one target application, one target environment and one
resource shape:

| Shape | Match |
| --- | --- |
| TargetOnly | No resource type or identifier. |
| ExactResource | The exact configured resource type and identifier. |
| AllOfType | Any identifier of one explicit type within that same application/environment. |

One whole grant must match every dimension of each requested capability. Grant
fragments never combine. Every required capability must pass against the same
actual target, as must additional native requirements. There is no implicit
wildcard, administrator, same-environment permission or partially allowed result.
The receiver supplies the authoritative target; authorization of an earlier
route identifier does not authorize a different loaded object.

The [grant cases](v1/grant-cases.json) include the cross-product counterexamples,
complete multi-capability evaluation, issuer separation and resource boundaries.
The C# fixture tests feed these cases into the actual native-backed handler.
Entra grants additionally require the actual validated application role; a
roleless workload receives no capability from its application mapping alone.

## Results and lifecycle

Allow/Deny is separate from business completion. The [failure catalog](v1/result-categories.json)
publishes the nine stable `security.*` codes. Safe detail can carry an actual HTTP
status, reliable provider/transport code and local/remote/unknown evidence source.
Arbitrary remote 403 is not proof of capability denial. Certificate authentication
can fail with HTTP 403; a TLS handshake can fail before HTTP. Missing evidence
remains unknown. Programming faults remain faults.

Trust, mappings, capabilities, grants and profile choice are one immutable
validated startup snapshot. Replacement/restart adopts a new policy. Leaf
renewal is the dynamic exception and cannot change that policy or extend expiry.
Cancellation/deadlines/retries remain with their existing operation owners;
native authorization is joined through actual settlement. Authentication denial,
refresh and certificate adoption introduce no hidden replay.

Initial bounds are 16 named profiles, 256 registered capabilities and 4,096 grants.
Aliases have at most 64 Unicode scalars; opaque issuer/subject/resource identifiers
have at most 1,024. Application/environment retain the existing 255-scalar rule.
Capability names use at most 128 lowercase ASCII characters in letter-led segments
separated by dots. Unknown names deny. Deliberately larger configuration capacities
must remain finite and fit the existing global resource/telemetry policy.

The native and deployment acceptance boundaries remain distinct. The module's
implementation record must retain real AKS and cert-manager prerequisites; local
fixtures cannot establish those external outcomes or complete the roadmap alone.
