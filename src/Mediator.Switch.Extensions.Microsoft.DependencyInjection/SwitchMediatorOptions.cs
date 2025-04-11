using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection;

public class SwitchMediatorOptions
{
	/// <summary>
	/// The assemblies scanned for handlers and behaviors.
	/// </summary>
	public Assembly[] TargetAssemblies { get; set; } = [];
	
	/// <summary>
	/// The default lifetime for the services registered by the mediator.
	/// </summary>
	public ServiceLifetime ServiceLifetime { get; set; } = ServiceLifetime.Scoped;
}