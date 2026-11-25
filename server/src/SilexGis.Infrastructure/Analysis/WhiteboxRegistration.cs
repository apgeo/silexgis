// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SilexGis.Infrastructure.Analysis;

/// <summary>Registers the WhiteboxTools switch.</summary>
public static class WhiteboxRegistration
{
    /// <summary>
    /// Whether WhiteboxTools runs, and where, is the installation's choice, made once at start-up
    /// like the survey compiler's: none unless it asked. Unlike the compiler's, a mode the
    /// application does not know stops the start instead of quietly meaning none.
    /// </summary>
    public static IServiceCollection AddWhiteboxTools(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WhiteboxOptions>()
            .Bind(configuration.GetSection(WhiteboxOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WhiteboxOptions>, WhiteboxOptionsValidator>();

        var choice = configuration.GetSection(WhiteboxOptions.SectionName).Get<WhiteboxOptions>();
        if (choice?.IsLocal == true)
        {
            services.AddSingleton<IWhiteboxTools, LocalWhiteboxTools>();
        }
        else if (choice?.IsService == true)
        {
            services.AddHttpClient(ServiceWhiteboxTools.HttpClientName);
            services.AddSingleton<IWhiteboxTools, ServiceWhiteboxTools>();
        }
        else
        {
            services.AddSingleton<IWhiteboxTools, NoWhiteboxTools>();
        }

        return services;
    }
}
