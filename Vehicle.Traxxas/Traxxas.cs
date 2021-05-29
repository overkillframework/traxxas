using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Overkill.Common.Enums;
using Overkill.Core.Connections.Data;
using Overkill.Core.Interfaces;
using Overkill.Core.Topics;
using Overkill.Core.Topics.Control;
using Overkill.Core.Topics.Lifecycle;
using Overkill.PubSub.Interfaces;
using Plugin.Lidar.Topics;
using System;
using System.Threading;

namespace TraxxasRobot
{
    public class Traxxas : IVehicle
    {
        private DateTimeOffset lastIsAlive = DateTime.MinValue;

        const int NeutralSignal = 50;
        const int SafetyThrottleMin = 30;
        const int SafetyThrottleMax = 65;
        const int DefaultPowerLimit = 11;

        private readonly ILogger<Traxxas> _logger;
        private IPubSubService pubSub;
        private IInputService inputService;
        private IConnectionInterface connectionInterface;
        private Thread controlThread;

        private int powerLimit = DefaultPowerLimit;
        private int inputThrottle = NeutralSignal;
        private int inputSteering = NeutralSignal;

        public Traxxas(
            ILogger<Traxxas> logger,
            IPubSubService _pubSub, 
            IConnectionInterface _interface, 
            IInputService _inputService
        )
        {
            _logger = logger;
            pubSub = _pubSub;
            inputService = _inputService;
            connectionInterface = _interface;
        }

        public void Initialize()
        {
            pubSub.Subscribe<IsAliveTopic>(topic =>
            {
                lastIsAlive = DateTime.UtcNow;
            });
            pubSub.Subscribe<LocomotionTopic>(HandleLocomotionTopic);
            pubSub.Subscribe<LidarCoordinateMapTopic>(map =>
            {
                Console.WriteLine($"Taxxas got {map.Points?.Length} measurements");
                if (map.Points == null) return;

                pubSub.Dispatch(new PluginMessageTopic()
                {
                    MessageType = "lidar_scan",
                    JSON = JArray.FromObject(map.Points).ToString()
                });
            });
            SetupInputs();

            controlThread = new Thread(new ThreadStart(SendPacket));
            controlThread.IsBackground = true;
            controlThread.Start();
        }

        void SetupInputs()
        {
            inputService.Keyboard("TurnLeft", KeyboardKey.A, state =>
            {
                inputSteering = state == InputState.Pressed ? 20 : NeutralSignal;
            });

            inputService.Keyboard("TurnRight", KeyboardKey.D, state =>
            {
                inputSteering = state == InputState.Pressed ? 80 : NeutralSignal;
            });

            inputService.Keyboard("MoveForward", KeyboardKey.W, state =>
            {
                powerLimit = DefaultPowerLimit;
                inputThrottle = state == InputState.Pressed ? NeutralSignal + powerLimit : NeutralSignal;
                inputThrottle = Math.Clamp(inputThrottle, SafetyThrottleMin, SafetyThrottleMax); //Protect against crazy speed
            });

            inputService.Keyboard("MoveBackward", KeyboardKey.S, state =>
            {
                inputThrottle = state == InputState.Pressed ? NeutralSignal - powerLimit : NeutralSignal;
                inputThrottle = Math.Clamp(inputThrottle, SafetyThrottleMin, SafetyThrottleMax); //Protect against crazy speed
            });

            inputService.GamepadJoystick("Steering", GamepadInput.JoystickL, axes =>
            {
                inputSteering = Math.Abs(axes.x) < 0.05 ? NeutralSignal : NeutralSignal + (int)(axes.x * 50);
            });

            inputService.GamepadJoystick("Throttle", GamepadInput.JoystickR, axes =>
            {
                inputThrottle = Math.Abs(axes.y) < 0.05 ? NeutralSignal : NeutralSignal - (int)(axes.y * 50);
                inputThrottle = Math.Max(SafetyThrottleMin, Math.Min(SafetyThrottleMax, inputThrottle)); //Protect against crazy speed
            });

            inputService.Keyboard("SpeedUp", KeyboardKey.UpArrow, state =>
            {
                if(state == InputState.Pressed)
                {
                    powerLimit = Math.Min(powerLimit + 1, 17);
                    if(inputThrottle > NeutralSignal)
                    {
                        inputThrottle = state == InputState.Pressed ? NeutralSignal + powerLimit : NeutralSignal;
                        inputThrottle = Math.Clamp(inputThrottle, SafetyThrottleMin, SafetyThrottleMax); //Protect against crazy speed
                    }
                }
            });

            inputService.Keyboard("SpeedDown", KeyboardKey.DownArrow, state =>
            {
                if(state == InputState.Pressed)
                {
                    powerLimit = Math.Max(powerLimit - 1, 10);
                }
            });
        }

        void HandleLocomotionTopic(LocomotionTopic topic)
        {
            lastIsAlive = DateTime.UtcNow;
            //Convert angle and speed to throttle and steering
            var throttle = NeutralSignal + ((topic.Speed / 100) * 50);
            float steering = NeutralSignal;
            
            if(topic.Direction >=0 && topic.Direction <= 180)
            {
                throttle = NeutralSignal + ((topic.Speed / 100) * NeutralSignal);
                steering = ((topic.Direction / 180) * 100);
            } else
            {
                throttle = NeutralSignal - ((topic.Speed / 100) * NeutralSignal);
                steering = 100 - (((topic.Direction - 180) / 180) * 100);
            }

            if(throttle != NeutralSignal)
            {
                throttle = Math.Clamp(throttle, SafetyThrottleMin, SafetyThrottleMax);
            }

            throttle = (int)Math.Floor(throttle);
            steering = (int)Math.Floor(steering);
            _logger.LogInformation("Throttle: {throttle}, Steering: {steering}", throttle, steering);

            var brake = topic.Speed == 0 ? 80 : 0; //TODO: Do this differently
            inputThrottle = (int)throttle;
            inputSteering = (int)steering;
        }

        void SendPacket()
        {
            while (true)
            {
                if((DateTime.UtcNow - lastIsAlive).TotalSeconds > 1)
                {
                    inputThrottle = NeutralSignal;
                }

                //Construct and send off the data
                byte checksum = (byte)((85 + 0 + 11 + 0 + inputThrottle + inputSteering + 0 + 0 + 0 + 0) % 256);
                var packet = new byte[] { 85, 0, 11, 0, (byte)inputThrottle, (byte)inputSteering, (byte)0, 0, 0, 0, checksum };

                connectionInterface.Send(new TcpData() { Data = packet });
                Thread.Sleep(100);
            }
        }
    }
}
