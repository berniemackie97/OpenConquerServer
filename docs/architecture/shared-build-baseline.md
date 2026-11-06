# Shared build baseline

CP1.3 / D5 decision, 2026-11-05. These are OpenConquer development policies, not
native 5517 behavior. No packet, gameplay, timing, or retail-data contract changes.

Both repositories keep `.editorconfig` and `Directory.Build.props` identical.
Change both copies together. `global.json` pins SDK `10.0.403` exactly, with SDK
roll-forward disabled and prerelease SDKs excluded. Both repositories enable
central package management, transitive package pinning, and locked restore.

| Setting | Shared value |
| --- | --- |
| Framework / language | `net10.0` / C# `14.0` |
| Analysis | `latest-recommended`, .NET analyzers enabled |
| Enforcement | Warnings are errors; build-time code style enabled |
| XML documentation | Generate and validate written documentation; missing-member documentation (`CS1591`) is allowed; omit XML from publish output |
| Reproducibility | Deterministic build, portable debug symbols; CI enables `ContinuousIntegrationBuild` |
| Types | Explicit types, including when the type is apparent |
| Required style | File-scoped namespaces, braces, clean formatting, unused-import removal, readonly fields, readonly structs, field naming |
| Compact blocks | Expand single-line blocks and statements |
| Primary constructors | Suggest where clean and appropriate; no mandatory conversion of existing types |

Use an explicit constructor when argument validation, derived initialization, or
readonly stored dependencies make it clearer. Primary-constructor parameters used
by instance members are mutable captures; the preference does not override readonly
ownership or validation. Stateless operations can be static rather than requiring
an injected instance.

Test method names use `Subject_ExpectedBehavior`, so `CA1707` is disabled only under
`tests/`. No production analyzer category is disabled for this alignment.

## Validation

Run locked restore, Release build, and `dotnet format` with
`--verify-no-changes --no-restore` on the solution. Run focused non-Docker test projects locally; CI
runs database and platform-specific checks. Existing native fixture and golden-vector
coverage stays in those suites. This build-policy change does not introduce a new
wire behavior requiring a new native capture.

Cancellation tokens belong to the operation they control. In particular, a caller
may cancel waiting for shared shutdown, but cannot cancel the cleanup task before it
starts. A processor that has accepted connection ownership must dispose it even when
its caller is already canceled. Regression tests cover both lifetime boundaries.
