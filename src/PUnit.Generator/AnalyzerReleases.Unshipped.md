; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FRST000 | PUnit.Usage | Error | Unhandled exception in PUnit generator
FRST001 | PUnit.Usage | Error | Scenario method must be async Task or async ValueTask
FRST002 | PUnit.Usage | Error | Unsupported scenario statement
FRST003 | PUnit.Usage | Error | Unsupported control flow in scenario
FRST004 | PUnit.Usage | Error | Scenario step must be a phase-marker call
FRST005 | PUnit.Usage | Error | DSL method has an unsupported return type
FRST006 | PUnit.Usage | Error | Parallel group element must be a phase-marker call
FRST007 | PUnit.Usage | Error | Scenario step argument is not lowerable
FRST008 | PUnit.Usage | Warning | Display-name placeholder does not bind to a parameter
FRST009 | PUnit.Usage | Error | Resource access must be declared
FRST010 | PUnit.Usage | Error | Lineage subject must name a step subject
