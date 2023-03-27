using System;
using System.Text.Json.Serialization;

namespace Cvolcy.DelicateDust.Models.CMC
{
    public class CMCQuote
    {
        [JsonPropertyName("price")]
        public double Price { get; set; }
        [JsonPropertyName("volume_24h")]
        public double Volume24h { get; set; }
        [JsonPropertyName("volume_change_24h")]
        public double VolumeChange24h { get; set; }
        [JsonPropertyName("percent_change_1h")]
        public double PercentChange1h { get; set; }
        [JsonPropertyName("percent_change_24h")]
        public double PercentChange24h { get; set; }
        [JsonPropertyName("percent_change_7d")]
        public double PercentChange7d { get; set; }
        [JsonPropertyName("percent_change_30d")]
        public double PercentChange30d { get; set; }
        [JsonPropertyName("percent_change_60d")]
        public double PercentChange60d { get; set; }
        [JsonPropertyName("percent_change_90d")]
        public double PercentChange90d { get; set; }
        [JsonPropertyName("market_cap")]
        public double MarketCap { get; set; }
        [JsonPropertyName("market_cap_dominance")]
        public double MarketCapDominance { get; set; }
        [JsonPropertyName("fully_diluted_market_cap")]
        public double FullyDilutedMarketCap { get; set; }
        // [JsonPropertyName("tvl")]
        // public string Tvl { get; set; }
        [JsonPropertyName("last_updated")]
        public DateTimeOffset LastUpdated { get; set; }
    }
}