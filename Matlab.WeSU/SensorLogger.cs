using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowsBlueSt;
using WindowsBlueSt.WeSU;

namespace Matlab.WeSU
{
    public static class SensorLogger
    {
        private static BlueStDevice blueStDevice;
        private static bool isLogging = false;
        private static List<int[]> loggedData;
        private static readonly EventHandler<FeatureDataReceivedEventArgs> featureDataReceived = OnFeatureDataReceived;
        private static int sampleIndex;

        public static int TimeoutMilliseconds { get; set; } = 10000;
        public const ushort MaxAccelerometerRate = 208;
        public const ushort MaxGyroRate = 208;
        public const ushort MaxMagnetometerRate = 80;
        public const ushort MaxDataRate = 133;

        public static void Connect(int timeoutMilliseconds)
        {
            var cancelSource = new CancellationTokenSource(timeoutMilliseconds);
            var findDevicesTask = BlueSt.FindAllBlueStDevices(cancelSource.Token);
            findDevicesTask.Wait(cancelSource.Token);
            var deviceInformation = findDevicesTask.Result.SingleOrDefault();
            if (deviceInformation == null) throw new Exception("Device not found.");

            var aggregateFeatures = new BlueStFeatureMask[]
            {
                BlueStFeatureMask.Acc | BlueStFeatureMask.Gyro | BlueStFeatureMask.Mag,
            };
            var createFromIdTask = BlueStDevice.CreateFromId(deviceInformation.Id, aggregateFeatures, cancelSource.Token);
            createFromIdTask.Wait(cancelSource.Token);
            blueStDevice = createFromIdTask.Result;

            blueStDevice.DisableAllFeatureDataNotifications().Wait(cancelSource.Token);
        }

        private static void OnFeatureDataReceived(object sender, FeatureDataReceivedEventArgs eventArgs)
        {
            if (!isLogging) return;  // If cancelled, do nothing.

            if (eventArgs.Data.Features == (BlueStFeatureMask.Acc | BlueStFeatureMask.Gyro | BlueStFeatureMask.Mag))
            {
                // Accelerometer single feature
                var stream = eventArgs.Data.Data;
                
                var motion = new BlueStAggregateFeatureData(() => new IFeatureData[]
                {
                        new BlueStMotionSensorFeatureData(),
                        new BlueStMotionSensorFeatureData(),
                        new BlueStMotionSensorFeatureData(),
                });
                motion.Decode(stream);
                var data = motion.FeatureData.Cast<BlueStMotionSensorFeatureData>().ToArray();

                var row = new int[10]
                {
                    sampleIndex,
                    data[0].X,
                    data[0].Y,
                    data[0].Z,
                    data[2].X,
                    data[2].Y,
                    data[2].Z,
                    data[1].X,
                    data[1].Y,
                    data[1].Z,
                };
                Interlocked.Increment(ref sampleIndex);

                loggedData.Add(row);
            }
        }
        public static void Start(ushort accelerometerRate, ushort gyroRate, ushort magnetometerRate, ushort dataRate)
        {
            if( blueStDevice == null)  throw new InvalidOperationException("Must be connected to the device before starting an acquisition.");
            if(isLogging) throw new InvalidOperationException("Cannot start logging twice at the same time.");

            var cancelSource = new CancellationTokenSource(TimeoutMilliseconds);

            var config = new WeSUConfig();
            config.LoadRegistersAsync(blueStDevice, cancelSource.Token).Wait(cancelSource.Token);
            
            // Configure registers
            config.AccelerometerFullScale = 16;
            config.AccelerometerOutputDataRate = accelerometerRate;
            config.GyroFullScale = 2000;
            config.GyroOutputDataRate = gyroRate;
            config.MagnetometerFullScale = 12;
            config.MagnetometerOutputDataRate = magnetometerRate;
            config.TimerFrequency = dataRate;

            config.SaveRegistersAsync(blueStDevice, cancelSource.Token).Wait(cancelSource.Token);

            loggedData = new List<int[]>();
            sampleIndex = 0;
            blueStDevice.FeatureDataReceived += featureDataReceived;
            isLogging = true;

            blueStDevice.EnableFeatureDataNotification(BlueStFeatureMask.Acc).Wait(cancelSource.Token);
        }

        public static int[][] Stop()
        {
            if (!isLogging) throw new InvalidOperationException("No logging are started.");

            var cancelSource = new CancellationTokenSource(TimeoutMilliseconds);

            blueStDevice.DisableAllFeatureDataNotifications().Wait(cancelSource.Token);
            blueStDevice.FeatureDataReceived -= featureDataReceived;
            isLogging = false;

            return loggedData.ToArray();
        }

        public static int[][] Measure(int durationSeconds)
        {
            Start(MaxAccelerometerRate, MaxGyroRate, MaxMagnetometerRate, MaxDataRate);
            Thread.Sleep(durationSeconds*1000);
            return Stop();
        }
    }
}
