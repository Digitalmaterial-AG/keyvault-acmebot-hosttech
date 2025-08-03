using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text;
using KeyVault.Acmebot.Internal;
using KeyVault.Acmebot.Options;
using Newtonsoft.Json;

namespace KeyVault.Acmebot.Providers;

public class CustomDnsProvider : IDnsProvider
{
    public CustomDnsProvider(CustomDnsOptions options)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.Endpoint)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(options.ApiKeyHeaderName, options.ApiKey);

        PropagationSeconds = options.PropagationSeconds;
    }

    private readonly HttpClient _httpClient;

    public string Name => "Custom DNS";

    public int PropagationSeconds { get; }

    public async Task<IReadOnlyList<DnsZone>> ListZonesAsync()
    {
        var response = await _httpClient.GetAsync("zones");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        var zoneResponse = JsonConvert.DeserializeObject<ZoneResponse>(content);

        if (zoneResponse == null || zoneResponse.Data == null)
            throw new Exception($"Deserialization failed or 'data' is null. Raw content: {content}");

        return zoneResponse.Data.Select(x => new DnsZone(this)
        {
            Id = x.Id.ToString(),
            Name = x.Name,
            NameServers = new[] { x.NameServer }
        }).ToArray();
    }

    public async Task CreateTxtRecordAsync(DnsZone zone, string relativeRecordName, IEnumerable<string> values)
    {
        var payload = new
        {
            type = "TXT",
            name = relativeRecordName,
            text = values.First(),
            ttl = 3600
        };

        var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"zones/{zone.Id}/records", content);
        var respContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to create TXT record: {response.StatusCode} - {respContent}");

        Console.WriteLine($"CreateTxtRecordAsync response: {respContent}");
    }

    public async Task DeleteTxtRecordAsync(DnsZone zone, string relativeRecordName)
    {
        var recordsResponse = await _httpClient.GetAsync($"zones/{zone.Id}/records");
        recordsResponse.EnsureSuccessStatusCode();

        var json = await recordsResponse.Content.ReadAsStringAsync();
        var recordData = JsonConvert.DeserializeObject<RecordResponse>(json);

        foreach (var rec in recordData.Data.Where(r => r.Type == "TXT"))
            Console.WriteLine($"TXT record: name='{rec.Name}', text='{rec.Text}'");

        var txtRecord = recordData.Data
            .FirstOrDefault(r => r.Type == "TXT" && 
                (r.Name.Equals(relativeRecordName, StringComparison.OrdinalIgnoreCase) ||
                 r.Name.EndsWith(relativeRecordName, StringComparison.OrdinalIgnoreCase)));

        if (txtRecord == null)
        {
            Console.WriteLine($"TXT record '{relativeRecordName}' not found in zone '{zone.Name}', nothing to delete.");
            return;
        }

        var deleteResponse = await _httpClient.DeleteAsync($"zones/{zone.Id}/records/{txtRecord.Id}");
        deleteResponse.EnsureSuccessStatusCode();
    }

    private class ZoneResponse
    {
        [JsonProperty("data")]
        public List<Zone> Data { get; set; }
    }

    private class Zone
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("nameserver")]
        public string NameServer { get; set; }
    }

    private class RecordResponse
    {
        [JsonProperty("data")]
        public List<Record> Data { get; set; }
    }

    private class Record
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
