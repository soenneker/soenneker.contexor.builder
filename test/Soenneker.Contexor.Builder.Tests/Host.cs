using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Soenneker.TestHosts.Unit;
using Soenneker.Utils.Test;
using Soenneker.Contexor.Builder.Registrars;
using Soenneker.Utils.Process.Registrars;

namespace Soenneker.Contexor.Builder.Tests;

public sealed class Host : UnitTestHost
{
    public override Task InitializeAsync()
    {
        SetupIoC(Services);

        return base.InitializeAsync();
    }

    private static void SetupIoC(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddSerilog(dispose: false);
        });

        IConfiguration config = TestUtil.BuildConfig();
        services.AddSingleton(config);

        services.AddContexorBuilderAsScoped();
        services.AddProcessUtilAsScoped();
    }
}
