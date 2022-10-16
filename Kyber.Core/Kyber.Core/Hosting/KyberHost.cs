using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Kyber.Core.Hosting;

/// <summary>
/// Helper methods for building and configuring a Kyber game.
/// </summary>
public static class KyberHost
{
    /// <summary>
    /// Creates a basic HostBuilder with the following presets:
    ///   - Sets the content root to the current directory.
    ///   - Adds command line args to the host configuration.
    ///   - Adds command line args to the app configuration.
    ///   - Enables console and debug logging.
    /// </summary>
    /// <param name="args">Command line args passed.</param>
    /// <returns>A HostBuilder ready for additional configuration.</returns>
    public static IHostBuilder CreateDefaultBuilder(params string[] args)
    {
        var builder = new HostBuilder();

        builder.UseContentRoot(Directory.GetCurrentDirectory());
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddEnvironmentVariables();
            if (args != null && args.Length > 0) config.AddCommandLine(args);
        });
        //builder.ConfigureAppConfiguration((hostingContext, config) =>
        //{
        //    if (args != null && args.Length > 0) config.AddCommandLine(args);
        //});

        builder.ConfigureLogging(config =>
        {
            config.AddSimpleConsole(options =>
            {
                //options.TimestampFormat = "HH:mm:ss";
                options.SingleLine = true;
            })
            .AddDebug();
        });

        return builder;
    }
}
