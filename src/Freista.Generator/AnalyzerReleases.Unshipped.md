; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FRST000 | Freista.Usage | Error | Unhandled exception in Freista generator
FRST001 | Freista.Usage | Error | Scenario method must be async Task or async ValueTask
FRST002 | Freista.Usage | Error | Unsupported scenario statement
FRST003 | Freista.Usage | Error | Unsupported control flow in scenario (loops, switch, try, goto)
FRST004 | Freista.Usage | Error | Scenario step must be a phase-marker call
FRST005 | Freista.Usage | Error | DSL method has an unsupported return type
FRST006 | Freista.Usage | Error | Parallel group element must be a phase-marker call
FRST007 | Freista.Usage | Error | Scenario step argument is not lowerable
FRST008 | Freista.Usage | Warning | Display-name placeholder does not bind to a parameter
FRST009 | Freista.Usage | Error | Resource access must be declared
FRST010 | Freista.Usage | Error | Lineage subject must name a step subject
FRST011 | Freista.Usage | Error | Scenario condition must be an awaited phase-marker call
FRST012 | Freista.Usage | Error | Conditionally assigned local has no step-produced definition
FRST013 | Freista.Usage | Error | Parallel steps conflict on one resource
FRST014 | Freista.Usage | Error | Cleanup uses the registering step's context
