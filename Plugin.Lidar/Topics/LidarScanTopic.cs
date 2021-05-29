using Overkill.PubSub.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Plugin.Lidar.Topics
{
    public class LidarScanTopic : IPubSubTopic
    {
        public List<(float Angle, float Distance)> Measurements { get; set; }
    }
}
