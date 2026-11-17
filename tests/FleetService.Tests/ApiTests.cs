using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FleetService.Api.Data;
using FleetService.Api.Models;
using FleetService.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FleetService.Tests;

public class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

public class TestApp : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    public ManualClock Clock { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        _conn.Open();
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<DbContextOptions<FleetDbContext>>();
            s.AddDbContext<FleetDbContext>(o => o.UseSqlite(_conn));
            s.RemoveAll<IHostedService>();          // фоновую задачу в тестах вызываем вручную
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
        });
    }

    protected override void Dispose(bool disposing) { base.Dispose(disposing); _conn.Dispose(); }
}

static class Ext
{
    public static void RemoveAll<T>(this IServiceCollection s)
    {
        foreach (var d in s.Where(d => d.ServiceType == typeof(T)).ToList()) s.Remove(d);
    }
}

public class ApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private readonly TestApp _app = new();
    private readonly HttpClient _http;

    public ApiTests() => _http = _app.CreateClient();
    public void Dispose() { _http.Dispose(); _app.Dispose(); }

    private async Task<VehicleDto> AddVehicle(string plate = "1234 AB-7")
    {
        var res = await _http.PostAsJsonAsync("/api/vehicles", new VehicleCreateDto(plate, "MAN TGS", 120000), Json);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<VehicleDto>(Json))!;
    }

    private async Task<RequestDto> AddRequest(int vehicleId, RequestPriority p, DateTime? due = null)
    {
        var res = await _http.PostAsJsonAsync("/api/requests", new RequestCreateDto(vehicleId, "Замена тормозных колодок", p, due), Json);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RequestDto>(Json))!;
    }

    [Fact]
    public async Task Create_vehicle_normalizes_plate_and_rejects_duplicate()
    {
        var v = await AddVehicle("1234 ab-7");
        Assert.Equal("1234 AB-7", v.PlateNumber);
        var dup = await _http.PostAsJsonAsync("/api/vehicles", new VehicleCreateDto("1234 AB-7", "Volvo", 1), Json);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Create_vehicle_validates_input()
    {
        var res = await _http.PostAsJsonAsync("/api/vehicles", new VehicleCreateDto("", "", -5), Json);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Request_gets_default_due_date_by_priority()
    {
        var v = await AddVehicle();
        var r = await AddRequest(v.Id, RequestPriority.High);
        Assert.Equal(_app.Clock.Now.UtcDateTime.AddHours(4), r.DueAtUtc);
        Assert.Equal(RequestStatus.New, r.Status);
    }

    [Fact]
    public async Task Request_for_unknown_vehicle_returns_404()
    {
        var res = await _http.PostAsJsonAsync("/api/requests", new RequestCreateDto(999, "x", RequestPriority.Low, null), Json);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Status_flow_allows_only_valid_transitions()
    {
        var v = await AddVehicle();
        var r = await AddRequest(v.Id, RequestPriority.Normal);
        var bad = await _http.PutAsJsonAsync($"/api/requests/{r.Id}/status", new RequestStatusDto(RequestStatus.Done), Json);
        Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);
        var ok1 = await _http.PutAsJsonAsync($"/api/requests/{r.Id}/status", new RequestStatusDto(RequestStatus.InProgress), Json);
        Assert.Equal(HttpStatusCode.OK, ok1.StatusCode);
        var ok2 = await _http.PutAsJsonAsync($"/api/requests/{r.Id}/status", new RequestStatusDto(RequestStatus.Done), Json);
        Assert.Equal(HttpStatusCode.OK, ok2.StatusCode);
    }

    [Fact]
    public async Task Background_check_marks_only_open_overdue_requests()
    {
        var v = await AddVehicle();
        var high = await AddRequest(v.Id, RequestPriority.High);      // срок +4 ч
        var low = await AddRequest(v.Id, RequestPriority.Low);        // срок +3 дня
        var done = await AddRequest(v.Id, RequestPriority.High);
        await _http.PutAsJsonAsync($"/api/requests/{done.Id}/status", new RequestStatusDto(RequestStatus.Cancelled), Json);

        var scopes = _app.Services.GetRequiredService<IServiceScopeFactory>();
        var marked = await OverdueMonitor.RunOnceAsync(scopes, _app.Clock.Now.UtcDateTime.AddHours(5));
        Assert.Equal(1, marked);

        var overdue = await _http.GetFromJsonAsync<List<RequestDto>>("/api/requests?overdue=true", Json);
        Assert.Single(overdue!);
        Assert.Equal(high.Id, overdue![0].Id);
        Assert.NotEqual(low.Id, overdue[0].Id);
    }

    [Fact]
    public async Task List_is_sorted_by_priority_then_due_date()
    {
        var v = await AddVehicle();
        await AddRequest(v.Id, RequestPriority.Low);
        await AddRequest(v.Id, RequestPriority.High);
        await AddRequest(v.Id, RequestPriority.Normal);
        var list = await _http.GetFromJsonAsync<List<RequestDto>>("/api/requests", Json);
        Assert.Equal(new[] { RequestPriority.High, RequestPriority.Normal, RequestPriority.Low }, list!.Select(r => r.Priority));
    }

    [Fact]
    public async Task Deleting_vehicle_removes_its_requests()
    {
        var v = await AddVehicle();
        await AddRequest(v.Id, RequestPriority.Low);
        var del = await _http.DeleteAsync($"/api/vehicles/{v.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var list = await _http.GetFromJsonAsync<List<RequestDto>>("/api/requests", Json);
        Assert.Empty(list!);
    }
}
