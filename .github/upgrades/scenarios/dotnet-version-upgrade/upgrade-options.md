# Upgrade Options — ObracunDb

Assessment: One SDK-style ASP.NET Core project targeting net8.0; one recommended package upgrade and nine source-incompatible API findings.

## Strategy

### Upgrade Strategy
A single modern .NET project with no dependencies can be upgraded in one atomic pass.

| Value | Description |
|-------|-------------|
| **All-at-Once** (selected) | Upgrade the project together in a single atomic pass. |
| Top-Down | Upgrade applications first while temporarily multi-targeting shared libraries. |

## Compatibility

### Unsupported API Handling
The assessment reports nine source-incompatible API findings for the target framework.

| Value | Description |
|-------|-------------|
| **Fix Inline** (selected) | Resolve every API change in the same task without creating deferred stubs. |
| Defer Complex Changes | Apply simple replacements and create compilable stubs plus resolution subtasks for complex changes. |
