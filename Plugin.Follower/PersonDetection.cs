using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Plugin.Follower
{
    public class PersonDetection
    {
        public const string TOPIC_NAME = "detection_person";

        [JsonPropertyName("xmin")]
        public float XMin { get; set; }

        [JsonPropertyName("xmax")]
        public float XMax { get; set; }

        [JsonPropertyName("ymin")]
        public float YMin { get; set; }

        [JsonPropertyName("ymax")]
        public float YMax { get; set; }

        [JsonPropertyName("w")]
        public int FrameWidth { get; set; }

        [JsonPropertyName("h")]
        public int FrameHeight { get; set; }
    }
}
