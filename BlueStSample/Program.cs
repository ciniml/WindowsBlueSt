using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowsBlueSt;
using WindowsBlueSt.WeSU;

namespace BlueStSample
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Press [ENTER] to exit.");

            using (var cancelSource = new CancellationTokenSource())
            {
                var task = MainAsyncOuter(args, cancelSource.Token);
                Console.ReadLine();
                cancelSource.Cancel();
                Console.WriteLine("Waiting util the main task finishes.");
                task.Wait();
            }
        }

        static async Task MainAsyncOuter(string[] args, CancellationToken cancellationToken)
        {
            try
            {
                await MainAsync(args, cancellationToken);
            }
            catch (AggregateException e) when(e.Flatten().InnerExceptions.SingleOrDefault() is OperationCanceledException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
            }
        }

        static async Task MainAsync(string[] args, CancellationToken cancellationToken)
        {
            Console.WriteLine("Finding BlueST devices...");
            var deviceInformations = await BlueSt.FindAllBlueStDevices(cancellationToken);

            if (deviceInformations.Length == 0)
            {
                Console.WriteLine("No BlueST devices found. Please check connection status of device.");
                return;
            }
            if (deviceInformations.Length > 1)
            {
                Console.WriteLine("Two or more BlueST devices are found. Force to use the first one.");
            }
            var deviceInformation = deviceInformations.First();

            var aggregateFeatures = new BlueStFeatureMask[]
            {
                BlueStFeatureMask.Acc | BlueStFeatureMask.Gyro | BlueStFeatureMask.Mag,
            };

            var blueStDevice = await BlueStDevice.CreateFromId(deviceInformation.Id, aggregateFeatures, cancellationToken);
            Console.WriteLine("Paired device was found. Trying to connect to this device...");
            await blueStDevice.DisableAllFeatureDataNotifications();

            Console.WriteLine("Connected to device successfully.");
            Console.WriteLine($"Supported Features: {blueStDevice.Features}");

            {
                Console.WriteLine("Reading configuration...");
                var config = new WeSUConfig();
                await config.LoadRegistersAsync(blueStDevice, cancellationToken);

                Console.WriteLine($"Firmware version: 0x{config.FirmwareVersion:X04}");

                // Configure registers
                config.AccelerometerFullScale = 16;
                config.AccelerometerOutputDataRate = 208;
                config.GyroFullScale = 2000;
                config.GyroOutputDataRate = 208;
                config.MagnetometerFullScale = 12;
                config.MagnetometerOutputDataRate = 80;
                config.TimerFrequency = 133;

                await config.SaveRegistersAsync(blueStDevice, cancellationToken);
            }

            var dataQueue = new ConcurrentQueue<BlueStAggregateFeatureData>();

            blueStDevice.FeatureDataReceived += (sender, eventArgs) =>
            {
                if (cancellationToken.IsCancellationRequested) return;  // If cancelled, do nothing.

                //Console.WwriteLine($"${eventArgs.Data.Features}: {eventArgs.Data.Timestamp}");
                if (eventArgs.Data.Features == BlueStFeatureMask.Battery)
                {
                    // Battery single feature
                    var stream = eventArgs.Data.Data;
                    
                    var batteryData = new BlueStSingleFeatureData<BlueStBatteryFeatureData>(() => new BlueStBatteryFeatureData());
                    batteryData.Decode(stream);

                    Console.WriteLine($"\tTime:{eventArgs.Data.Timestamp:o} Timestamp:{batteryData.Timestamp}, Level:{batteryData.FeatureData.LevelRatio:P1}");
                    
                }
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
                    dataQueue.Enqueue(motion);

                    //Console.WriteLine($"\tTimestamp:{eventArgs.Data.Timestamp.Millisecond} Index:{motion.Timestamp}, ({data[0].X}, {data[0].Y}, {data[0].Z})");
                    
                }
            };

            Console.WriteLine("Enable notifications for all features.");
            await blueStDevice.EnableFeatureDataNotification(BlueStFeatureMask.Battery | BlueStFeatureMask.Acc);

            // Write received data.
            var writeoutTask = Task.Run(async () =>
            {
                using (var writer = new StreamWriter("output.csv", false, Encoding.UTF8))
                {
                    while (!(dataQueue.IsEmpty && cancellationToken.IsCancellationRequested))
                    {
                        BlueStAggregateFeatureData motion;
                        if (dataQueue.TryDequeue(out motion))
                        {
                            var data = motion.FeatureData.Cast<BlueStMotionSensorFeatureData>().ToArray();
                            writer.Write(motion.Timestamp);
                            foreach (var featureData in data)
                            {
                                writer.Write($",{featureData.X},{featureData.Y},{featureData.Z}");
                            }
                        }
                        else
                        {
                            await Task.Delay(1, cancellationToken);
                        }
                    }
                }
            }, cancellationToken);

            await writeoutTask;
            // Disable all notifications before disconnecting from the peripheral.
            await blueStDevice.DisableAllFeatureDataNotifications();
        }
    }
}
