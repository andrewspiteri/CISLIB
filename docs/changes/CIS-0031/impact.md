---
title: "CIS-0031 impact analysis"
type: impact-analysis
status: Draft
change_id: CIS-0031
graph_build_id: "sha256:1328a3343309175f8c5ded3b41404886ba8b52b9387b43f1554ddf71cb5b52dd"
analysis_truncated: false
authority: human-reviewed
---

# Impact analysis

Findings remain proposals until a recorded human decision or eligible derivation supplies their disposition. Derived rows retain original authority separately from the controller action.

## Findings

| ID | Category | Target | Label | State | Confidence | Evidence | Rationale | Review reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IMPACT-7A9FD767F3 | documentation | cislib::document::cislib.document.modules.configuration.central-snapshots | Configuration central snapshot contracts | proposed | high | docs/modules/configuration/central-snapshots.md:document=cislib.document.modules.configuration.central-snapshots | Explicit analysis root. |  |
| IMPACT-13B0C40A3F | documentation | cislib::document::cislib.document.modules.management.configuration-contract | Central configuration contract | proposed | high | docs/modules/management/configuration-contract.md:document=cislib.document.modules.management.configuration-contract | Explicit analysis root. |  |
| IMPACT-1CECBC6544 | documentation | cislib::document::cislib:guide:management-portal-inspection | Application inspection in the Management portal | proposed | high | docs/modules/management/portal-inspection.md:document=cislib:guide:management-portal-inspection | Explicit analysis root. |  |
| IMPACT-F89E950861 | implementation | cislib::component::CIS.Audit | Audit | proposed | high | docs/references/current-component-ownership.md:line=45 | Related through owns at depth 1. |  |
| IMPACT-B1EDDFE455 | implementation | cislib::component::CIS.Management | Central Management clients | proposed | high | docs/references/current-component-ownership.md:line=46 | Related through belongs-to at depth 1. |  |
| IMPACT-196E5D4CC1 | implementation | cislib::component::CIS.Management.Service | Central Management service | proposed | high | docs/references/current-component-ownership.md:line=53 | Related through belongs-to at depth 1. |  |
| IMPACT-112C2369B1 | implementation | cislib::symbol::CIS.Audit/project/implementations%2Fcsharp%2Fsrc%2FCIS.Core%2FCIS.Core.csproj/csharp/interface/CIS.Audit.IAuditWriter | CIS.Audit.IAuditWriter | proposed | high | implementations/csharp/src/CIS.Core/Audit/Contracts/AuditWriterContracts.cs:line=128 | Explicit analysis root. |  |
| IMPACT-624C4791A3 | implementation | cislib::symbol::CIS.Management.Service/project/implementations%2Fcsharp%2Fapplications%2FCIS.Management.Service%2FCIS.Management.Service.csproj/csharp/method/CIS.Management.Service.Configuration.ManagementConfigurationService.ReplaceAssignmentsAsync%28CIS.Management.Service.Configuration.ManagementConfigurationBindingResource%2C%20string%2C%20string%2C%20CIS.Management.ManagementConfigurationAssignmentCommand%2C%20CancellationToken%29%2Flambda%20expression%40implementations%2Fcsharp%2Fapplications%2FCIS.Management.Service%2FConfiguration%2FManagementConfigurationService.cs%3A4541 | CIS.Management.Service.Configuration.ManagementConfigurationService.ReplaceAssignmentsAsync(CIS.Management.Service.Configuration.ManagementConfigurationBindingResource, string, string, CIS.Management.ManagementConfigurationAssignmentCommand, CancellationToken)/lambda expression@implementations/csharp/applications/CIS.Management.Service/Configuration/ManagementConfigurationService.cs:4541 | proposed | high | implementations/csharp/applications/CIS.Management.Service/Configuration/ManagementConfigurationService.cs:line=67 | Related through calls at depth 1. |  |
| IMPACT-46E11866F7 | implementation | cislib::symbol::CIS.Management.Service/project/implementations%2Fcsharp%2Fapplications%2FCIS.Management.Service%2FCIS.Management.Service.csproj/csharp/method/CIS.Management.Service.Persistence.ManagementSql.RequireContext%28CIS.Transactions.ILocalTransactionContext%29 | CIS.Management.Service.Persistence.ManagementSql.RequireContext(CIS.Transactions.ILocalTransactionContext) | proposed | high | implementations/csharp/applications/CIS.Management.Service/Persistence/ManagementSql.cs:line=15 | Related through calls at depth 1. |  |
| IMPACT-46C46AD9C7 | implementation | cislib::symbol::CIS.Management.Service/project/implementations%2Fcsharp%2Fapplications%2FCIS.Management.Service%2FCIS.Management.Service.csproj/csharp/method/CIS.Management.Service.Persistence.PostgreSqlManagementStore.ReplaceConfigurationAssignmentsAsync%28CIS.Transactions.ILocalTransactionContext%2C%20CIS.Management.Service.Configuration.ManagementAssignmentInput%2C%20CancellationToken%29 | CIS.Management.Service.Persistence.PostgreSqlManagementStore.ReplaceConfigurationAssignmentsAsync(CIS.Transactions.ILocalTransactionContext, CIS.Management.Service.Configuration.ManagementAssignmentInput, CancellationToken) | proposed | high | implementations/csharp/applications/CIS.Management.Service/Persistence/PostgreSqlManagementStore.Configuration.Assignments.cs:line=20 | Explicit analysis root. |  |
| IMPACT-C4AED68EAA | implementation | cislib::symbol::CIS.Management/project/implementations%2Fcsharp%2Fsrc%2FCIS.Core%2FCIS.Core.csproj/csharp/interface/CIS.Management.IManagedWorkAdmission | CIS.Management.IManagedWorkAdmission | proposed | high | implementations/csharp/src/CIS.Core/Management/Contracts/ManagedWorkAdmissionContracts.cs:line=49 | Explicit analysis root. |  |

## Coverage

- Total findings: 11
- Accepted: 0
- Proposed: 11
- Rejected: 0
- Deferred: 0
- Traversal truncated: false
