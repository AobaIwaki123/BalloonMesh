using System.Security.Claims;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace BalloonMesh.Server.Auth;

public static class AuthRoles
{
    public const string Viewer = "Viewer";
    public const string Operator = "Operator";
    public const string Admin = "Admin";
}

public sealed record AuthenticatedUser(
    string Name,
    string Provider,
    string? Avatar,
    bool CanOperate,
    bool CanAdmin)
{
    public static AuthenticatedUser From(ClaimsPrincipal principal)
    {
        var identity = principal.Identity;
        var name = principal.FindFirstValue("display_name")
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? identity?.Name
            ?? "Operator";
        var provider = principal.FindFirstValue("provider") ?? "unknown";
        var avatar = principal.FindFirstValue("avatar")
            ?? principal.FindFirstValue("picture");
        var admin = principal.IsInRole(AuthRoles.Admin);
        var operate = admin || principal.IsInRole(AuthRoles.Operator);
        return new AuthenticatedUser(name, provider, avatar, operate, admin);
    }
}

public static class AuthSetup
{
    private const string SecurityStampClaimType = "AspNet.Identity.SecurityStamp";
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string LocalMode = "Local";
    public const string OidcMode = "Oidc";
    public const string DiscordMode = "Discord";
    public const string MockMode = "Mock";

    public static string ResolveMode(IConfiguration configuration)
    {
        var configured = configuration["Authentication:Mode"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var legacyProviders = new List<string>();
        if (HasOidcConfiguration(configuration))
        {
            legacyProviders.Add(OidcMode);
        }
        if (HasDiscordConfiguration(configuration))
        {
            legacyProviders.Add(DiscordMode);
        }
        if (configuration.GetValue("Authentication:Mock:Enabled", false))
        {
            legacyProviders.Add(MockMode);
        }
        if (legacyProviders.Count > 1)
        {
            throw new InvalidOperationException(
                "Multiple legacy authentication providers are configured; set Authentication:Mode explicitly.");
        }
        return legacyProviders.SingleOrDefault() ?? LocalMode;
    }

    public static bool IsMode(IConfiguration configuration, string mode) =>
        string.Equals(ResolveMode(configuration), mode,
            StringComparison.OrdinalIgnoreCase);

    private static void ValidateMode(IConfiguration configuration)
    {
        var mode = ResolveMode(configuration);
        if (mode is not LocalMode and not OidcMode and not DiscordMode and not MockMode)
        {
            throw new InvalidOperationException(
                $"Authentication:Mode must be one of {LocalMode}, {OidcMode}, {DiscordMode}, or {MockMode}.");
        }
    }

    public static bool OidcEnabled(IConfiguration configuration) =>
        IsMode(configuration, OidcMode) &&
        HasOidcConfiguration(configuration);

    public static bool DiscordEnabled(IConfiguration configuration) =>
        IsMode(configuration, DiscordMode) &&
        HasDiscordConfiguration(configuration);

    public static bool MockEnabled(IConfiguration configuration) =>
        IsMode(configuration, MockMode) &&
        configuration.GetValue("Authentication:Mock:Enabled", false);

    public static bool LocalEnabled(IConfiguration configuration) =>
        IsMode(configuration, LocalMode) &&
        configuration.GetValue("Authentication:Local:Enabled", true);

    public static IServiceCollection AddBalloonMeshAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ValidateMode(configuration);
        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieScheme;
            options.DefaultChallengeScheme = CookieScheme;
        });

        authentication.AddCookie(options =>
        {
            options.Cookie.Name = "BalloonMesh.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
                RedirectOrStatus(context, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context =>
                RedirectOrStatus(context, StatusCodes.Status403Forbidden);
            options.Events.OnValidatePrincipal = ValidateLocalPrincipalAsync;
        });

        if (OidcEnabled(configuration))
        {
            authentication.AddOpenIdConnect("oidc", options =>
            {
                options.Authority = configuration["Authentication:Oidc:Authority"];
                options.ClientId = configuration["Authentication:Oidc:ClientId"];
                options.ClientSecret = configuration["Authentication:Oidc:ClientSecret"];
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";
                options.SignedOutRedirectUri = "/login";
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
                options.ClaimActions.MapJsonKey("avatar", "picture");
                options.Events.OnTokenValidated = context =>
                {
                    NormalizeOidcRoles(context.Principal, configuration);
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/login?error=OIDC%20login%20failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        if (DiscordEnabled(configuration))
        {
            authentication.AddOAuth("discord", options =>
            {
                options.ClientId = configuration["Authentication:Discord:ClientId"]!;
                options.ClientSecret = configuration["Authentication:Discord:ClientSecret"]!;
                options.CallbackPath = "/signin-discord";
                options.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
                options.TokenEndpoint = "https://discord.com/api/oauth2/token";
                options.UserInformationEndpoint = "https://discord.com/api/users/@me";
                options.SaveTokens = true;
                options.Scope.Add("identify");
                options.Scope.Add("email");
                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "username");
                options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
                options.Events.OnCreatingTicket = async context =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                    using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
                    response.EnsureSuccessStatusCode();
                    using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                    context.RunClaimActions(user.RootElement);
                    NormalizeDiscordIdentity(context, user.RootElement, configuration);
                };
                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/login?error=Discord%20login%20failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthRoles.Viewer, policy =>
                policy.RequireAuthenticatedUser().RequireRole(
                    AuthRoles.Viewer, AuthRoles.Operator, AuthRoles.Admin));
            options.AddPolicy(AuthRoles.Operator, policy =>
                policy.RequireAuthenticatedUser().RequireRole(
                    AuthRoles.Operator, AuthRoles.Admin));
            options.AddPolicy(AuthRoles.Admin, policy =>
                policy.RequireAuthenticatedUser().RequireRole(AuthRoles.Admin));
        });

        return services;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/login/oidc", (HttpContext context, IConfiguration configuration,
            string? returnUrl) =>
        {
            if (!OidcEnabled(configuration))
            {
                return Results.NotFound();
            }
            return Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = SafeReturnUrl(returnUrl)
            }, new[] { "oidc" });
        }).AllowAnonymous();

        endpoints.MapGet("/auth/login/discord", (HttpContext context, IConfiguration configuration,
            string? returnUrl) =>
        {
            if (!DiscordEnabled(configuration))
            {
                return Results.NotFound();
            }
            return Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = SafeReturnUrl(returnUrl)
            }, new[] { "discord" });
        }).AllowAnonymous();

        endpoints.MapPost("/auth/login/local", async (HttpContext context,
            IAntiforgery antiforgery, UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            if (!LocalEnabled(context.RequestServices.GetRequiredService<IConfiguration>()))
            {
                return Results.NotFound();
            }
            await antiforgery.ValidateRequestAsync(context);
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var user = await userManager.FindByNameAsync(username);
            if (user is null ||
                !(await signInManager.CheckPasswordSignInAsync(user, password,
                    lockoutOnFailure: true)).Succeeded)
            {
                return Results.Redirect("/login?error=Invalid%20username%20or%20password");
            }

            var role = (await userManager.GetRolesAsync(user)).SingleOrDefault();
            if (role is not AuthRoles.Viewer and not AuthRoles.Operator and not AuthRoles.Admin)
            {
                return Results.Redirect("/login?error=Your%20account%20has%20no%20access%20role");
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.DisplayName),
                new("display_name", user.DisplayName),
                new("provider", "Local"),
                new(SecurityStampClaimType,
                    user.SecurityStamp ?? string.Empty),
                new(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, CookieScheme);
            await context.SignInAsync(CookieScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

            return Results.Redirect(SafeReturnUrl(form["returnUrl"].ToString()));
        }).AllowAnonymous();

        endpoints.MapGet("/auth/login/mock", async (HttpContext context, IConfiguration configuration,
            string? role) =>
        {
            if (!MockEnabled(configuration))
            {
                return Results.NotFound();
            }
            var selectedRole = role is AuthRoles.Viewer or AuthRoles.Operator or AuthRoles.Admin
                ? role
                : AuthRoles.Admin;
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, $"mock-{selectedRole.ToLowerInvariant()}"),
                new(ClaimTypes.Name, $"Demo {selectedRole}"),
                new("display_name", $"Demo {selectedRole}"),
                new("provider", "Mock"),
                new(ClaimTypes.Role, selectedRole)
            };
            var identity = new ClaimsIdentity(claims, CookieScheme);
            await context.SignInAsync(CookieScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });
            return Results.Redirect("/");
        }).AllowAnonymous();

        endpoints.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieScheme);
            return Results.Redirect("/login");
        }).AllowAnonymous();

        return endpoints;
    }

    public static string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !returnUrl.StartsWith('/') ||
            returnUrl.StartsWith("//") ||
            returnUrl.Contains("\\", StringComparison.Ordinal))
        {
            return "/";
        }
        return returnUrl;
    }

    private static Task RedirectOrStatus(
        RedirectContext<CookieAuthenticationOptions> context,
        int statusCode)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") ||
            path.StartsWithSegments("/hubs") ||
            path.StartsWithSegments("/ws"))
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        if (statusCode == StatusCodes.Status403Forbidden)
        {
            context.Response.Redirect("/login?error=forbidden");
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }
        return Task.CompletedTask;
    }

    private static void NormalizeOidcRoles(ClaimsPrincipal? principal, IConfiguration configuration)
    {
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }
        var roles = identity.FindAll(claim =>
                claim.Type is ClaimTypes.Role or "role" or "roles" or "groups" or "permissions")
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var adminRoles = Split(configuration["Authentication:Oidc:AdminRoles"], "balloon-admin");
        var operatorRoles = Split(configuration["Authentication:Oidc:OperatorRoles"], "balloon-operator");
        var defaultRole = configuration["Authentication:Oidc:DefaultRole"] ?? AuthRoles.Viewer;

        var role = roles.Contains(AuthRoles.Admin) ||
                   adminRoles.Any(roleName => roles.Contains(roleName))
            ? AuthRoles.Admin
            : roles.Contains(AuthRoles.Operator) ||
              operatorRoles.Any(roleName => roles.Contains(roleName))
                ? AuthRoles.Operator
                : defaultRole;

        RemoveClaims(identity, ClaimTypes.Role);
        if (!string.IsNullOrWhiteSpace(role) &&
            !string.Equals(role, "None", StringComparison.OrdinalIgnoreCase))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
        identity.AddClaim(new Claim("provider", "OIDC"));
    }

    private static void NormalizeDiscordIdentity(OAuthCreatingTicketContext context,
        JsonElement user, IConfiguration configuration)
    {
        if (context.Identity is not ClaimsIdentity identity)
        {
            return;
        }
        var id = GetString(user, "id") ?? string.Empty;
        var username = GetString(user, "username") ?? "Discord User";
        var globalName = GetString(user, "global_name");
        var avatarHash = GetString(user, "avatar");

        identity.AddClaim(new Claim("display_name",
            string.IsNullOrWhiteSpace(globalName) ? username : globalName));
        identity.AddClaim(new Claim("provider", "Discord"));
        if (!string.IsNullOrWhiteSpace(avatarHash))
        {
            identity.AddClaim(new Claim("avatar",
                $"https://cdn.discordapp.com/avatars/{id}/{avatarHash}.png?size=128"));
        }
        else
        {
            identity.AddClaim(new Claim("avatar",
                "https://cdn.discordapp.com/embed/avatars/0.png"));
        }

        var adminIds = Split(configuration["Authentication:Discord:AdminUserIds"], string.Empty);
        var operatorIds = Split(configuration["Authentication:Discord:OperatorUserIds"], string.Empty);
        var defaultRole = configuration["Authentication:Discord:DefaultRole"] ?? AuthRoles.Viewer;
        var role = adminIds.Contains(id)
            ? AuthRoles.Admin
            : operatorIds.Contains(id)
                ? AuthRoles.Operator
                : defaultRole;

        RemoveClaims(identity, ClaimTypes.Role);
        if (!string.IsNullOrWhiteSpace(role) &&
            !string.Equals(role, "None", StringComparison.OrdinalIgnoreCase))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static HashSet<string> Split(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value;
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries |
                               StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void RemoveClaims(ClaimsIdentity identity, string type)
    {
        foreach (var claim in identity.FindAll(type).ToArray())
        {
            identity.RemoveClaim(claim);
        }
    }

    private static bool HasOidcConfiguration(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Authentication:Oidc:Authority"]) &&
        !string.IsNullOrWhiteSpace(configuration["Authentication:Oidc:ClientId"]);

    private static bool HasDiscordConfiguration(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Authentication:Discord:ClientId"]) &&
        !string.IsNullOrWhiteSpace(configuration["Authentication:Discord:ClientSecret"]);

    private static async Task ValidateLocalPrincipalAsync(
        CookieValidatePrincipalContext context)
    {
        if (!string.Equals(context.Principal?.FindFirstValue("provider"), "Local",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(SecurityStampClaimType);
        var userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = context.HttpContext.RequestServices
            .GetRequiredService<SignInManager<ApplicationUser>>();
        var user = string.IsNullOrWhiteSpace(userId)
            ? null
            : await userManager.FindByIdAsync(userId);
        var validStamp = user is not null &&
                         await signInManager.ValidateSecurityStampAsync(user, stamp);
        var currentRoles = user is null
            ? []
            : await userManager.GetRolesAsync(user);
        var cookieRoles = context.Principal?.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        if (!validStamp || !cookieRoles.SetEquals(currentRoles))
        {
            context.RejectPrincipal();
        }
    }
}
