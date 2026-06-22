; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PUNIT000 | PUnit.Usage | Error | Unhandled exception in PUnit generator
PUNIT001 | PUnit.Usage | Error | Scenario method must be async Task or async ValueTask
PUNIT002 | PUnit.Usage | Error | Unsupported scenario statement
PUNIT003 | PUnit.Usage | Error | Unsupported control flow in scenario
PUNIT004 | PUnit.Usage | Error | Scenario step must be a phase-marker call
PUNIT005 | PUnit.Usage | Error | DSL method has an unsupported return type
PUNIT006 | PUnit.Usage | Error | Parallel group element must be a phase-marker call
PUNIT007 | PUnit.Usage | Error | Scenario step argument is not lowerable
PUNIT008 | PUnit.Usage | Warning | Display-name placeholder does not bind to a parameter
PUNIT009 | PUnit.Usage | Error | Resource access must be declared
PUNIT010 | PUnit.Usage | Error | Lineage subject must name a step subject
