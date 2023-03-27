using System.Text.Json.Serialization;

namespace Cvolcy.DelicateDust.Models.CMC
{
    public class CMCRequestModel<T>
    {
        [JsonPropertyName("status")]
        public CMCRequestStatus Status { get; set; }
        [JsonPropertyName("data")]
        public T Data { get; set; }
    }
}