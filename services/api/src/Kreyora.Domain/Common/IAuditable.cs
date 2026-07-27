namespace Kreyora.Domain.Common;

public interface IAuditable
{
    string? CreatedBy { get; set; }
    string? ModifiedBy { get; set; }
}
