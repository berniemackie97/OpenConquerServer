using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenConquer.Application.Accounts.Authentication.Passwords;
using OpenConquer.Application.Accounts.Registration;
using OpenConquer.Infrastructure.Persistence.Accounts.Registration;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;
using OpenConquer.Infrastructure.Security.Accounts.Registration;

namespace OpenConquer.Infrastructure.DependencyInjection;

public static class AccountRegistrationServiceCollectionExtensions
{
    /// <summary>
    /// Enables self-service account registration through a dedicated registration database identity.
    /// </summary>
    public static IServiceCollection AddAccountRegistration(this IServiceCollection services, string accountConnectionString,
        string registrationConnectionString, AccountRegistrationProtectionOptions protectionOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(protectionOptions);

        AccountRegistrationConnectionPolicy.Validate(accountConnectionString, registrationConnectionString);

        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
        services.TryAddSingleton<IAccountPasswordHasher, AccountPasswordHasher>();
        services.AddSingleton(protectionOptions);
        services.AddSingleton<IAccountRegistrationLimiter, AccountRegistrationProtection>();
        services.AddSingleton<IAccountRegistrationStore>(_ => new AccountRegistrationStore(registrationConnectionString));
        services.AddSingleton<AccountRegistrationService>();

        return services;
    }
}
