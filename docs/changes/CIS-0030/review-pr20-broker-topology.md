# Pooled Management broker topology cleanup

PR 20's first service run reached the unchanged 128-queue limit because each
Management fixture installed a durable reply-quarantine queue in the shared
broker and released its lease without removing that fixture's topology.
The separate Messaging fixture already retired its exact owned topology.

Management now captures its freshly generated namespace before installation.
After its actual replicas, clients and transports drain, disposal removes only
that namespace's one producer quarantine queue and three exact exchanges.
The setup identity and current endpoint come from the existing owned broker.
A strict run-owner/Management/eight-hex namespace check, a passive consumer
check, separate channels for missing resources and a finite cleanup deadline
bound the operation. Only an actual 404 is treated as an already absent
declaration. Drain or deletion failures remain failures and retain the broker
lease; they do not certify completed ownership. Failed setup follows the same
cleanup path, and repeated disposal is idempotent.

The real pooled lifecycle regression keeps one sibling service running while
three other Management fixtures install and remove their topology. It verifies
the exact retired queue/exchanges return 404, the sibling's declarations remain
present and its authenticated application read still succeeds. An invalid
registration then exercises failed setup after topology installation; bounded
before/after queue and exchange inventories must match exactly.

No runtime topology behavior, TLS, durable queue properties, permissions,
128-queue/32-connection limits, image/package pins or shared resource ceilings
change. Qualification subsequently passed: scoped format/build with zero
warnings/errors, two focused native cases, four current-UX browser cases, and
all 364 selected Ubuntu service cases with zero skips. See the exact run and
retained failure/accounting history in [the shared verification record](review-pr20-ci-corrections.md).
