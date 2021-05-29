using Overkill.PubSub.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Plugin.Lidar.Topics
{
    public class LidarCoordinateMapTopic : IPubSubTopic
    {
        public Vector2[] Points { get; set; }
    }
}
