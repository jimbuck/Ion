; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ION001 | Ion.Schedule | Error | Unknown stage
ION002 | Ion.Schedule | Error | Ordering cycle
ION003 | Ion.Schedule | Error | Scope without a matching end
ION004 | Ion.Schedule | Error | Step can never run
ION005 | Ion.Schedule | Error | Async step
ION006 | Ion.Schedule | Error | Scoped service in the root schedule
ION007 | Ion.Schedule | Error | Unsupported step signature
ION008 | Ion.Schedule | Error | Unregistered step parameter
ION009 | Ion.Schedule | Error | Unregistered system
ION010 | Ion.Schedule | Warning | Legacy middleware step
ION011 | Ion.Schedule | Error | Ambiguous scope
ION012 | Ion.Schedule | Warning | Constraint on a system that is not in the schedule
ION013 | Ion.Schedule | Warning | System without steps
ION014 | Ion.Schedule | Warning | Ion schedule generator is disabled
ION101 | Ion.Events | Warning | Event emitted but never read
ION102 | Ion.Events | Warning | Event read but never emitted
ION103 | Ion.Events | Warning | Event reader created in a stage method
ION104 | Ion.Events | Error | Event payload is not unmanaged
ION105 | Ion.Events | Info | Event read in an earlier stage than it is emitted
ION106 | Ion.Events | Warning | Event reader in a readonly field or a property
ION301 | Ion.Ecs | Error | Query on a class that is not partial
ION302 | Ion.Ecs | Error | Query component parameter passed by value
ION303 | Ion.Ecs | Error | Query component is not a struct
ION304 | Ion.Ecs | Error | Query component both required and excluded
ION305 | Ion.Ecs | Error | Structural change inside a query without Commands
ION306 | Ion.Ecs | Error | Unsupported query method
ION307 | Ion.Ecs | Warning | Query without a stage
