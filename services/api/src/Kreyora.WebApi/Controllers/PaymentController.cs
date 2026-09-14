using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Payments;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, RequireTenantContext, ApiVersion("1.0")]
public sealed class PaymentController(IPaymentService paymentService) : ControllerBase
{
    [HttpGet("v{version:apiVersion}/orders/{orderId}/payments"), Authorize(Policy = TenantPermissions.PaymentsRead)]
    public async Task<ActionResult<IReadOnlyList<PaymentAttemptItem>>> GetPaymentAttempts(string orderId, CancellationToken cancellationToken) =>
        this.ToActionResult(await paymentService.GetPaymentAttemptsAsync(orderId, cancellationToken));

    [HttpPost("v{version:apiVersion}/orders/{orderId}/payments/{attemptId}/proof/initiate"), Authorize(Policy = TenantPermissions.OrdersWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<PaymentProofItem>> InitiateProofUpload(
        string orderId,
        string attemptId,
        InitiateProofUploadBody body,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await paymentService.InitiateProofUploadAsync(
            new InitiatePaymentProofUploadRequest(orderId, attemptId, body.ContentType, body.ByteSize, body.CustomerNote),
            cancellationToken));

    [HttpPost("v{version:apiVersion}/payments/proofs/{proofId}/complete"), Authorize(Policy = TenantPermissions.OrdersWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<PaymentProofItem>> CompleteProofUpload(string proofId, IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using var stream = file.OpenReadStream();
        return this.ToActionResult(await paymentService.CompleteProofUploadAsync(proofId, stream, cancellationToken));
    }

    [HttpGet("v{version:apiVersion}/payments/proofs/{proofId}/content"), Authorize(Policy = TenantPermissions.PaymentsRead)]
    public async Task<IActionResult> GetProofContent(string proofId, CancellationToken cancellationToken)
    {
        var result = await paymentService.GetProofContentAsync(proofId, cancellationToken);
        if (result.IsSuccess)
        {
            return File(result.Value!.Content, result.Value.ContentType, enableRangeProcessing: false);
        }

        var error = result.Error!;
        return StatusCode(error.Status, new ProblemDetails
        {
            Type = error.Type,
            Title = error.Title,
            Status = error.Status,
            Detail = error.Detail
        });
    }
}

public sealed record InitiateProofUploadBody(string ContentType, long ByteSize, string? CustomerNote = null);

