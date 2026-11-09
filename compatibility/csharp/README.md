# C# compatibility fixtures

The actual [publication ledger](publications/ledger.json) contains zero released versions. Frozen Healthcheck API/configuration/wire inputs under `healthcheck/` and Type Manager native consumer fixtures under the C# tests protect genuine current-development commitments; they are not invented release history.

Preserve actual published source/binary/configuration/wire/default behavior from first publication as specified by the [compatibility policy](../../docs/architecture/compatibility-policy.md). Native API comparison and consumer tests are the supported route. Historical replay/receipt schemas and campaign orchestration are retired.
