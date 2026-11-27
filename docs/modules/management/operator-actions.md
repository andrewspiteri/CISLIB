# Operator action eligibility and recovery grants

Configuration source and binding actions are finite advisory reads over the
current native caller and the actual stored resource. They disclose supported
operation IDs, allowed flags and bounded reason codes. They disclose no grants,
principal memberships, configuration values or defaults. A successful action
read is not permission retained for a later write.

| Intent | Exact native resource | Required for submission and replay |
| --- | --- | --- |
| Replace display metadata | management.application / actual application UUID | management.applications.metadata.write and management.operations.read |
| Publish a source | management.configuration.source / actual source UUID | management.configuration.sources.publish and management.configuration.operations.read |
| Create a release | management.configuration.binding / resolved binding UUID | management.configuration.releases.create and management.configuration.operations.read |
| Replace, promote or roll back assignments | management.configuration.binding / resolved binding UUID | management.configuration.assignments.write and management.configuration.operations.read |

Every capability is checked against that same resolved resource and the
Management service's actual application/environment. A lookup grant for another
source, binding or resource type cannot complete the bundle. Execution retains the
existing native Operations authorization point before arbitration, so replaying
an old key cannot bypass a current missing permission. Original result lookup
independently checks its lookup permission; revoking write permission alone does
not erase the original authorized recovery path.

The source route is GET configuration/sources/{sourceId}/actions. The binding
route is GET applications/{applicationId}/configuration/{configurationId}/actions
with one canonical schemaVersion query value. The server resolves these targets,
then requires definitions.read on the actual source or binding before returning
eligibility. Unknown or hidden targets do not return action details. Caller
resource IDs, permission selectors, cursors and bodies cannot select a different
target. Responses use no-store.

Source actions describe validateConfiguration and publishConfigurationSource.
Binding actions describe previewConfiguration, createConfigurationRelease and
replaceConfigurationAssignments. Validation, preview, content disclosure and
adoption retain their independent grants. Application-level API hints do not
replace source/binding checks. Missing or unsupported action discovery must not
be interpreted as an empty successful authorization result.

Every mutation and result read reauthorizes through the actual native owner.
Configuration and metadata ETags, stable original actor/resource scopes,
fingerprints, original keys, transaction outcomes and required Audit remain
unchanged. A timeout is not rollback and is not permission to retry with a new key.
Required lookup authority lets the operator retain and recover the original
intent after an uncertain response.

Trusted deployment catalog apply retains its existing execution requirement;
it is not one of the portal editing operations above. A separate lookup still
needs its existing catalog lookup grant. Selecting these HTTP companions starts
no scheduler, producer or worker. Scheduler action eligibility must remain
unavailable until its actual backend and native resource checks exist.

