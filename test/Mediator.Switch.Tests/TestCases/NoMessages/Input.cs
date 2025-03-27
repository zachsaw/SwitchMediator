using Mediator.Switch; // Might still be needed depending on how generator finds files

namespace Test.NoMessages;

// This namespace intentionally contains no IRequest or INotification types.
// Add some other unrelated types to ensure the generator doesn't crash.

public class UtilityClass
{
    public static string GetVersion() => "1.0";
}

public interface IOtherInterface { }

public struct SomeData : IOtherInterface { }