using Overkill.Core.Interfaces;
using Overkill.Core.Topics;
using Overkill.Core.Topics.Control;
using Overkill.PubSub.Interfaces;
using Plugin.Lidar.Topics;
using System;
using System.Linq;

namespace Plugin.CollisionAvoidance
{
    public class CollisionAvoidancePlugin : IPlugin
    {
        private IPubSubService _pubSub;

        public CollisionAvoidancePlugin(IPubSubService pubSub)
        {
            _pubSub = pubSub;
        }

        public void Initialize()
        {
            _pubSub.Subscribe<LidarScanTopic>(ProcessMeasurements);
        }

        void ProcessMeasurements(LidarScanTopic scan)
        {
            var safeSpaceInches = 50;
            var closestPoints = scan.Measurements
                .Select(x => ((float Angle, float Distance))(x.Angle, ToInches(x.Distance)))
                .OrderBy(x => x.Distance)
                .Where(x => x.Distance < safeSpaceInches)
                .Take(4)
                .ToArray();

            if(closestPoints.Any())
            {
                _pubSub.Dispatch(new LocomotionTopic()
                {
                    Direction = (closestPoints[0].Angle + 180) % 360,
                    Speed = 18
                });
            } else
            {
                _pubSub.Dispatch(new LocomotionTopic()
                {
                    Direction = 50,
                    Speed = 0
                });
            }

        }

        float ToInches(float mm)
        {
            return mm * 0.0393701f;
        }
    }
}
