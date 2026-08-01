using Kreyora.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.Id).HasMaxLength(ApplicationUser.IdLength);
        builder.Property(user => user.DisplayName)
            .IsRequired()
            .HasMaxLength(ApplicationUser.DisplayNameMaxLength);
        builder.Property(user => user.UserName).IsRequired().HasMaxLength(256);
        builder.Property(user => user.NormalizedUserName).IsRequired().HasMaxLength(256);
        builder.Property(user => user.Email).IsRequired().HasMaxLength(256);
        builder.Property(user => user.NormalizedEmail).IsRequired().HasMaxLength(256);
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
    }
}
