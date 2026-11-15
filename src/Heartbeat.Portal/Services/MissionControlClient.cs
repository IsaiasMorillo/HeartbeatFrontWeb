using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Heartbeat.Shared;

namespace Heartbeat.Portal.Services;

public sealed class MissionControlClient(
    HttpClient httpClient,
    MissionControlTokenRefresher tokenRefresher)
{
    private static readonly JsonSerializerOptions MissionControlJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var accessToken = await tokenRefresher.GetAccessTokenAsync(cancellationToken);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await tokenRefresher.ClearSessionAsync(cancellationToken);
        }

        return response;
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        tokenRefresher.GetAccessTokenAsync(cancellationToken);

    public Task<HttpResponseMessage> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default) =>
        httpClient.PostAsJsonAsync("auth/login", request, cancellationToken);

    public async Task<HttpResponseMessage> GetMeAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "me");
        return await SendAsync(request, cancellationToken);
    }

    public Task<CrewSummaryDto[]> GetCrewAsync(CancellationToken cancellationToken = default) =>
        GetAsync<CrewSummaryDto[]>("crew", cancellationToken);

    public Task<MissionProfileResponse> GetMissionAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MissionProfileResponse>("mission", cancellationToken);

    public Task<DomainProfileResponse[]> GetDomainProfilesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<DomainProfileResponse[]>("profiles", cancellationToken);

    public Task<RiskStatusResponse> GetCrewRiskAsync(
        Guid astronautId,
        string? profileId = null,
        CancellationToken cancellationToken = default) =>
        GetAsync<RiskStatusResponse>(
            string.IsNullOrWhiteSpace(profileId)
                ? $"crew/{astronautId:D}/risk"
                : $"crew/{astronautId:D}/risk?profile={Uri.EscapeDataString(profileId)}",
            cancellationToken);

    public async Task<AssistantResponse> AskAssistantAsync(
        Guid astronautId,
        string question,
        string language,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"crew/{astronautId:D}/assistant/chat")
        {
            Content = JsonContent.Create(
                new AssistantRequest(question, language),
                options: MissionControlJsonOptions)
        };
        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<AssistantResponse>(
            MissionControlJsonOptions,
            cancellationToken) ?? throw new InvalidDataException(
                "The Mission Control API returned an empty assistant response.");
    }

    public Task<PhantomComparisonResponse> GetPhantomComparisonAsync(
        IReadOnlyCollection<Guid>? profileIds = null,
        string environment = "station",
        int weeks = 26,
        decimal exerciseHours = 7,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (profileIds is { Count: > 0 })
        {
            query.Add($"profiles={string.Join(",", profileIds.Select(id => Uri.EscapeDataString(id.ToString("D"))))}");
        }

        query.Add($"environment={Uri.EscapeDataString(environment)}");
        query.Add($"weeks={weeks.ToString(CultureInfo.InvariantCulture)}");
        query.Add($"exerciseHours={exerciseHours.ToString(CultureInfo.InvariantCulture)}");
        return GetAsync<PhantomComparisonResponse>(
            $"phantoms/compare?{string.Join("&", query)}",
            cancellationToken);
    }

    public Task<AstronautProfile> GetCrewMemberAsync(
        Guid astronautId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AstronautProfile>($"crew/{astronautId:D}", cancellationToken);

    public Task<AstronautStatusDto> GetAstronautStatusAsync(
        Guid astronautId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AstronautStatusDto>($"crew/{astronautId:D}/status", cancellationToken);

    public Task<Paged<CheckUpDto>> GetCheckUpsAsync(
        Guid astronautId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        HealthStatus? status = null,
        int page = 1,
        int pageSize = 20,
        ReadingSource? source = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"page={page.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}"
        };

        if (from is not null)
        {
            query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (to is not null)
        {
            query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (status is not null)
        {
            var statusName = JsonNamingPolicy.CamelCase.ConvertName(status.Value.ToString());
            query.Add($"status={Uri.EscapeDataString(statusName)}");
        }

        if (source is not null)
        {
            query.Add($"source={SourceName(source.Value)}");
        }

        var uri = $"crew/{astronautId:D}/checkups?{string.Join("&", query)}";
        return GetAsync<Paged<CheckUpDto>>(uri, cancellationToken);
    }

    public Task<CheckUpDetailDto> GetCheckUpDetailAsync(
        Guid astronautId,
        Guid checkUpId,
        CancellationToken cancellationToken = default) =>
        GetAsync<CheckUpDetailDto>(
            $"crew/{astronautId:D}/checkups/{checkUpId:D}",
            cancellationToken);

    public Task<SeriesDto> GetSeriesAsync(
        Guid astronautId,
        IndicatorType indicator,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        ReadingSource? source = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"indicator={Uri.EscapeDataString(JsonNamingPolicy.CamelCase.ConvertName(indicator.ToString()))}"
        };

        if (from is not null)
        {
            query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (to is not null)
        {
            query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (source is not null)
        {
            query.Add($"source={SourceName(source.Value)}");
        }

        return GetAsync<SeriesDto>(
            $"crew/{astronautId:D}/series?{string.Join("&", query)}",
            cancellationToken);
    }

    private static string SourceName(ReadingSource source) =>
        Uri.EscapeDataString(JsonNamingPolicy.CamelCase.ConvertName(source.ToString()));

    public Task<JsonDocument> GetReferenceRangesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<JsonDocument>("reference-ranges", cancellationToken);

    public Task<Paged<MessageDto>> GetCrewMessagesAsync(
        Guid astronautId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var uri = $"crew/{astronautId:D}/messages" +
                  $"?page={page.ToString(CultureInfo.InvariantCulture)}" +
                  $"&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}";
        return GetAsync<Paged<MessageDto>>(uri, cancellationToken);
    }

    public async Task<MessageDto> SendCrewMessageAsync(
        Guid astronautId,
        SendMessageRequest message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"crew/{astronautId:D}/messages")
        {
            Content = JsonContent.Create(message, options: MissionControlJsonOptions)
        };
        using var response = await SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MessageDto>(
            MissionControlJsonOptions,
            cancellationToken) ?? throw new InvalidDataException(
                "The Mission Control API returned an empty response.");
    }

    public async Task MarkMessageReadAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"messages/{messageId:D}/read");
        using var response = await SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<Paged<AlertDto>> GetAlertsAsync(
        string state = "open",
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var uri = $"alerts?state={Uri.EscapeDataString(state)}" +
                  $"&page={page.ToString(CultureInfo.InvariantCulture)}" +
                  $"&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}";
        return GetAsync<Paged<AlertDto>>(uri, cancellationToken);
    }

    public async Task<AlertDto> AcknowledgeAlertAsync(
        Guid alertId,
        string note,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"alerts/{alertId:D}/acknowledge")
        {
            Content = JsonContent.Create(
                new AcknowledgeAlertRequest(note),
                options: MissionControlJsonOptions)
        };
        using var response = await SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AlertDto>(
            MissionControlJsonOptions,
            cancellationToken) ?? throw new InvalidDataException(
                "The Mission Control API returned an empty response.");
    }

    public Task<Paged<PortalUserDto>> GetUsersAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var uri = $"users?page={page.ToString(CultureInfo.InvariantCulture)}" +
                  $"&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}";
        return GetAsync<Paged<PortalUserDto>>(uri, cancellationToken);
    }

    public async Task<PortalUserDto> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var requestMsg = new HttpRequestMessage(
            HttpMethod.Post,
            "users")
        {
            Content = JsonContent.Create(request, options: MissionControlJsonOptions)
        };
        using var response = await SendAsync(requestMsg, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<PortalUserDto>(
            MissionControlJsonOptions,
            cancellationToken) ?? throw new InvalidDataException(
                "The Mission Control API returned an empty response.");
    }

    public async Task<PortalUserDto> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var requestMsg = new HttpRequestMessage(
            HttpMethod.Put,
            $"users/{id:D}")
        {
            Content = JsonContent.Create(request, options: MissionControlJsonOptions)
        };
        using var response = await SendAsync(requestMsg, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<PortalUserDto>(
            MissionControlJsonOptions,
            cancellationToken) ?? throw new InvalidDataException(
                "The Mission Control API returned an empty response.");
    }

    public async Task<PortalUserDto> SetUserRoleAsync(
        Guid id,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        using var requestMsg = new HttpRequestMessage(
            HttpMethod.Put,
            $"users/{id:D}/role")
        {
            Content = JsonContent.Create(
                new SetUserRoleRequest(roleId),
                options: MissionControlJsonOptions)
        };
        using var response = await SendAsync(requestMsg, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<PortalUserDto>(
            MissionControlJsonOptions,
            cancellationToken) ?? throw new InvalidDataException(
                "The Mission Control API returned an empty response.");
    }

    public async Task DisableUserAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var requestMsg = new HttpRequestMessage(
            HttpMethod.Post,
            $"users/{id:D}/disable");
        using var response = await SendAsync(requestMsg, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task EnableUserAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var requestMsg = new HttpRequestMessage(
            HttpMethod.Post,
            $"users/{id:D}/enable");
        using var response = await SendAsync(requestMsg, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<List<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<RoleDto>>("roles", cancellationToken);

    public Task<List<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<PermissionDto>>("permissions", cancellationToken);

    public async Task<RoleDto> CreateRoleAsync(
        string name,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var request = new SaveRoleRequest(name, permissions);
        using var requestMsg = new HttpRequestMessage(
            HttpMethod.Post,
            "roles")
        {
            Content = JsonContent.Create(request, options: MissionControlJsonOptions)
        };
        using var response = await SendAsync(requestMsg, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RoleDto>(
            MissionControlJsonOptions,
            cancellationToken) ?? throw new InvalidDataException(
                "The Mission Control API returned an empty response.");
    }

    public async Task<RoleDto> UpdateRoleAsync(
        Guid id,
        string name,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var request = new SaveRoleRequest(name, permissions);
        using var requestMsg = new HttpRequestMessage(
            HttpMethod.Put,
            $"roles/{id:D}")
        {
            Content = JsonContent.Create(request, options: MissionControlJsonOptions)
        };
        using var response = await SendAsync(requestMsg, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RoleDto>(
            MissionControlJsonOptions,
            cancellationToken) ?? throw new InvalidDataException(
                "The Mission Control API returned an empty response.");
    }

    public async Task DeleteRoleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var requestMsg = new HttpRequestMessage(
            HttpMethod.Delete,
            $"roles/{id:D}");
        using var response = await SendAsync(requestMsg, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<CsvDownload> ExportAstronautCsvAsync(
        Guid astronautId,
        CancellationToken cancellationToken = default) =>
        DownloadCsvAsync($"crew/{astronautId:D}/export.csv", $"heartbeat-{astronautId:D}.csv", cancellationToken);

    public Task<CsvDownload> ExportMissionCsvAsync(
        Guid? missionId = null,
        CancellationToken cancellationToken = default) =>
        DownloadCsvAsync(
            missionId is { } id ? $"mission/export.csv?missionId={id:D}" : "mission/export.csv",
            "heartbeat-mission.csv",
            cancellationToken);

    private async Task<CsvDownload> DownloadCsvAsync(
        string uri,
        string fallbackFileName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
        return new CsvDownload(
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            string.IsNullOrWhiteSpace(fileName) ? fallbackFileName : fileName);
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(
            MissionControlJsonOptions,
            cancellationToken) ?? throw new InvalidDataException(
                "The Mission Control API returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            ReadApiError(body, response.StatusCode),
            inner: null,
            statusCode: response.StatusCode);
    }

    private static string ReadApiError(
        string body,
        System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    messages.AddRange(property.Value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!));
                }
            }

            if (messages.Count == 0 &&
                root.TryGetProperty("detail", out var detail) &&
                detail.ValueKind == JsonValueKind.String)
            {
                messages.Add(detail.GetString()!);
            }

            if (messages.Count == 0 &&
                root.TryGetProperty("title", out var title) &&
                title.ValueKind == JsonValueKind.String)
            {
                messages.Add(title.GetString()!);
            }

            if (messages.Count > 0)
            {
                return string.Join(" ", messages);
            }
        }
        catch (JsonException)
        {
            // Fall back to the generic message below.
        }

        return $"La API respondió {(int)statusCode}.";
    }
}
