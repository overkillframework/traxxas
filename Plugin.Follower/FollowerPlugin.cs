using Microsoft.Extensions.Logging;
using Overkill.Core.Interfaces;
using Overkill.Core.Topics;
using Overkill.Core.Topics.Control;
using Overkill.Proxies.Interfaces;
using Overkill.PubSub.Interfaces;
using System;
using System.Text.Json;
using System.Threading;

namespace Plugin.Follower
{
    public class FollowerPlugin : IPlugin
    {
        private readonly ILogger<FollowerPlugin> _logger;
        private readonly IThreadProxy _threadCreator;
        private readonly IThreadProxy _workThread;
        private readonly IPubSubService _pubSub;

        private DateTime lastBearingUpdate = DateTime.MinValue;
        private DateTime noSubjectTime = DateTime.MinValue;
        private bool subjectInFrame = false;
        private float lastDetectionMidPoint = 0;
        private float bearing = 0;

        public FollowerPlugin(
            ILogger<FollowerPlugin> logger,
            IThreadProxy threadCreator,
            IPubSubService pubSub
        )
        {
            _logger = logger;
            _pubSub = pubSub;
            _threadCreator = threadCreator;

            _workThread = _threadCreator.Create("Follower Work Thread", Process);
        }

        public void Initialize()
        {
            _pubSub.Subscribe<PluginMessageTopic>(msg =>
            {
                _logger.LogInformation(msg.MessageType);
                switch (msg.MessageType)
                {
                    case PersonDetection.TOPIC_NAME:
                        if(!subjectInFrame)
                        {
                            _pubSub.Dispatch(new LocomotionTopic
                            {
                                Speed = 0,
                                Direction = 0
                            });
                        }

                        subjectInFrame = true;
                        var personDetection = JsonSerializer.Deserialize<PersonDetection>(msg.JSON);
                        HandlePersonDetection(personDetection);
                        break;
                    case "no_detections":
                        if (subjectInFrame)
                        {
                            subjectInFrame = false;
                            noSubjectTime = DateTime.Now;
                            
                            _pubSub.Dispatch(new LocomotionTopic
                            {
                                Speed = 0,
                                Direction = 0
                            });
                        }
                        break;
                }
            });

            _workThread.Start();
        }

        private void HandlePersonDetection(PersonDetection detection)
        {
            var personWidth = detection.XMax - detection.XMin;
            var personHeight = detection.YMax - detection.YMin;
            var midX = detection.XMin + (personWidth / 2);
            var midPerc = midX / detection.FrameWidth;

            if((DateTime.Now - lastBearingUpdate).TotalMilliseconds > 250)
            {
                lastBearingUpdate = DateTime.Now;
                bearing = midPerc * 180;
            }

            lastDetectionMidPoint = midPerc;

            if(personHeight < (detection.FrameHeight * 0.80) && personHeight > (detection.FrameHeight * 0.05f))
            {
                _pubSub.Dispatch(new LocomotionTopic
                {
                    Direction = bearing,
                    Speed = personHeight < (detection.FrameHeight * 0.45f) ? 25 : 20
                });
            }
            else
            {
                _pubSub.Dispatch(new LocomotionTopic
                {
                    Direction = 0,
                    Speed = 0
                });
            }
        }

        private void Process()
        {
            while (true)
            {
                if (!subjectInFrame && (DateTime.Now - noSubjectTime).TotalSeconds >= 0.5 && (DateTime.Now - noSubjectTime).TotalSeconds < 2)
                {
                    //No one is in frame...
                    if (lastDetectionMidPoint != 0)
                    {
                        //There WAS someone in frame
                        if(lastDetectionMidPoint < 0.5)
                        {
                            //We last saw them on the left
                            _pubSub.Dispatch(new LocomotionTopic
                            {
                                Direction = 180,
                                Speed = -20     
                            });
                        } else if(lastDetectionMidPoint > 0.5)
                        {
                            //We last saw them on the right
                            _pubSub.Dispatch(new LocomotionTopic
                            {
                                Direction = 0,
                                Speed = -20
                            });
                        }
                    }
                } else if(!subjectInFrame && (DateTime.Now - noSubjectTime).TotalSeconds >= 5)
                {
                    _pubSub.Dispatch(new LocomotionTopic
                    {
                        Speed = 0,
                        Direction = 0
                    });
                }

                Thread.Sleep(10);
            }
        }
    }
}
