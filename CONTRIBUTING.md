# Contributing

Use the pinned SDK, initialize the reference submodule and copy `eng/style.editorconfig` to the ignored local `.editorconfig`. See [development and testing](docs/development.md).

The dependency direction is `Campfire.Web → Campfire.Application`. Use direct Minimal API modules, native platform security, explicit parameterized SQL, leased connections and ordered after-commit work. Registration and JSON must remain Native AOT compatible. Keep the pinned `.slnx`, package versions and locks.

Preserve the selected frontend's functional behavior and security, not existing Rails installation interoperability. Record current contracts in [frontend contracts](docs/compatibility.md). Test security independently of reference comparison.

Run checks appropriate to the change and report discovered/pass/fail/skip counts plus exclusions. The complete gate is `dotnet eng/check`, including the pinned media toolchain. Native/browser/HTTP/Cable behavior needs its owning runtime proof. Regression tests must fail without the fix.

Use [benchmark controls](docs/benchmarks.md). Never weaken response/delivery validation, hide slower workloads or infer an engine ranking from noisy shared-host observations.
