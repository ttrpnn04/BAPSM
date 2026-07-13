using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Models;

public partial class BAPStockContext : IdentityDbContext<IdentityUser>
{
    public BAPStockContext()
    {
    }

    public BAPStockContext(DbContextOptions<BAPStockContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductVariant> ProductVariants { get; set; }

    public virtual DbSet<StockBalance> StockBalances { get; set; }

    public virtual DbSet<StockDocument> StockDocuments { get; set; }

    public virtual DbSet<StockTransaction> StockTransactions { get; set; }

    public virtual DbSet<TransactionType> TransactionTypes { get; set; }

    public virtual DbSet<VwCurrentStock> VwCurrentStocks { get; set; }

    public virtual DbSet<VwMonthlySummary> VwMonthlySummaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            base.OnModelCreating(modelBuilder);
            
            entity.HasIndex(e => e.CategoryName, "UQ_Categories_Name").IsUnique();

            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CategoryName).HasMaxLength(150);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())", "DF_Categories_CreatedAt");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Categories_IsActive");
            entity.Property(e => e.VariantLabel)
                .HasMaxLength(50)
                .HasDefaultValue("สี", "DF_Categories_VariantLabel");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasIndex(e => e.CategoryId, "IX_Products_CategoryID");

            entity.HasIndex(e => e.Sku, "IX_Products_SKU");

            entity.HasIndex(e => new { e.Sku, e.ProductName }, "UQ_Products_SKU_Name").IsUnique();

            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())", "DF_Products_CreatedAt");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Products_IsActive");
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.ProductName).HasMaxLength(300);
            entity.Property(e => e.Sku)
                .HasMaxLength(50)
                .HasColumnName("SKU");
            entity.Property(e => e.Unit)
                .HasMaxLength(20)
                .HasDefaultValue("คัน", "DF_Products_Unit");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())", "DF_Products_UpdatedAt");

            entity.HasOne(d => d.Category).WithMany(p => p.Products)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Products_Categories");
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.HasKey(e => e.VariantId);

            entity.HasIndex(e => e.ProductId, "IX_ProductVariants_ProductID");

            entity.HasIndex(e => new { e.ProductId, e.VariantName }, "UQ_ProductVariants_Product_Name").IsUnique();

            entity.Property(e => e.VariantId).HasColumnName("VariantID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())", "DF_ProductVariants_CreatedAt");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_ProductVariants_IsActive");
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.VariantName).HasMaxLength(100);

            entity.HasOne(d => d.Product).WithMany(p => p.ProductVariants)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProductVariants_Products");
        });

        modelBuilder.Entity<StockBalance>(entity =>
        {
            entity.HasKey(e => e.VariantId);

            entity.Property(e => e.VariantId)
                .ValueGeneratedNever()
                .HasColumnName("VariantID");
            entity.Property(e => e.LastUpdated).HasDefaultValueSql("(sysdatetime())", "DF_StockBalances_LastUpdated");

            entity.HasOne(d => d.Variant).WithOne(p => p.StockBalance)
                .HasForeignKey<StockBalance>(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockBalances_Variants");
        });

        modelBuilder.Entity<StockDocument>(entity =>
        {
            entity.HasKey(e => e.DocumentId);

            entity.ToTable("StockDocuments");

            entity.HasIndex(e => e.TxnDate, "IX_StockDocuments_Date");
            entity.HasIndex(e => e.RefNo, "IX_StockDocuments_RefNo");

            entity.Property(e => e.DocumentId).HasColumnName("DocumentID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())", "DF_StockDocuments_CreatedAt");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.Note).HasMaxLength(300);
            entity.Property(e => e.RefNo).HasMaxLength(50);
            entity.Property(e => e.TransactionTypeId).HasColumnName("TransactionTypeID");
            entity.Property(e => e.TxnDate).HasDefaultValueSql("(CONVERT([date],sysdatetime()))", "DF_StockDocuments_TxnDate");

            entity.HasOne(d => d.TransactionType).WithMany(p => p.StockDocuments)
                .HasForeignKey(d => d.TransactionTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockDocuments_Types");
        });

        modelBuilder.Entity<StockTransaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId);

            entity.ToTable(tb => tb.HasTrigger("trg_StockTransactions_UpdateBalance"));

            entity.HasIndex(e => e.TxnDate, "IX_StockTransactions_Date");

            entity.HasIndex(e => new { e.VariantId, e.TxnDate }, "IX_StockTransactions_Variant_Date");

            entity.HasIndex(e => e.DocumentId, "IX_StockTransactions_DocumentID");

            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
            entity.Property(e => e.DocumentId).HasColumnName("DocumentID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())", "DF_StockTransactions_CreatedAt");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.Note).HasMaxLength(300);
            entity.Property(e => e.RefNo).HasMaxLength(50);
            entity.Property(e => e.TransactionTypeId).HasColumnName("TransactionTypeID");
            entity.Property(e => e.TxnDate).HasDefaultValueSql("(CONVERT([date],sysdatetime()))", "DF_StockTransactions_TxnDate");
            entity.Property(e => e.VariantId).HasColumnName("VariantID");

            entity.HasOne(d => d.Document).WithMany(p => p.StockTransactions)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockTransactions_Documents");

            entity.HasOne(d => d.TransactionType).WithMany(p => p.StockTransactions)
                .HasForeignKey(d => d.TransactionTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockTransactions_Types");

            entity.HasOne(d => d.Variant).WithMany(p => p.StockTransactions)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockTransactions_Variants");
        });

        modelBuilder.Entity<TransactionType>(entity =>
        {
            entity.HasIndex(e => e.TypeName, "UQ_TransactionTypes_Name").IsUnique();

            entity.Property(e => e.TransactionTypeId).HasColumnName("TransactionTypeID");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_TransactionTypes_IsActive");
            entity.Property(e => e.TypeName).HasMaxLength(50);
        });

        modelBuilder.Entity<VwCurrentStock>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_CurrentStock");

            entity.Property(e => e.CategoryName).HasMaxLength(150);
            entity.Property(e => e.ProductName).HasMaxLength(300);
            entity.Property(e => e.Sku)
                .HasMaxLength(50)
                .HasColumnName("SKU");
            entity.Property(e => e.Unit).HasMaxLength(20);
            entity.Property(e => e.VariantId).HasColumnName("VariantID");
            entity.Property(e => e.VariantName).HasMaxLength(100);
        });

        modelBuilder.Entity<VwMonthlySummary>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_MonthlySummary");

            entity.Property(e => e.CategoryName).HasMaxLength(150);
            entity.Property(e => e.ProductName).HasMaxLength(300);
            entity.Property(e => e.Sku)
                .HasMaxLength(50)
                .HasColumnName("SKU");
            entity.Property(e => e.VariantName).HasMaxLength(100);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
