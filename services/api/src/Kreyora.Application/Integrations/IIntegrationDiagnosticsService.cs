using Kreyora.Application.Models;
using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public interface IIntegrationDiagnosticsService
{
    Task<Result<IntegrationOverviewDto>> GetOverviewAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ConnectionDiagnosticsDto>> GetConnectionDiagnosticsAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<WebhookEventDto>>> GetConnectionWebhooksAsync(
        string connectionId,
        int page = 1,
        int pageSize = 20,
        WebhookProcessingStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<Result<WebhookEventDetailDto>> GetWebhookDetailAsync(
        string webhookEventId,
        CancellationToken cancellationToken = default);

    Task<Result<SimulatorScenarioResult>> ExecuteSimulatorScenarioAsync(
        SimulatorScenarioRequest request,
        CancellationToken cancellationToken = default);
}

