using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Contexor.Builder.Abstract;
using Soenneker.JsonSchema.ToCSharp.Registrars;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.Directory.Registrars;

namespace Soenneker.Contexor.Builder.Registrars;

/// <summary>
/// A utility for generating complete C# clients from JSON schemas.
/// </summary>
public static class ContexorBuilderRegistrar
{
    /// <summary>
    /// Adds <see cref="IContexorBuilder"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddContexorBuilderAsSingleton(this IServiceCollection services)
    {
        services.AddFileUtilAsSingleton();
        services.AddDirectoryUtilAsSingleton();
        services.AddJsonSchemaToCSharpAsSingleton();
        services.TryAddSingleton<IContexorBuilder, ContexorBuilder>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IContexorBuilder"/> as a scoped service. <para/>
    /// </summary>
    public static IServiceCollection AddContexorBuilderAsScoped(this IServiceCollection services)
    {
        services.AddFileUtilAsScoped();
        services.AddDirectoryUtilAsScoped();
        services.AddJsonSchemaToCSharpAsScoped();
        services.TryAddScoped<IContexorBuilder, ContexorBuilder>();

        return services;
    }
}

