using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace db_transfer;

public partial class IdentifierContext : DbContext
{
    public IdentifierContext()
    {
    }

    public IdentifierContext(DbContextOptions<IdentifierContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Library> Libraries { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlite("DataSource=C:\\\\Users\\\\bleidd\\\\DataGripProjects\\\\db_local\\\\identifier.sqlite");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Library>(entity =>
        {
            entity.ToTable("Library");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AuthorName).HasColumnName("author_name");
            entity.Property(e => e.BookGenre).HasColumnName("book_genre");
            entity.Property(e => e.BookTitle).HasColumnName("book_title");
            entity.Property(e => e.BorrowDate)
                .HasColumnType("DATE")
                .HasColumnName("borrow_date");
            entity.Property(e => e.ReaderName).HasColumnName("reader_name");
            entity.Property(e => e.ReturnDate)
                .HasColumnType("DATE")
                .HasColumnName("return_date");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}


