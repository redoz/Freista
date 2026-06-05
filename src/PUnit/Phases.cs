namespace PUnit;

// Marker "phase" types. Domain DSLs hang steps off these with C# 14 static extension
// members, e.g. `extension(Given) { public static Task<Patient> PatientExists(...) }`.
// They carry no behaviour and are never instantiated; they exist only to give the
// Given/When/Then call sites a type to extend. Custom phases are any type implementing
// IPhase — the generator recognises them and uses the type name as the phase label.

/// <summary>Marker interface for phase types. Any type implementing it can host DSL steps and is
/// recognised by the generator; the type's name becomes the step's phase label.</summary>
#pragma warning disable CA1040 // Avoid empty interfaces — IPhase is an intentional marker interface.
public interface IPhase { }
#pragma warning restore CA1040

/// <summary>Phase marker for arrange / precondition steps.</summary>
public sealed class Given : IPhase { private Given() { } }

/// <summary>Phase marker for the action under test.</summary>
public sealed class When : IPhase { private When() { } }

/// <summary>Phase marker for assertions / postconditions.</summary>
public sealed class Then : IPhase { private Then() { } }
