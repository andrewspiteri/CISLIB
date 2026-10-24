# Unit170 actual source binding and launch, 24 October 2026

The full Unit campaign launched on the authorized Ubuntu server at10:52:54 UTC. This records actual preparation and execution, not a completed campaign or qualification. The accepted union remains1591/3065,51.908646%,UNQUALIFIED.

Maintained preparation used the existing C# DependencyChecker, SHA256 `b6dec3b6e775b743f24e3bc0f236641c8dd59b2027adad023be682cf207a64cf`, and the reviewed `omit-archived-healthcheck-evidence-v1` policy. It exited0 after copying1936 selected files,48,193,449 bytes. The three original metadata hashes are:

- source-inputs: `eea4cbabeb2970bbdd64ad51d6bd81e9c86cda29d6bc3f35928bc59ebc2469e3`
- preparation: `6a68ca7f253870ac51ce1910871a7236a489ee2a0a020d7a36277dac24e3f6e5`
- archive-omissions: `1b756eea4e7b90c8d024bd741c08c488c5ec0170d550d03ed1424fa0e9b07afe`

The selected-source digest is `0e4ac7240be5877ae7077883fae2a1e2605f3f72bb967eedf27fac03185bd7ee`. Exactly281 archived evidence files,3,373,453,721 bytes, were omitted under the fixed policy; no arbitrary source exclusions were added.

The independently reviewed C# stager, DLL `59b0ad30ae7dcb6eee1e8cd935f6e9c31ad7684d886c3beec15b51c95ab43c50`, bound every copied source, all146 shipping C# files,41 selected paths and complete original preparation metadata before and after copying. Actual staging exited0. Closed input SHA256 `c1abd72bccd97425fa0613384939fbc7dd9b90dec749807696b7a5b97103ee89`; staging receipt `becee9b8baead5eae8893b359d4c58d8211846e54b310f539ef89c8393535c03`; complete file ledger `c9584497a5cfa5689fb1850e82ab6d2b08af58d0c923ab7ae63ed53de41beb33`. The actual staging peer is retained separately.

The complete1973-file payload is retained as `retained/unit-complete-partition170-payload01.tar.gz`,8,688,221 bytes/SHA256 `67632664d6d48e7b1e503b4db03db85bc7f2eb344def1349d718b2cfce9ae228`. Ubuntu tar comparison reported only platform mode/UID/GID differences; the complete actual per-file SHA256 check subsequently passed1973/1973, with exit0 and empty stderr. Both observations are preserved, rather than treating the initial tar exit1 as a byte mismatch or silently discarding it.

At10:52:21 UTC measured free space was8,576,454,656 bytes. Existing143 actual three-root logical usage was2,219,424,234 bytes. Remaining143 allowance plus the new170 four-billion-byte allowance, two-billion-byte reserve and200-million-byte outside allowance required7,980,575,766 bytes and fit. This launch check does not reserve capacity for another campaign.

The immutable payload/controller uses SDK10.0.401, runtime10.0.12, per-test coverage, no mutant mixing, two runners, additional timeout4,500,000 ms, shutdown timeout5000 ms,720-minute controller limit and a whole payload/run/TMP four-billion-byte cap. Original discovery and ordinary execution passed all1389 identities. Native mutation execution began10:55:01.7703848 UTC, controller1783032/native strace1783791. At11:40 UTC both remained active. No final native report, restoration/fresh-run closure, capture audit, Index08 entry or union acceptance is inferred.

Historical143 remains immutable and active, including all five earlier host-abort holds. Neither ordinary passes nor the complete diagnostic176 supplies mutation credit. New170 must independently finish and satisfy the full capture, source, report, restoration and union checks.
