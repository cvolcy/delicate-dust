using System.Text.Json.Serialization;

namespace Cvolcy.DelicateDust.Models.CMC
{
    public class CMCTag
    {
        [JsonPropertyName("slug")]
        public string Slug { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("category")]
        public string Category { get; set; }
    }
}