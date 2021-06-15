using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MTGViewer.Areas.Identity.Data;

[assembly: HostingStartup(typeof(MTGViewer.Areas.Identity.IdentityHostingStartup))]
namespace MTGViewer.Areas.Identity
{
    public class IdentityHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) => {
                // services.AddDbContext<MTGViewerIdentityDbContext>(options =>
                //     options.UseSqlite(
                //         context.Configuration.GetConnectionString("MTGViewerIdentityDbContextConnection")));

                services.AddDefaultIdentity<CardUser>(options => options.SignIn.RequireConfirmedAccount = true)
                    .AddEntityFrameworkStores<MTGCardContext>();
            });
        }
    }
}