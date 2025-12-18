using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace eduHub.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Building
        modelBuilder.Entity<Building>(entity =>
        {
            entity.ToTable("buildings");

            entity.HasKey(b => b.Id);

            entity.Property(b => b.Name)
                .IsRequired()
                .HasMaxLength(200);
        });

        // Room
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

            entity.Property(r => r.IsDeleted)
                .HasDefaultValue(false);

            entity.HasOne(r => r.Building)
                .WithMany(b => b.Rooms)
                .HasForeignKey(r => r.BuildingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => new { r.BuildingId, r.Code })
                  .IsUnique()
                  .HasDatabaseName("IX_rooms_BuildingId_Code");

            entity.HasQueryFilter(r => !r.IsDeleted);
        });

        // Reservation
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

            entity.HasOne(r => r.Room)
                .WithMany(room => room.Reservations)
                .HasForeignKey(r => r.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => new { r.RoomId, r.StartTimeUtc, r.EndTimeUtc })
                  .HasDatabaseName("IX_reservations_RoomId_StartTimeUtc_EndTimeUtc");

            entity.HasOne(r => r.CreatedByUser)
                .WithMany(u => u.Reservations)
                .HasForeignKey(r => r.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(r => r.IsDeleted)
                  .HasDefaultValue(false);

            entity.HasQueryFilter(r => !r.IsDeleted);
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
}
