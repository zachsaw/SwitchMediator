namespace Mediator.Switch.SourceGenerator;

/// <summary>
/// Mirror of <c>Mediator.Switch.MediatorServiceProvider</c>, read from the
/// <c>[SwitchMediator]</c> attribute at compile time. Kept in sync by name so the generator
/// stays usable against older <c>Mediator.Switch</c> assemblies where the property does not
/// exist yet (in which case <see cref="Abstraction"/> applies).
/// </summary>
public enum MediatorServiceProviderMode
{
    Abstraction,
    Direct,
}
