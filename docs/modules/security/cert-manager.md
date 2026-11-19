---
title: cert-manager certificate deployment recipe
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.cert_manager
---

# cert-manager certificate deployment recipe

This recipe targets the approved cert-manager **v1.21.3** line, whose documented
Kubernetes support is **1.33–1.36**. It is a reproducible deployment input, not an
installation or live acceptance result. Confirm the actual cluster support
relationship before deployment using the official
[release table](https://cert-manager.io/docs/releases/) and
[1.21 release notes](https://cert-manager.io/docs/releases/release-notes/release-notes-1.21/).

An authorized platform operator owns issuer policy, issuance, private-key custody,
Secret access and rollout. CISLIB only validates and adopts resulting material.
The following values are placeholders and grant no authority to change a cluster.

```yaml
apiVersion: cert-manager.io/v1
kind: Certificate
metadata:
  name: country-worker-client
  namespace: example-workloads
spec:
  secretName: country-worker-client
  duration: 12h
  renewBefore: 4h
  privateKey:
    rotationPolicy: Always
    algorithm: RSA
    size: 2048
  usages:
    - digital signature
    - client auth
  uris:
    - https://identity.example.test/applications/country-worker/environments/production
  issuerRef:
    group: cert-manager.io
    kind: Issuer
    name: platform-controlled-client-issuer
```

For explicit production `ShortLived`, verify the **issued** leaf's actual
`NotAfter - NotBefore` is at most 24 hours (or the configured smaller ceiling).
The requested twelve-hour duration is not proof. The issuer must emit a non-CA
leaf with digital-signature usage and explicit client-authentication EKU. Use the
exact configured URI under an organization-controlled namespace. The receiver
checks the actual direct issuing certificate relationship, not an issuer DN or a
renewal-sensitive leaf thumbprint.

For the default production `Online` policy, use an issuer that actually publishes
reachable CRL/OCSP information supported by the native platform. Unknown or
unavailable required status denies. Do not silently change to ShortLived or
Development after an outage. Native caches and propagation mean Online is not
universally instantaneous revocation. ShortLived provides no immediate revocation
of copied leaves or of continued access to new issuance after CA compromise.

Deployment automation controls Certificate/Issuer and any approval-policy
resources. Workload service accounts can read only their own credential Secret;
they cannot modify issuance policy, request arbitrary applications' SANs, read
other applications' private keys or obtain a CA private key. If using an approval
controller, document exactly how its requester/namespace/resource checks bind the
original deployment request to the allowed identity. A controller-created
CertificateRequest does not itself prove the originating workload identity.

Mount `tls.crt` and `tls.key` read-only as files, with owner/group permissions
limited to the application identity. Do not use a `subPath` mount that freezes a
credential file to an old Secret generation. Keep CA/client-issuer and server
trust bundles separate and use absolute paths in `CertificatePemSecurityOptions`.
The source's paired bounded reads reject a temporarily inconsistent replacement;
the next existing Heartbeat check can adopt the complete new pair.

Only leaf/key source files reload. A changed root bundle, authorized issuer,
mapping, grant or allowed origin requires replacement application instances.
Changing/deleting a mounted Secret does not revoke remote copies. Complete an
emergency trust/grant removal by stopping admission on **every** instance with the
superseded snapshot; existing admitted operations retain their own deadlines and
cleanup ownership.

To collect real AC-24 evidence, an authorized deployment needs a supported cluster,
the selected cert-manager release and actual issuer, least-privilege issuance/Secret
policy, read-only mounted files and two independently identified applications.
Record initial issuance/permit/deny, an actual renewed Secret generation, adoption
on new TLS connections while held old responses drain, rejected bad replacement,
expiry/readiness and shutdown custody. Verify exact SAN, usages and issued
validity, plus the platform's chosen revocation behavior.

**AC-24 remains unverified without that actual deployment.** Local generated-PEM
and TLS tests prove the library's local capture/adoption boundary. They do not prove
cluster issuance authorization, Secret update propagation or cert-manager renewal.
