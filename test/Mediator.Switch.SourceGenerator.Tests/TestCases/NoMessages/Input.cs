using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.NoMessages;

[SwitchMediator]
public partial class TestMediator;

public class UtilityClass
{
    public static string GetVersion() => "1.0";
}

public interface IOtherInterface { }

public struct SomeData : IOtherInterface { }