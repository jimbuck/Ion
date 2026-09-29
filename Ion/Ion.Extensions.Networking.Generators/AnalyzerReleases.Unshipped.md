; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ION201 | Ion.Networking | Error | Network type is not unmanaged
ION202 | Ion.Networking | Error | Predicted component without owner authority
ION203 | Ion.Networking | Error | Replicated component too large
ION204 | Ion.Networking | Warning | Replicated type not used as an ECS component
ION205 | Ion.Networking | Error | Member cannot be serialized
ION206 | Ion.Networking | Warning | Network reader created in a stage method
ION207 | Ion.Networking | Warning | Network message sent but never read
ION208 | Ion.Networking | Warning | Network message read but never sent
ION209 | Ion.Networking | Error | Predicted or Interpolated without Replicated
ION210 | Ion.Networking | Warning | Entity handle in a network type
