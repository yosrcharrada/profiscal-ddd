using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Profiscal.Application.Common.Interfaces;
using Profiscal.Domain.Entities;

namespace Profiscal.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IApplicationDbContext
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuthAuditLog> AuthAuditLogs => Set<AuthAuditLog>();
    public DbSet<FiscalConsultation> FiscalConsultations => Set<FiscalConsultation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        builder.Entity<AppUser>(e =>
        {
            e.HasMany(u => u.RefreshTokens)
             .WithOne(r => r.AppUser)
             .HasForeignKey(r => r.AppUserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.TokenHash).IsRequired().HasMaxLength(64); // SHA-256 hex
            e.HasIndex(r => r.TokenHash).IsUnique();
            e.HasIndex(r => r.AppUserId);
            e.Property(r => r.CreatedByIp).HasMaxLength(45);   // IPv6 max
            e.Property(r => r.RevokedByIp).HasMaxLength(45);
            e.Property(r => r.UserAgent).HasMaxLength(512);
            e.Property(r => r.RevokedReason).HasMaxLength(128);
            e.Property(r => r.ReplacedByTokenHash).HasMaxLength(64);
        });

        builder.Entity<AuthAuditLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Email).IsRequired().HasMaxLength(256);
            e.Property(l => l.Event).HasConversion<string>().HasMaxLength(40);
            e.Property(l => l.Detail).HasMaxLength(512);
            e.Property(l => l.IpAddress).HasMaxLength(45);
            e.Property(l => l.UserAgent).HasMaxLength(512);
            e.HasIndex(l => new { l.AppUserId, l.CreatedAt });
        });

        builder.Entity<FiscalConsultation>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Reference).HasMaxLength(128);
            e.Property(c => c.ClientName).HasMaxLength(256);
            e.Property(c => c.Method).HasMaxLength(64);
            e.Property(c => c.Branches).HasMaxLength(256);
            e.Property(c => c.Countries).HasMaxLength(256);
            e.Property(c => c.RatingComment).HasMaxLength(1024);
            e.HasIndex(c => c.ClientName);
            e.HasIndex(c => new { c.OwnerUserId, c.CreatedAt });
        });
    }
}
