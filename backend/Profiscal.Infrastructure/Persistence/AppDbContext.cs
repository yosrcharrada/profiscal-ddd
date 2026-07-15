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
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<WorkTaskCollaborator> WorkTaskCollaborators => Set<WorkTaskCollaborator>();
    public DbSet<Reclamation> Reclamations => Set<Reclamation>();
    public DbSet<JortActivity> JortActivities => Set<JortActivity>();
    public DbSet<Notification> Notifications => Set<Notification>();

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

            // Manager → consultants self-reference. Deleting a manager detaches
            // their consultants instead of deleting them.
            e.HasOne(u => u.Manager)
             .WithMany(m => m.Consultants)
             .HasForeignKey(u => u.ManagerId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<WorkTask>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Title).IsRequired().HasMaxLength(256);
            e.Property(t => t.Description).HasMaxLength(4000);
            e.Property(t => t.ClientName).HasMaxLength(256);
            e.Property(t => t.SubmitNote).HasMaxLength(2000);
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(t => new { t.ConsultantId, t.Status });
            e.HasIndex(t => new { t.ManagerId, t.CreatedAt });

            e.HasOne(t => t.Manager)
             .WithMany()
             .HasForeignKey(t => t.ManagerId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.Consultant)
             .WithMany()
             .HasForeignKey(t => t.ConsultantId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(t => t.Collaborators)
             .WithOne(c => c.WorkTask)
             .HasForeignKey(c => c.WorkTaskId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkTaskCollaborator>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.WorkTaskId, c.UserId }).IsUnique();
            e.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Reclamation>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Subject).IsRequired().HasMaxLength(256);
            e.Property(r => r.Description).IsRequired().HasMaxLength(4000);
            e.Property(r => r.AdminNote).HasMaxLength(2000);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Category).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(r => new { r.Status, r.CreatedAt });

            e.HasOne(r => r.CreatedBy)
             .WithMany()
             .HasForeignKey(r => r.CreatedById)
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

        builder.Entity<JortActivity>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Title).IsRequired().HasMaxLength(512);
            e.Property(a => a.Description).HasMaxLength(4000);
            e.Property(a => a.Category).HasMaxLength(32);
            e.Property(a => a.PublishedText).HasMaxLength(64);
            e.Property(a => a.Hash).IsRequired().HasMaxLength(64);
            e.HasIndex(a => a.Hash).IsUnique();
            e.HasIndex(a => a.PublishedOn);
        });

        builder.Entity<Notification>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Type).HasMaxLength(32);
            e.Property(n => n.Title).IsRequired().HasMaxLength(256);
            e.Property(n => n.Body).HasMaxLength(1024);
            e.Property(n => n.LinkUrl).HasMaxLength(512);
            e.HasIndex(n => new { n.RecipientId, n.IsRead, n.CreatedAt });

            e.HasOne(n => n.Recipient)
             .WithMany()
             .HasForeignKey(n => n.RecipientId)
             .OnDelete(DeleteBehavior.Cascade);
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
