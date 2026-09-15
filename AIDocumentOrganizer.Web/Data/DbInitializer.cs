using AIDocumentOrganizer.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AIDocumentOrganizer.Web.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        try
        {
            await db.Database.MigrateAsync();

            // Seed roles
            foreach (var role in new[] { "Admin", "User" })
            {
                if (!await roleMgr.RoleExistsAsync(role))
                    await roleMgr.CreateAsync(new IdentityRole(role));
            }

            // Seed admin user
            const string adminEmail = "admin@docorganizer.com";
            if (await userMgr.FindByEmailAsync(adminEmail) is null)
            {
                var admin = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "System Administrator",
                    EmailConfirmed = true,
                    IsActive = true
                };
                var result = await userMgr.CreateAsync(admin, "Admin@123!");
                if (result.Succeeded)
                    await userMgr.AddToRoleAsync(admin, "Admin");
            }

            // Seed demo user
            const string userEmail = "user@docorganizer.com";
            if (await userMgr.FindByEmailAsync(userEmail) is null)
            {
                var user = new ApplicationUser
                {
                    UserName = userEmail,
                    Email = userEmail,
                    FullName = "Demo User",
                    EmailConfirmed = true,
                    IsActive = true
                };
                var result = await userMgr.CreateAsync(user, "User@123!");
                if (result.Succeeded)
                    await userMgr.AddToRoleAsync(user, "User");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
        }
    }
}
