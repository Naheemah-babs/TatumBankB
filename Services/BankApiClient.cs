
using System.Net;
using System.Net.Http.Json;
using TatumWelcome2.DTOs;
using TatumWelcome2.Interfaces;

namespace TatumWelcome2.Services;

public class BankApiClient : IBankApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public BankApiClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;

        _httpClient.Timeout = TimeSpan.FromSeconds(5);
    }

    private HttpRequestMessage CreateInterbankRequest(
        HttpMethod method,
        string url)
    {
        string apiKey =
            _configuration["Interbank:ApiKey"]
            ?? throw new InvalidOperationException(
                "Interbank API key is missing.");

        var request = new HttpRequestMessage(method, url);

        request.Headers.Add("X-Interbank-Key", apiKey);

        return request;
    }

    public async Task<AccountLookupDto?> FindAccountAsync(
        string bankUrl,
        string accountNumber)
    {
        string url =
            $"{bankUrl.TrimEnd('/')}/accounts/{accountNumber}";

        using HttpResponseMessage response =
            await _httpClient.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<AccountLookupDto>();
    }

    public async Task<HttpResponseMessage> SendTransferAsync(
        string bankUrl,
        InterbankTransferRequestDto request)
    {
        string url =
            $"{bankUrl.TrimEnd('/')}/transfers/receive";

        using HttpRequestMessage httpRequest =
            CreateInterbankRequest(HttpMethod.Post, url);

        httpRequest.Content = JsonContent.Create(request);

        return await _httpClient.SendAsync(httpRequest);
    }

    public async Task<HttpResponseMessage> GetTransferStatusAsync(
        string bankUrl,
        string reference)
    {
        string url =
            $"{bankUrl.TrimEnd('/')}/transfers/status/" +
            Uri.EscapeDataString(reference);

        using HttpRequestMessage httpRequest =
            CreateInterbankRequest(HttpMethod.Get, url);

        return await _httpClient.SendAsync(httpRequest);
    }

    public async Task<HttpResponseMessage> SendBillPaymentAsync(
        string bankUrl,
        BillPaymentRequestDto request)
    {
        string url =
            $"{bankUrl.TrimEnd('/')}/bills/pay";

        using HttpRequestMessage httpRequest =
            CreateInterbankRequest(HttpMethod.Post, url);

        httpRequest.Content = JsonContent.Create(request);

        return await _httpClient.SendAsync(httpRequest);
    }
}
