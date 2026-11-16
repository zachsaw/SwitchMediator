using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Mediator.Switch.Tests;

public class MediatorGraphValidationTests
{
    [Fact]
    public async Task ValidateOnStart_ShouldReportTheComponentsThatCannotBeResolved()
    {
        var (provider, scope, _, _, _) = MediatorTestSetup.Setup(
            configureMediator: options => options.ValidateOnStart = true);

        using (provider)
        using (scope)
        {
            var hostedServices = provider.GetServices<IHostedService>().ToList();
            Assert.Single(hostedServices);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                foreach (var hostedService in hostedServices)
                {
                    await hostedService.StartAsync(CancellationToken.None);
                }
            });

            Assert.Contains(nameof(BrokenRequestHandler), exception.Message);
        }
    }

    [Fact]
    public void ValidateOnStart_ShouldBeOffByDefault()
    {
        var (provider, scope, _, _, _) = MediatorTestSetup.Setup();

        using (provider)
        using (scope)
        {
            Assert.Empty(provider.GetServices<IHostedService>());
        }
    }
}
