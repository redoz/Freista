namespace PUnit;

// Marker "phase" types. Domain DSLs hang steps off these with C# 14 static extension
// members, e.g. `extension(Given) { public static Task<Patient> PatientExists(...) }`.
// They carry no behaviour and are never instantiated; they exist only to give the
// Given/When/Then call sites a type to extend.
//
// Note: if the final C# 14 compiler shape rejects a static class as an extension
// receiver, these become non-static uninstantiable markers without touching call sites.

/// <summary>Phase marker for arrange / precondition steps.</summary>
public static class Given { }

/// <summary>Phase marker for the action under test.</summary>
public static class When { }

/// <summary>Phase marker for assertions / postconditions.</summary>
public static class Then { }
