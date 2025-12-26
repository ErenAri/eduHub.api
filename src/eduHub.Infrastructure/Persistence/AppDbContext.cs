using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace eduHub.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenant _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant)
        : base(options)
    {
        _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
        _tenant = new DesignTimeTenant();
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<OrganizationInvite> OrganizationInvites => Set<OrganizationInvite>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();

    public override int SaveChanges()
    {
        ApplyTenantInfo();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantInfo();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTenantInfo();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantInfo();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasKey(o => o.Id);

            entity.Property(o => o.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(o => o.Slug)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(o => o.LogoUrl)
                .HasMaxLength(500);

            entity.Property(o => o.PrimaryColor)
                .HasMaxLength(32);

            entity.Property(o => o.Timezone)
                .HasMaxLength(120);

            entity.Property(o => o.SubscriptionPlan)
                .HasMaxLength(80);

            entity.Property(o => o.IsActive)
                .HasDefaultValue(true);

            entity.Property(o => o.CreatedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(o => o.Slug)
                .IsUnique()
                .HasDatabaseName("IX_organizations_Slug");

            entity.HasOne(o => o.CreatedByUser)
                .WithMany()
                .HasForeignKey(o => o.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrganizationMember>(entity =>
        {
            entity.ToTable("organization_members");
            entity.HasKey(m => new { m.OrganizationId, m.UserId });

            entity.Property(m => m.Role)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(m => m.Status)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(m => m.JoinedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.HasOne(m => m.Organization)
                .WithMany(o => o.Members)
                .HasForeignKey(m => m.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(m => m.UserId)
                .HasDatabaseName("IX_org_members_UserId");

            entity.HasQueryFilter(m =>
                _tenant.IsPlatformScope || m.OrganizationId == _tenant.OrganizationId);
        });

        modelBuilder.Entity<OrganizationInvite>(entity =>
        {
            entity.ToTable("organization_invites");
            entity.HasKey(i => i.Id);

            entity.Property(i => i.Email)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(i => i.Role)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(i => i.TokenHash)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(i => i.ExpiresAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.Property(i => i.CreatedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.Property(i => i.UsedAtUtc)
                .HasColumnType("timestamp with time zone");

            entity.Property(i => i.RevokedAtUtc)
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(i => i.TokenHash)
                .IsUnique()
                .HasDatabaseName("IX_org_invites_TokenHash");

            entity.HasIndex(i => new { i.OrganizationId, i.Email })
                .HasDatabaseName("IX_org_invites_OrganizationId_Email");

            entity.HasOne(i => i.Organization)
                .WithMany(o => o.Invites)
                .HasForeignKey(i => i.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.CreatedByUser)
                .WithMany()
                .HasForeignKey(i => i.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(i => i.UsedByUser)
                .WithMany()
                .HasForeignKey(i => i.UsedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(i =>
                _tenant.IsPlatformScope || i.OrganizationId == _tenant.OrganizationId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(a => a.Id);

            entity.Property(a => a.OrganizationId)
                .IsRequired();

            entity.Property(a => a.Action)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(a => a.EntityType)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(a => a.EntityId)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(a => a.Summary)
                .HasMaxLength(500);

            entity.Property(a => a.CreatedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.HasOne(a => a.CreatedByUser)
                .WithMany()
                .HasForeignKey(a => a.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(a => new { a.OrganizationId, a.CreatedAtUtc })
                .HasDatabaseName("IX_audit_logs_OrganizationId_CreatedAtUtc");

            entity.HasQueryFilter(a =>
                _tenant.IsPlatformScope || a.OrganizationId == _tenant.OrganizationId);
        });

        modelBuilder.Entity<Building>(entity =>
        {
            entity.ToTable("buildings");
            entity.HasKey(b => b.Id);

            entity.Property(b => b.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(b => b.OrganizationId)
                .IsRequired();

            entity.Property(b => b.IsDeleted)
                .HasDefaultValue(false);

            entity.HasOne(b => b.Organization)
                .WithMany()
                .HasForeignKey(b => b.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(b => new { b.OrganizationId, b.Name })
                .HasDatabaseName("IX_buildings_OrganizationId_Name");

            entity.HasQueryFilter(b =>
                !b.IsDeleted &&
                (_tenant.IsPlatformScope || b.OrganizationId == _tenant.OrganizationId));
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.ToTable("rooms");
            entity.HasKey(r => r.Id);

            entity.Property(r => r.Code)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(r => r.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(r => r.Capacity);

            entity.Property(r => r.OrganizationId)
                .IsRequired();

            entity.Property(r => r.IsDeleted)
                .HasDefaultValue(false);

            entity.HasOne(r => r.Building)
                .WithMany(b => b.Rooms)
                .HasForeignKey(r => r.BuildingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Organization)
                .WithMany()
                .HasForeignKey(r => r.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => new { r.BuildingId, r.Code })
                .IsUnique()
                .HasDatabaseName("IX_rooms_BuildingId_Code");

            entity.HasIndex(r => new { r.OrganizationId, r.BuildingId })
                .HasDatabaseName("IX_rooms_OrganizationId_BuildingId");

            entity.HasQueryFilter(r =>
                !r.IsDeleted &&
                (_tenant.IsPlatformScope || r.OrganizationId == _tenant.OrganizationId));
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.ToTable("reservations");
            entity.HasKey(r => r.Id);

            entity.Property(r => r.StartTimeUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.Property(r => r.EndTimeUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.Property(r => r.CreatedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.Property(r => r.Purpose)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(r => r.Status)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(r => r.OrganizationId)
                .IsRequired();

            entity.HasOne(r => r.Room)
                .WithMany(room => room.Reservations)
                .HasForeignKey(r => r.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Organization)
                .WithMany()
                .HasForeignKey(r => r.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => new { r.RoomId, r.StartTimeUtc, r.EndTimeUtc })
                .HasDatabaseName("IX_reservations_RoomId_StartTimeUtc_EndTimeUtc");

            entity.HasIndex(r => new { r.OrganizationId, r.RoomId })
                .HasDatabaseName("IX_reservations_OrganizationId_RoomId");

            entity.HasOne(r => r.CreatedByUser)
                .WithMany(u => u.Reservations)
                .HasForeignKey(r => r.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(r => r.IsDeleted)
                .HasDefaultValue(false);

            entity.HasQueryFilter(r =>
                !r.IsDeleted &&
                (_tenant.IsPlatformScope || r.OrganizationId == _tenant.OrganizationId));
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);

            entity.Property(u => u.UserName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(u => u.AvatarUrl)
                .HasMaxLength(500);

            entity.Property(u => u.PasswordHash)
                .IsRequired();

            entity.Property(u => u.Role)
                .IsRequired()
                .HasConversion<int>();

            entity.Property(u => u.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(u => u.UserName)
                .IsUnique();

            entity.HasIndex(u => u.Email)
                .IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(rt => rt.Id);

            entity.Property(rt => rt.TokenHash)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(rt => rt.CreatedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.Property(rt => rt.ExpiresAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.Property(rt => rt.RevokedAtUtc)
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(rt => rt.TokenHash)
                .IsUnique();

            entity.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RevokedToken>(entity =>
        {
            entity.ToTable("revoked_tokens");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.Jti)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(t => t.ExpiresAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.Property(t => t.RevokedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(t => t.Jti)
                .IsUnique();

            entity.HasOne(t => t.User)
                .WithMany(u => u.RevokedTokens)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ApplyTenantInfo()
    {
        var entries = ChangeTracker.Entries<ITenantScoped>()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();

        if (entries.Count == 0)
            return;

        if (_tenant.IsPlatformScope || !_tenant.OrganizationId.HasValue)
            throw new InvalidOperationException("Tenant context is required for tenant-scoped entities.");

        var organizationId = _tenant.OrganizationId.Value;
        foreach (var entry in entries)
        {
            if (entry.Entity.OrganizationId != Guid.Empty &&
                entry.Entity.OrganizationId != organizationId)
            {
                throw new InvalidOperationException("Tenant mismatch for new entity.");
            }

            entry.Entity.OrganizationId = organizationId;
        }
    }

    private sealed class DesignTimeTenant : ICurrentTenant
    {
        public Guid? OrganizationId => null;
        public bool IsPlatformScope => false;
    }
}
