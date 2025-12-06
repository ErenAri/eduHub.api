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

            entity.HasOne(r => r.Building)
                .WithMany(b => b.Rooms)
                .HasForeignKey(r => r.BuildingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => new { r.BuildingId, r.Code })
                  .IsUnique()
                  .HasDatabaseName("IX_rooms_BuildingId_Code");
        });

        // Reservation
        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.ToTable("reservations");

            entity.HasKey(r => r.Id);

            entity.Property(r => r.StartTimeUtc).IsRequired();
            entity.Property(r => r.EndTimeUtc).IsRequired();
            entity.Property(r => r.CreatedAtUtc).IsRequired();

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
        });
        base.OnModelCreating(modelBuilder);

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
        });
    }
}