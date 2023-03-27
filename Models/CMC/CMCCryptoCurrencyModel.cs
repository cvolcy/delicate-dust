using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cvolcy.DelicateDust.Models.CMC
{
    public class CMCCryptoCurrencyModel
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }
        [JsonPropertyName("slug")]
        public string Slug { get; set; }
        [JsonPropertyName("num_market_pairs")]
        public int NumMarketPairs { get; set; }
        [JsonPropertyName("date_added")]
        public DateTime DateAdded { get; set; }
        [JsonPropertyName("tags")]
        public IEnumerable<CMCTag> Tags { get; set; }
        [JsonPropertyName("max_supply")]
        public long MaxSupply { get; set; }
        [JsonPropertyName("circulating_supply")]
        public double CirculatingSupply { get; set; }
        [JsonPropertyName("total_supply")]
        public double? TotalSupply { get; set; }
        [JsonPropertyName("is_active")]
        public byte IsActive { get; set; }
        [JsonPropertyName("platform")]
        public string Platform { get; set; }
        [JsonPropertyName("cmc_rank")]
        public int CmcRank { get; set; }
        [JsonPropertyName("is_fiat")]
        public byte IsFiat { get; set; }
        [JsonPropertyName("self_reported_circulating_supply")]
        public string SelfReportedCirculatingSupply { get; set; }
        [JsonPropertyName("self_reported_market_cap")]
        public string SelfReportedMarketCap { get; set; }
        [JsonPropertyName("tvl_ratio")]
        public string TvlRatio { get; set; }
        [JsonPropertyName("last_updated")]
        public DateTime LastUpdated { get; set; }
        [JsonPropertyName("quote")]
        public IDictionary<string, CMCQuote> Quote { get; set; }
    }
}