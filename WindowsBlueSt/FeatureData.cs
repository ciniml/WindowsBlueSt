using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsBlueSt
{

    public interface IFeatureData
    {
        void Decode(Stream stream);
    }

    public class BlueStBatteryFeatureData : IFeatureData
    {
        public short Level { get; private set; }
        public short Voltage { get; private set; }
        public short Current { get; private set; }
        public byte PowerManagementStatus { get; private set; }

        public float LevelRatio => this.Level / 1000.0f;

        public BlueStBatteryFeatureData()
        {
        }

        public BlueStBatteryFeatureData(short level, short voltage, short current, byte powerManagementStatus)
        {
            this.Level = level;
            this.Voltage = voltage;
            this.Current = current;
            this.PowerManagementStatus = powerManagementStatus;
        }

        void IFeatureData.Decode(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                this.Level = reader.ReadInt16();
                this.Voltage = reader.ReadInt16();
                this.Current = reader.ReadInt16();
                this.PowerManagementStatus = reader.ReadByte();
            }
        }
    }

    public class BlueStMotionSensorFeatureData : IFeatureData
    {
        public short X { get; private set; }
        public short Y { get; private set; }
        public short Z { get; private set; }

        public BlueStMotionSensorFeatureData()
        {
        }

        public BlueStMotionSensorFeatureData(short x, short y, short z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        void IFeatureData.Decode(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                this.X = reader.ReadInt16();
                this.Y = reader.ReadInt16();
                this.Z = reader.ReadInt16();
            }
        }
    }

    public class BlueStSingleFeatureData<TFeatureData> : IFeatureData
        where TFeatureData : IFeatureData
    {
        public ushort Timestamp { get; private set; }
        private readonly Func<TFeatureData> featureDataConstructor;

        public TFeatureData FeatureData { get; private set; }

        public BlueStSingleFeatureData(Func<TFeatureData> featureDataConstructor)
        {
            this.featureDataConstructor = featureDataConstructor;
        }

        public void Decode(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                this.Timestamp = reader.ReadUInt16();
            }
            this.FeatureData = this.featureDataConstructor();
            this.FeatureData.Decode(stream);
        }
    }

    public class BlueStAggregateFeatureData : IFeatureData
    {
        public ushort Timestamp { get; private set; }
        private readonly Func<IFeatureData[]> featureDataConstructor;

        public IFeatureData[] FeatureData { get; private set; }

        public BlueStAggregateFeatureData(Func<IFeatureData[]> featureDataConstructor)
        {
            this.featureDataConstructor = featureDataConstructor;
        }


        public void Decode(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                this.Timestamp = reader.ReadUInt16();
            }
            this.FeatureData = this.featureDataConstructor();
            foreach (var featureData in this.FeatureData)
            {
                featureData.Decode(stream);
            }
        }
    }
}
