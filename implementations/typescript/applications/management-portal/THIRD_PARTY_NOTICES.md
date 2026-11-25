# Third-party notices

The portal uses exact dependency versions from the workspace lock. The portable
CIS clients remain separate. Keep the original license files shipped with every
installed/bundled dependency, including transitive dependencies, with any
redistribution; this summary does not replace them.

| Dependency | Version | License |
| --- | --- | --- |
| Next.js | 16.4.6 | MIT |
| React / React DOM | 19.3.1 | MIT |
| Auth.js (`next-auth`) | 4.24.15 | ISC |
| `@auth/pg-adapter` | 1.11.3 | ISC |
| node-postgres (`pg`) | 8.23.1 | MIT |
| Radix Dialog / Tabs | 1.3.1 / 1.2.1 | MIT |
| `server-only` | 0.0.1 | MIT |
| Playwright test (development) | 1.65.1 | Apache-2.0 |
| TypeScript and declaration packages (development) | Workspace pins | Apache-2.0 / MIT, as supplied |

The licensed libraries retain their own ownership and source notices. In
particular, the Auth.js ISC notice and permission text are retained in its
package `LICENSE`; MIT dependencies retain their package copyright/permission
texts; Playwright retains its Apache license and any supplied NOTICE. The
selected auth-provider composition and native request policy in this application
are original integration code. They do not copy or replace the libraries'
OAuth/JWT/session implementations.

The screenshot review's embedded Inter font belongs to the existing design
artifact. Any production font copied into this portal must carry its own
existing SIL Open Font License notice; this host adds no downloaded font.
