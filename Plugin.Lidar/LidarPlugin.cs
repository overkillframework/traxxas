using Newtonsoft.Json.Linq;
using Overkill.Core.Interfaces;
using Overkill.Core.Topics;
using Overkill.PubSub.Interfaces;
using Plugin.Lidar.Properties;
using Plugin.Lidar.Topics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

namespace Plugin.Lidar
{
    /// <summary>
    /// This plugin is for interfacing with RPLidar. This plugin contains a Python script that does the hard work of reading from
    /// the product's serial interface and quickly compiling measurement data. This plugin hosts a UDP socket that the Python script will send
    /// measurements to.
    /// </summary>
    public class LidarPlugin : IPlugin
    {
        const int MEASUREMENT_PACKET_SIZE = 13; //bool(new_scan) + int(quality) + float(angle) + float(distance) 
        const string SCRIPT_FILE = "lidar_scanner.py";

        private IPubSubService pubSub;
        private Thread thread;
        private Socket socket;
        private Process pythonProcess;
        private Stopwatch snapshotStopwatch;

        public LidarPlugin(IPubSubService _pubSub)
        {
            pubSub = _pubSub;
            snapshotStopwatch = new Stopwatch();
        }

        /// <summary>
        /// On initialization, save the embedded Python script to disk and start it. Create the UDP socket that mesurements will be sent to.
        /// </summary>
        public void Initialize()
        {
            Console.WriteLine("Initializing LIDAR");

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

            var startInfo = new ProcessStartInfo();
            startInfo.FileName = "python3";
            startInfo.Arguments = string.Join(" ", new string[] { 
                SCRIPT_FILE, 
                "/dev/ttyUSB0", 
                ((IPEndPoint)socket.LocalEndPoint).Port.ToString() 
            });
            startInfo.UseShellExecute = true;
            startInfo.CreateNoWindow = true;

            pythonProcess = Process.Start(startInfo);

            thread = new Thread(ProcessThread);
            thread.Start();

            snapshotStopwatch.Start();
        }

        /// <summary>
        /// Processes incoming UDP messages from the Python script.
        /// This will dispatch a PubSub message every 400ms. At the moment, it goes directly to viewers' browsers. In the future, this will be decoupled
        /// by having a custom LidarMeasurement Topic so other plugins may use this data on-device as well.
        /// </summary>
        void ProcessThread()
        {
            var measurements = new List<(float Angle, float Distance)>();
            var points = new List<Vector2>();
            while (true)
            {
                var buffer = new byte[MEASUREMENT_PACKET_SIZE];
                if (socket.Available < buffer.Length) continue;
                socket.Receive(buffer, 0, buffer.Length, SocketFlags.None);

                var isNewScan = buffer[0] == 1;
                var quality = BitConverter.ToInt32(buffer.Skip(1).Take(4).ToArray());
                var angle = BitConverter.ToSingle(buffer.Skip(5).Take(4).ToArray());
                var distance = BitConverter.ToSingle(buffer.Skip(9).Take(4).ToArray());

                //New 360 scan, this is the time to dispatch a full scan if needed.
                if (isNewScan)
                {
                    if(snapshotStopwatch.ElapsedMilliseconds > 400)
                    {
                        Console.WriteLine($"{measurements.Count} measurements");
                        pubSub.Dispatch(new LidarCoordinateMapTopic() { Points = points.ToArray() });
                        snapshotStopwatch.Restart();
                    }

                    pubSub.Dispatch(new LidarScanTopic() { Measurements = measurements });
                    points.Clear();
                    measurements.Clear();
                }

                //Make sure its a valid measurement, add it to the snapshot
                if (quality > 0 && distance > 0)
                {
                    var point = ConvertAngleDistanceToCoordinate(angle, distance);
                    points.Add(point);
                    measurements.Add((angle, distance));
                }
            }
        }

        //Convert angle and distance measurements to X,Y coordinates so it may be drawn
        Vector2 ConvertAngleDistanceToCoordinate(float angle, float distance)
        {
            var x = (distance * (float)Math.Cos(Math.PI / 180.0f * angle));
            var y = (distance * (float)Math.Sin(Math.PI / 180.0f * angle));
            return new Vector2() { X = x, Y = y };
        }
    }
}