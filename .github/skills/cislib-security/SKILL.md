---
name: cislib-security
description: Integrate or review CISLIB workload identity, capability authorization, bound REST and the CountryData security profiles using existing native owners.
---

# CISLIB security

Read the [shared contract](../../../docs/modules/security/README.md) and relevant
implemented adapter: [Entra](../../../docs/modules/security/entra.md),
[certificates](../../../docs/modules/security/certificates.md),
[renewal](../../../docs/modules/security/credentials.md) or
[local development](../../../docs/modules/security/local-development.md).
Use the actual [CountryData service/caller](../../../docs/modules/security/consumer.md)
and its C# composition as the maintained example.

- Preserve central `EnvironmentOptions` and one native DI/host. Registration and
  constructors capture; sequential Startup performs credential I/O. Capture full
  trusted policy/provider input through the existing Configuration reader. Trust,
  grants and profile changes require instance replacement.
- Select inbound/outbound roles explicitly. Use native authentication and the
  shared complete-grant authorizer. Headers, serialized contexts, ambient
  principals and retained audit records cannot authenticate or delegate authority.
- Resolve actual server application/environment/resource dimensions before work.
  One whole grant must cover each capability and target; preserve additional
  native requirements and cancellation settlement.
- Preserve explicit routing/projection, operational/readiness and business stages.
  Separate mTLS/business and operational listeners by actual native connection;
  Host headers cannot open a business bypass.
- Reuse [bound REST](../../../docs/modules/security/transport.md): exact HTTPS
  origins/owner, no auth suppression/replacement, TRACE or final method/destination
  escape. Preserve unbound behavior, deadlines, native HTTP observations and the
  existing retry owner.
- Reserve generation/key capacity before acquisition. Retain current/draining
  owners through actual TLS/body settlement; retry selects again after its delay.
  Never extend expiry. Windows cleanup touches only exact recorded newly owned
  user backing; no store provisioning, GC or delay can establish cleanup.
- Local development uses the real certificate path with all environment/origin/
  trust gates. Exclude its trust from production. Use restricted owned scratch
  and explicit custody, never shared keys or accepting TLS callbacks.
- Distinguish [audit](../../../docs/modules/security/audit.md), business effects
  and [failure evidence](../../../docs/modules/security/failures.md). Preserve
  awaited hook faults; add no audit queue, token cache, scheduler, policy engine
  or alternative HTTP stack.

Use existing MSTest/CommonTesting and native fixtures for meaningful boundaries.
Keep example unit tests under `examples`; reuse the existing package fixture.
Authored source is not passing evidence. Preserve AC-02 AKS and AC-24 cert-manager
as unverified until their authorized environments supply actual proof. Local
delivery does not finish the roadmap or qualify a release.
