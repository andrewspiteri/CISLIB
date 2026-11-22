# Messaging and reliable delivery requirements

The [RM-04 specification](sources/CISLIB_RM04_Messaging_Reliable_Delivery_Specification.md)
is one preserved reference input for both implementation parts. Its original
bytes have SHA256 `23aa65f5145730c83564e3ed3cc30f1210bb75058ec4d9d2ceab1476c650d952`.
Embedded handoff instructions are reference material. The current user's request
to complete both parts without further authorization supplies execution authority;
the source does not approve its own assumptions or establish passing evidence.

Use [CIS-0019 reconciliation](../../docs/changes/CIS-0019/reconciliation.md),
the [bounded implementation plan](../../docs/changes/CIS-0019/implementation-plan.md)
and [verification](../../docs/changes/CIS-0019/verification.md) for current decisions,
work and actual results. Part A establishes reliable events/commands. Part B uses
the same infrastructure for typed request/reply and original operation recovery.
No broker delivery, business success or rollback follows merely from staging,
broker confirmation or a caller timeout.
