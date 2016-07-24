using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace WindowsBlueSt
{
    [Flags]
    public enum BlueStFeatureMask : uint
    {
        None = 0,
        Gesture = 1<<2,
        CarryPosition = 1<<3,
        Activity = 1<<4,
        SensorFusion = 1<<7,
        SensorFusionCompact = 1<<8,
        Battery = 1<<17,
        Temperature = 1<<18,
        Humidity = 1<<19,
        Pressure = 1<<20,
        Mag = 1<<21,
        Gyro = 1<<22,
        Acc = 1<<23,
        Lux = 1<<24,
        Proxymity = 1<<25,
        MicLevel = 1<<26,
    }

    public enum BlueStDeviceId : byte
    {
        Generic = 0x00,
        StevalWesu1 = 0x01,
        GenericNucleo = 0x80,
    }

    public class BlueStAdvertisementData
    {
        public byte ProtocolVersion { get; }
        public BlueStDeviceId DeviceId { get; }
        public BlueStFeatureMask FeatureMask { get; }
        public byte[] DeviceMac { get; }

        public BlueStAdvertisementData(byte[] data, int offset)
        {
            if( offset < 0 ) throw new ArgumentOutOfRangeException(nameof(offset));
            if( data.Length - offset < 0 ) throw new ArgumentOutOfRangeException(nameof(offset));
            var length = data[offset];
            if( length != 0x07 && length != 0x0d) throw new ArgumentException("Invalid length field in the data", nameof(data));
            var fieldType = data[offset + 1];
            if( fieldType != 0xff ) throw new ArgumentException("Invalid field type of the data", nameof(data));

            this.ProtocolVersion = data[offset + 2];
            if(this.ProtocolVersion != 0x01 ) throw new ArgumentException("Invalid protocol version in the data.", nameof(data));

            this.DeviceId = (BlueStDeviceId) data[offset + 3];
            this.FeatureMask = (BlueStFeatureMask) (data[offset + 4] | (data[offset + 5] << 8) | (data[offset + 6] << 16) | (data[offset + 7] << 24));

            if (length == 0x0d)
            {
                this.DeviceMac = new byte[6];
                Array.Copy(data, offset + 8, this.DeviceMac, 0, this.DeviceMac.Length);
            }
        }
    }


    public static class BleUtil
    {
        public static readonly string BluetoothGattDeviceServiceInterfaceClassGuid = "{6E3BB679-4372-40C8-9EAA-4509DF260CD8}";
        private const string ContainerIdProperty = "System.Devices.ContainerId";
        private const string InterfaceClassGuidProperty = "System.Devices.InterfaceClassGuid";
        private const string ServiceGuidProperty = "System.DeviceInterface.Bluetooth.ServiceGuid";
        private const string InterfaceEnabledProperty = "System.Devices.InterfaceEnabled";
        private const string AqsBooleanFalse = "System.StructuredQueryType.Boolean#False";
        private const string AqsBooleanTrue = "System.StructuredQueryType.Boolean#True";

        public static string GetDeviceSelecorEndsWithGuid(string guidPattern)
        {
            return $"{InterfaceClassGuidProperty}:={BluetoothGattDeviceServiceInterfaceClassGuid} AND {InterfaceEnabledProperty}:={AqsBooleanTrue} AND {ServiceGuidProperty}:~>\"{guidPattern}}}\"";
        }

        public static string AppendContainerIdCriterion(string aqsString, Guid containerId)
        {
            return $"{aqsString} AND {ContainerIdProperty}:=\"{{{containerId}}}\"";
        }

        public static async Task<IEnumerable<IGrouping<Guid, DeviceInformation>>> FindAllDeviceServicesAsync(string aqsFilter, CancellationToken cancellationToken)
        {
            return (await DeviceInformation.FindAllAsync(aqsFilter, new[] {ContainerIdProperty}).AsTask(cancellationToken)).GroupByDevice();
        }

        public static IEnumerable<IGrouping<Guid, DeviceInformation>> GroupByDevice(this IEnumerable<DeviceInformation> devices)
        {
            return devices.GroupBy(device => (Guid)device.Properties[ContainerIdProperty]);
        }
    }
    public class BlueSt
    {
        public static readonly Guid DataServiceUuid = new Guid("00000000-0001-11e1-9ab4-0002a5d5c51b");
        public static readonly Guid DebugServiceUuid = new Guid("00000000-000e-11e1-9ab4-0002a5d5c51b");
        public static readonly Guid ConfigServiceUuid = new Guid("00000000-000f-11e1-9ab4-0002a5d5c51b");

        public static readonly Guid ConfigRegisterAccessCharacteristicUuid = new Guid("00000001-000f-11e1-ac36-0002a5d5c51b");

        private const string BlueStDataGuidPattern = "-0001-11e1-ac36-0002a5d5c51b";
        


        public static string GetBlueStServiceSelector()
        {
            //return BleUtil.GetDeviceSelecorEndsWithGuid(BlueStDataGuidPattern);
            return GattDeviceService.GetDeviceSelectorFromUuid(DataServiceUuid);
        }

        public static Guid GetDataCharacteristicGuid(uint featureMask)
        {
            return new Guid(featureMask.ToString("X08") + BlueStDataGuidPattern);
        }

        public static async Task<BlueStDeviceInformation[]> FindAllBlueStDevices(CancellationToken cancellationToken)
        {
            var selector = GetBlueStServiceSelector();
            var deviceInformations = await BleUtil.FindAllDeviceServicesAsync(selector, cancellationToken);
            return deviceInformations.Select(deviceInformation => new BlueStDeviceInformation(deviceInformation.Key)).ToArray();
        }

        public static BlueStFeatureMask GetFeatureMaskFromUuid(Guid uuid)
        {
            var bytes = uuid.ToByteArray();
            var featureMask = (uint)bytes[0];
            featureMask |= (uint)(bytes[1] << 8);
            featureMask |= (uint)(bytes[2] << 16);
            featureMask |= (uint)(bytes[3] << 24);
            return (BlueStFeatureMask) featureMask;
        }
    }

    public struct BlueStFeatureData
    {
        public Stream Data { get; }
        public DateTimeOffset Timestamp { get; }
        public BlueStFeatureMask Features { get; }
        public BlueStFeatureDataSource Source { get; }

        public BlueStFeatureData(BlueStFeatureMask features, DateTimeOffset timestamp, Stream data)
        {
            this.Features = features;
            this.Timestamp = timestamp;
            this.Data = data;
            this.Source = null;
        }
        public BlueStFeatureData(BlueStFeatureDataSource source, DateTimeOffset timestamp, Stream data)
        {
            this.Features = source.Features;
            this.Timestamp = timestamp;
            this.Data = data;
            this.Source = source;
        }
    }

    public class BlueStFeatureDataSource : IEquatable<BlueStFeatureDataSource>
    {
        private readonly ushort characteristicAttributeId;
        public BlueStFeatureMask Features { get; }

        public bool Equals(BlueStFeatureDataSource other)
        {
            return (object)other != null && 
                (object.ReferenceEquals(this, other) ||
                 (this.characteristicAttributeId == other.characteristicAttributeId && this.Features == other.Features));
        }

        public override bool Equals(object other)
        {
            return this.Equals(other as BlueStFeatureDataSource);
        }

        public override int GetHashCode()
        {
            return this.characteristicAttributeId.GetHashCode() ^ this.Features.GetHashCode();
        }

        public static bool operator ==(BlueStFeatureDataSource lhs, BlueStFeatureDataSource rhs)
        {
            return object.ReferenceEquals(lhs, rhs) ||
                   ((object) lhs != null && (object) rhs != null && lhs.Equals(rhs));
        }

        public static bool operator !=(BlueStFeatureDataSource lhs, BlueStFeatureDataSource rhs)
        {
            return !(lhs == rhs);
        }

        internal BlueStFeatureDataSource(ushort characteristicAttributeId, BlueStFeatureMask features)
        {
            this.characteristicAttributeId = characteristicAttributeId;
            Features = features;
        }
    }

    public class FeatureDataReceivedEventArgs : EventArgs
    {
        public BlueStFeatureData Data { get; }

        public FeatureDataReceivedEventArgs(BlueStFeatureData data)
        {
            this.Data = data;
        }
    }

    public class BlueStDeviceInformation
    {
        public Guid Id { get; }

        internal BlueStDeviceInformation(Guid id)
        {
            this.Id = id;
        }
    }

    public enum BlueStConfigRegisterPersistence
    {
        Session,
        Persistent,
    }

    public class BlueStDevice
    {
        private readonly Guid deviceId;
        private readonly GattDeviceService[] services;
        private readonly GattDeviceService debugService;
        private readonly GattDeviceService configService;

        private GattCharacteristic[] characteristics;
        private GattCharacteristic configRegisterAccess;

        private bool isConfigRegisterNotificationEnabled = false;

        public BlueStFeatureMask Features { get; }

        public event EventHandler<FeatureDataReceivedEventArgs> FeatureDataReceived;

        public static  async Task<BlueStDevice> CreateFromId(Guid deviceId, BlueStFeatureMask[] aggregateFeatures, CancellationToken cancellationToken)
        {
            // Check all aggregate characteristics
            if (aggregateFeatures.Cast<uint>().Any(x => (x & (x - 1)) == 0))
            {
                // There are some single features in the aggregateFeatures collection.
                throw new ArgumentException("Single features in aggregate features collection.", nameof(aggregateFeatures));
            }

            var serviceSelector = BleUtil.AppendContainerIdCriterion(BlueSt.GetBlueStServiceSelector(), deviceId);
            var serviceDeviceInformations = await DeviceInformation.FindAllAsync(serviceSelector).AsTask(cancellationToken);

            //
            var services = new List<GattDeviceService>();
            foreach (var serviceDeviceInformation in serviceDeviceInformations)
            {
                var service = await GattDeviceService.FromIdAsync(serviceDeviceInformation.Id).AsTask(cancellationToken);
                services.Add(service);
            }

            // Locate additional services.
            GattDeviceService debugService = null;
            GattDeviceService configService = null;
            {
                // Find debug service
                var selector = GattDeviceService.GetDeviceSelectorFromUuid(BlueSt.DebugServiceUuid);
                var selectorInThisDevice = BleUtil.AppendContainerIdCriterion(selector, deviceId);
                var serviceDevices = await DeviceInformation.FindAllAsync(selectorInThisDevice).AsTask(cancellationToken);
                var serviceDevice = serviceDevices.SingleOrDefault();
                if (serviceDevice != null)
                {
                    debugService = await GattDeviceService.FromIdAsync(serviceDevice.Id).AsTask(cancellationToken);
                }
            }

            {
                // Find config service
                var selector = GattDeviceService.GetDeviceSelectorFromUuid(BlueSt.ConfigServiceUuid);
                var selectorInThisDevice = BleUtil.AppendContainerIdCriterion(selector, deviceId);
                var serviceDevices = await DeviceInformation.FindAllAsync(selectorInThisDevice).AsTask(cancellationToken);
                var serviceDevice = serviceDevices.SingleOrDefault();
                if (serviceDevice != null)
                {
                    configService = await GattDeviceService.FromIdAsync(serviceDevice.Id).AsTask(cancellationToken);
                }
            }

            return new BlueStDevice(deviceId, services.ToArray(), debugService, configService, aggregateFeatures);
        }

        private static IEnumerable<GattCharacteristic> GetDataCharacteristics(GattDeviceService service, BlueStFeatureMask[] aggregateFeatures)
        {
            // This class only supports single-feature characteristics at this time.
            // We have to use Windows 10 or later to support aggregate-feature characteristics, 
            // which requires enumeration of characteristics in services.
            return Enumerable.Range(0, 31)
                .Select(bit => BlueSt.GetDataCharacteristicGuid((1u << bit)))
                .Concat(aggregateFeatures.Select(feature => BlueSt.GetDataCharacteristicGuid((uint)feature)))
                .SelectMany(guid => service.GetCharacteristics(guid));
        }

        private void OnFeatureDataReceived(GattCharacteristic characteristic, GattValueChangedEventArgs e)
        {
            var featureMask = BlueSt.GetFeatureMaskFromUuid(characteristic.Uuid);
            var dataSource = new BlueStFeatureDataSource(characteristic.AttributeHandle, featureMask);
            using (var stream = e.CharacteristicValue.AsStream())
            {
                var data = new BlueStFeatureData(dataSource, e.Timestamp, stream);

                this.FeatureDataReceived?.Invoke(this, new FeatureDataReceivedEventArgs(data));
            }
        }

        internal BlueStDevice(Guid deviceId, GattDeviceService[] services, GattDeviceService debugService, GattDeviceService configService, BlueStFeatureMask[] aggregateFeatures )
        {
            this.deviceId = deviceId;
            this.services = services;
            this.debugService = debugService;
            this.configService = configService;

            
            // Get all feature data characteristics.
            this.characteristics = this.services.SelectMany(service => GetDataCharacteristics(service, aggregateFeatures)).ToArray();

            // Subscribe ValueChanged event
            foreach (var characteristic in this.characteristics)
            {
                characteristic.ValueChanged += (sender, args) => this.OnFeatureDataReceived(characteristic, args);
            }

            // Calculate supported features.
            this.Features = this.characteristics
                .Select(characteristic => BlueSt.GetFeatureMaskFromUuid(characteristic.Uuid))
                .Aggregate((l, r) => l | r);

            // Get configuration service characteristics
            if (this.configService != null)
            {
                this.configRegisterAccess = this.configService.GetCharacteristics(BlueSt.ConfigRegisterAccessCharacteristicUuid).SingleOrDefault();
            }
        }
        

        [Flags]
        private enum RegisterAccessControlField : byte
        {
            None = 0,
            PendingExec = 1<<7,
            Persistent = 1<<6,
            Write = 1<<5,
            Error = 1<<4,
            AckRequired = 1<<3,
        }

        public async Task ReadConfigurationRegisterAsync(BlueStConfigRegisterPersistence persistence, int registerIndex, byte[] data, int offset, int length, CancellationToken cancellationToken)
        {
            if( this.configRegisterAccess == null ) throw new NotSupportedException("Accessing configuration registers is not supported.");
            if( (length & 1) != 0 ) throw new ArgumentException("Length must be a multiple of 2.", nameof(length));

            {
                var control = RegisterAccessControlField.PendingExec | RegisterAccessControlField.AckRequired;
                if (persistence == BlueStConfigRegisterPersistence.Persistent)
                {
                    control |= RegisterAccessControlField.Persistent;
                }

                // Send read register request to the peripheral.

                var buffer = new byte[4]
                {
                    (byte) control,
                    (byte) registerIndex,
                    (byte) 0,
                    (byte) (length/2),
                };
                var runtimeBuffer = buffer.AsBuffer();
                var result =
                    await

                        this.configRegisterAccess.WriteValueAsync(runtimeBuffer, GattWriteOption.WriteWithResponse)
                            .AsTask(cancellationToken);
                if (result != GattCommunicationStatus.Success) throw new Exception("Failed to access to the register.");
            }

            while(true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await this.configRegisterAccess.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (result.Status != GattCommunicationStatus.Success) throw new Exception("Failed to access to the register.");
                var header = new byte[4];
                using (var stream = result.Value.AsStream())
                {
                    stream.Read(header, 0, header.Length);
                    var control = (RegisterAccessControlField) header[0];
                    if (control.HasFlag(RegisterAccessControlField.Error))
                    {
                        // The result has an error. Check the error code.
                        var errorCode = header[2];
                        throw new Exception(
                            $"The peripheral returned an error while accessing a register. code={errorCode:X02}");
                    }
                    if (control.HasFlag(RegisterAccessControlField.PendingExec)) continue;

                    stream.Read(data, offset, length);
                    break;
                }
            }
        }

        public async Task WriteConfigurationRegisterAsync(BlueStConfigRegisterPersistence persistence, int registerIndex, byte[] data, int offset, int length, CancellationToken cancellationToken)
        {
            if (this.configRegisterAccess == null) throw new NotSupportedException("Accessing configuration registers is not supported.");
            if ((length & 1) != 0) throw new ArgumentException("Length must be a multiple of 2.", nameof(length));

            {
                var control = RegisterAccessControlField.PendingExec | RegisterAccessControlField.AckRequired | RegisterAccessControlField.Write;
                if (persistence == BlueStConfigRegisterPersistence.Persistent)
                {
                    control |= RegisterAccessControlField.Persistent;
                }

                // Send read register request to the peripheral.

                var buffer = new byte[length + 4];
                buffer[0] = (byte) control;
                buffer[1] = (byte) registerIndex;
                buffer[2] = 0;
                buffer[3] = (byte) (length/2);
                System.Buffer.BlockCopy(data, offset, buffer, 4, length);

                var runtimeBuffer = buffer.AsBuffer();
                var result = await this.configRegisterAccess.WriteValueAsync(runtimeBuffer, GattWriteOption.WriteWithResponse).AsTask(cancellationToken);
                if (result != GattCommunicationStatus.Success) throw new Exception("Failed to access to the register.");
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await this.configRegisterAccess.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (result.Status != GattCommunicationStatus.Success) throw new Exception("Failed to access to the register.");
                var header = new byte[4];
                using (var stream = result.Value.AsStream())
                {
                    stream.Read(header, 0, header.Length);
                    var control = (RegisterAccessControlField)header[0];
                    if (control.HasFlag(RegisterAccessControlField.Error))
                    {
                        // The result has an error. Check the error code.
                        var errorCode = header[2];
                        throw new Exception(
                            $"The peripheral returned an error while accessing a register. code={errorCode:X02}");
                    }
                    if (!control.HasFlag(RegisterAccessControlField.PendingExec)) break;
                }
            }
        }

        public async Task DisableAllFeatureDataNotifications()
        {
            foreach (var characteristic in this.characteristics)
            {
                try
                {
                    var newCccd = GattClientCharacteristicConfigurationDescriptorValue.None;
                    var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(newCccd);
                    if (result == GattCommunicationStatus.Unreachable) throw new InvalidOperationException("Failed to set CCCD");
                }
                catch (Exception e) when((uint)e.HResult == 0x8007001fu)
                {
                }
            }
        }

        public async Task EnableFeatureDataNotification(BlueStFeatureMask targetFeatures)
        {
            foreach (var characteristic in this.characteristics)
            {
                var featureMask = BlueSt.GetFeatureMaskFromUuid(characteristic.Uuid);
                if ((featureMask & targetFeatures) == BlueStFeatureMask.None) continue;
                if (!characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)) continue;
                
                var newCccd = GattClientCharacteristicConfigurationDescriptorValue.Notify;
                var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(newCccd);
                if( result == GattCommunicationStatus.Unreachable ) throw new InvalidOperationException("Failed to set CCCD");
            }
        }


    }
}
