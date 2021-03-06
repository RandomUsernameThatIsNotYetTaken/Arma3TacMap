using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using Arma3TacMapLibrary;
using Arma3TacMapLibrary.Arma3;
using Arma3TacMapLibrary.Hubs;
using Arma3TacMapLibrary.Maps;
using Arma3TacMapWebApp.Entities;
using Arma3TacMapWebApp.Maps;
using Arma3TacMapWebApp.Security;
using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arma3TacMapWebApp
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddControllersWithViews()
                .AddViewLocalization()
                .AddDataAnnotationsLocalization();

            services.AddSignalR();
            services.AddScoped<IMapService<MapId>, MapService>();
            services.AddScoped<MapService>();
            services.AddScoped<MapInfosService>();
            services.AddScoped<MapPreviewService>();
            services.AddSingleton<ScreenshotService>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddDbContext<Arma3TacMapContext>(options =>
                options.UseSqlite(Configuration.GetConnectionString(nameof(Arma3TacMapContext))));

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.LoginPath = "/Authentication/SignIn";
                options.LogoutPath = "/Authentication/SignOut";
                options.AccessDeniedPath = "/Authentication/Denied";
            })
            .AddSteam(s => s.ApplicationKey = Configuration.GetValue<string>("Steam:Key"))
            .AddApiKeyInHeader<ApiKeyProvider>(options => {
                options.SuppressWWWAuthenticateHeader = true;
                options.KeyName = "ApiKey";
            });

            services.AddAuthorization(options =>
            {
                var admins = Configuration.GetSection("Admins").Get<string[]>();
                options.AddPolicy("Admin", policy => policy.RequireClaim(ClaimTypes.NameIdentifier, admins.ToArray()));
                options.AddPolicy("ApiClient", policy => policy.RequireClaim(User.IsServiceClaim, "true"));
                options.AddPolicy("AnyAuthenticated", policy => policy.RequireAuthenticatedUser());
                options.AddPolicy("LoggedUser", policy => policy.RequireAssertion(ctx => !string.IsNullOrEmpty(MapService.GetSteamId(ctx.User))));
            });

            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(Configuration.GetValue<string>("UnixKeysDirectory")))
                    .SetApplicationName("Arma3Event");
            }

            var origins = Configuration.GetSection("CorsOrigins").Get<string[]>();
            services.AddCors(o => o.AddPolicy("SignalRHub", builder =>
            {
                builder.WithOrigins(origins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .DisallowCredentials();
            }));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseForwardedHeaders(new ForwardedHeadersOptions
                {
                    ForwardedHeaders = ForwardedHeaders.XForwardedProto
                });

                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseArma3TacMapStaticFiles();

            app.UseRouting();
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseCookiePolicy(new CookiePolicyOptions()
            {
                HttpOnly = HttpOnlyPolicy.Always,
                Secure = CookieSecurePolicy.SameAsRequest,
                MinimumSameSitePolicy = SameSiteMode.Lax
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

                endpoints.MapHub<MapHub<MapId>>("/MapHub").RequireCors("SignalRHub");
            });
        }
    }
}
