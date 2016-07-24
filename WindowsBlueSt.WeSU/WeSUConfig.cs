using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowsBlueSt;

namespace WindowsBlueSt.WeSU
{
    /// <summary>
    /// Attribute to show the target property should be mapped to a register in WeSU.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    internal class WeSUConfigRegisterAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the index of the target register.
        /// </summary>
        public int Index { get; set; }
        
        public WeSUConfigRegisterAttribute()
        {
        }

        public WeSUConfigRegisterAttribute(int index)
        {
            this.Index = index;
        }
    }

    /// <summary>
    /// A class which represents values in the configuration registers in WeSU.
    /// </summary>
    public class WeSUConfig
    {
        private static readonly PropertyInfo[] registerProperties = 
            typeof(WeSUConfig).GetProperties()
                .Where(property => property.GetCustomAttribute<WeSUConfigRegisterAttribute>() != null)
                .ToArray();

        /// <summary>
        /// Firmware version number. This register is read-only.
        /// </summary>
        [WeSUConfigRegister(0x00)]
        public ushort FirmwareVersion { get; private set; }

        /// <summary>
        /// Full scale of accelerometer.
        /// </summary>
        [WeSUConfigRegister(0x74)]
        public ushort AccelerometerFullScale { get; set; }
        /// <summary>
        /// Output data rate of accelerometer.
        /// </summary>
        [WeSUConfigRegister(0x75)]
        public ushort AccelerometerOutputDataRate { get; set; }
        /// <summary>
        /// Full scale of gyro in [degree/s]
        /// </summary>
        [WeSUConfigRegister(0x76)]
        public ushort GyroFullScale { get; set; }
        /// <summary>
        /// Output data rate of gyro.
        /// </summary>
        [WeSUConfigRegister(0x77)]
        public ushort GyroOutputDataRate { get; set; }
        /// <summary>
        /// Full scale of magnetometer in [gauss]
        /// </summary>
        [WeSUConfigRegister(0x78)]
        public ushort MagnetometerFullScale { get; set; }
        /// <summary>
        /// Output data rate of magnetometer.
        /// </summary>
        [WeSUConfigRegister(0x79)]
        public ushort MagnetometerOutputDataRate { get; set; }
        /// <summary>
        /// Data upload frequency.
        /// </summary>
        [WeSUConfigRegister(0x21)]
        public ushort TimerFrequency { get; set; }

        public async Task LoadRegistersAsync(BlueStDevice device, CancellationToken cancellationToken)
        {
            var buffer = new byte[4];
            foreach (var property in registerProperties)
            {
                var attribute = property.GetCustomAttribute<WeSUConfigRegisterAttribute>();
                int length = 0;
                if (property.PropertyType == typeof(ushort))
                {
                    length = 2;
                    await device.ReadConfigurationRegisterAsync(BlueStConfigRegisterPersistence.Persistent, attribute.Index, buffer, 0, length, cancellationToken);
                    var value = BitConverter.ToUInt16(buffer, 0);
                    property.SetValue(this, value);
                }
            }
        }

        public async Task SaveRegistersAsync(BlueStDevice device, CancellationToken cancellationToken)
        {
            foreach (var property in registerProperties)
            {
                var attribute = property.GetCustomAttribute<WeSUConfigRegisterAttribute>();
                if (property.PropertyType == typeof(ushort))
                {
                    var setter = property.GetSetMethod(false);
                    if (setter == null) continue;   // No public setter exists. This register is not writable.

                    var value = (ushort)property.GetValue(this);
                    var buffer = BitConverter.GetBytes(value);
                    await device.WriteConfigurationRegisterAsync(BlueStConfigRegisterPersistence.Persistent, attribute.Index, buffer, 0, buffer.Length, cancellationToken);
                }
            }
        }
    }
}
