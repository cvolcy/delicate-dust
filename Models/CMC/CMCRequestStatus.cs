using System;
using System.Text.Json.Serialization;

namespace Cvolcy.DelicateDust.Models.CMC
{
    public class CMCRequestStatus
    {
        public DateTimeOffset Timestamp { get; set; }
        [JsonPropertyName("error_code")]
        public int ErrorCode { get; set; }
        [JsonPropertyName("error_message")]
        public string ErrorMessage { get; set; }
        public int Elapsed { get; set; }
        [JsonPropertyName("credit_count")]
        public int CreditCount { get; set; }
        public string Notice { get; set; }
    }
}