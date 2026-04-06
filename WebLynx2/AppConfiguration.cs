using System;
using Microsoft.Extensions.Configuration;

namespace WebLynx2;

public static class AppConfiguration
{
    public static AppSettings Load()
    {
        var settings = new AppSettings();
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build()
            .Bind(settings);
        return settings;
    }
}
