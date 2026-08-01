using Kreyora.Application.Audit;

namespace Kreyora.UnitTests.Audit;

public class AuditMetadataSanitizerTests
{
    [Fact]
    public void Sanitizer_RedactsSensitiveValuesRecursively()
    {
        var result = AuditMetadataSanitizer.Sanitize("{\"token\":\"unsafe\",\"safe\":true,\"nested\":{\"password\":\"unsafe\"}}");
        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain("unsafe", result);
    }
}
