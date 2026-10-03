using System;
using HMoeData.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HMoeViewer.Persistence;

// Viewer queries open the crawler database read-only without schema creation or migration.
internal sealed class ViewerDbContext(string databasePath) : DbContext
{
    private static readonly ValueConverter<Uri, string> _UriConverter = new(
        uri => uri.ToString(),
        value => new Uri(value, UriKind.RelativeOrAbsolute));

    public DbSet<Post> Posts => Set<Post>();

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Thumbnail> Thumbnails => Set<Thumbnail>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Medal> Medals => Set<Medal>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlite(
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<Post>(entity =>
        {
            _ = entity.Property(post => post.Url).HasConversion(_UriConverter);
            _ = entity.HasOne(post => post.DbAuthor).WithMany().HasForeignKey(post => post.DbAuthorId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity.HasOne(post => post.DbThumbnail).WithOne(thumbnail => thumbnail.DbPost)
                .HasForeignKey<Thumbnail>(thumbnail => thumbnail.DbPostId).OnDelete(DeleteBehavior.Cascade);
            _ = entity.HasMany(post => post.DbTags).WithMany();
            _ = entity.HasMany(post => post.DbCats).WithMany();
        });
        _ = modelBuilder.Entity<Author>(entity =>
        {
            _ = entity.Property(author => author.Url).HasConversion(_UriConverter);
            _ = entity.Property(author => author.AvatarUrl).HasConversion(_UriConverter);
            _ = entity.HasOne(author => author.DbRole).WithMany().HasForeignKey(author => author.DbRoleName)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity.HasMany(author => author.DbMedals).WithMany();
        });
        _ = modelBuilder.Entity<Thumbnail>().Property(thumbnail => thumbnail.Url).HasConversion(_UriConverter);
        _ = modelBuilder.Entity<Tag>().Property(tag => tag.Url).HasConversion(_UriConverter);
        _ = modelBuilder.Entity<Category>().Property(category => category.Url).HasConversion(_UriConverter);
        _ = modelBuilder.Entity<Medal>().Property(medal => medal.ImgUrl).HasConversion(_UriConverter);
    }
}
