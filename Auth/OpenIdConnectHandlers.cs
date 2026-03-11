using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Security.Claims;

namespace MustMail.Auth;

public static class OpenIdConnectHandlers
{
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        ILogger logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("OIDC");

        using IServiceScope scope = context.HttpContext.RequestServices.CreateScope();
        DatabaseContext dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        logger.LogDebug("OIDC token validated event triggered.");

        ClaimsIdentity claimsIdentity = (ClaimsIdentity)context.Principal!.Identity!;

        string? userId = context.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        string? name = context.Principal!.FindFirstValue("name");
        string? email = context.Principal!.FindFirstValue(ClaimTypes.Email);

        // Check required claims are not null
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
        {
            logger.LogWarning(
                "Missing required claims during OIDC token validation. Sub: {Sub}, Name: {Name}, Email: {Email}",
                userId, name, email);
            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
            "Authenticated user detected during token validation. Sub: {Sub}, Email: {Email}",
            userId, email);

        User? user = await dbContext.FindAsync<User>(userId);

        // If the user does not exist then create a record for them in the database
        if (user == null)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("User not found in database. Creating user. Sub: {Sub}", userId);

            // Create the user
            user = new User
            {
                Id = userId,
                Name = name,
                Email = email,
                Profile = new Profile
                {
                    DateFormat = "dddd dd MMMM yyyy",
                    TimeFormat = "HH:mm",
                    TimeZone = "GMT"
                }
            };

            // If this is the first user then make them an admin
            if (!await dbContext.User.AnyAsync())
            {
                user.Admin = true;
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("First user detected, granting admin privileges. Sub: {Sub}", userId);
            }

            await dbContext.User.AddAsync(user);
            await dbContext.SaveChangesAsync();

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("User created successfully. Sub: {Sub}, Email: {Email}", userId, email);
        }

        // If the user is an admin set the "must_mail_role" claim to admin
        if (user.Admin && !context.Principal.HasClaim(c => c.Type == "must_mail_role"))
        {
            claimsIdentity.AddClaim(new Claim("must_mail_role", "admin"));

            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Admin role claim added for user. Sub: {Sub}", userId);
        }

        // Update name/email from identity provider if changed
        dbContext.Entry(user).CurrentValues.SetValues(new { Name = name, Email = email });

        if (dbContext.Entry(user).Properties.Any(p => p.IsModified))
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("User record updated from identity provider claims. Sub: {Sub}", userId);

            await dbContext.SaveChangesAsync();
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("OIDC token validation processing complete. Sub: {Sub}", userId);
    }
}