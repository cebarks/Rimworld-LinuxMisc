#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal sealed class CpuAffinityMask : IEquatable<CpuAffinityMask>
    {
        private readonly byte[] bytes;
        private int? count;
        private int[]? cpuIds;

        private CpuAffinityMask(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public static CpuAffinityMask Empty { get; } = new CpuAffinityMask(Array.Empty<byte>());

        public int ByteLength => bytes.Length;

        public int Count
        {
            get
            {
                if (count.HasValue)
                    return count.Value;

                int bitCount = 0;
                foreach (byte value in bytes)
                {
                    byte remaining = value;
                    while (remaining != 0)
                    {
                        bitCount++;
                        remaining &= (byte)(remaining - 1);
                    }
                }

                count = bitCount;
                return bitCount;
            }
        }

        public bool IsEmpty => Count == 0;

        public IReadOnlyList<int> CpuIds
        {
            get
            {
                if (cpuIds != null)
                    return cpuIds;

                var ids = new List<int>();
                for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
                {
                    byte value = bytes[byteIndex];
                    if (value == 0)
                        continue;

                    for (int bit = 0; bit < 8; bit++)
                    {
                        if ((value & (1 << bit)) != 0)
                            ids.Add((byteIndex * 8) + bit);
                    }
                }

                cpuIds = ids.ToArray();
                return cpuIds;
            }
        }

        public static CpuAffinityMask FromBytes(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            return new CpuAffinityMask((byte[])bytes.Clone());
        }

        public static CpuAffinityMask FromCpuIds(IEnumerable<int> cpuIds, int minimumByteLength = 0)
        {
            if (cpuIds == null)
                throw new ArgumentNullException(nameof(cpuIds));
            if (minimumByteLength < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumByteLength));

            int[] ids = cpuIds.Distinct().OrderBy(id => id).ToArray();
            if (ids.Any(id => id < 0))
                throw new ArgumentOutOfRangeException(nameof(cpuIds), "CPU IDs cannot be negative");

            int requiredLength = ids.Length == 0 ? 0 : (ids[ids.Length - 1] / 8) + 1;
            var bytes = new byte[Math.Max(minimumByteLength, requiredLength)];
            foreach (int id in ids)
                bytes[id / 8] |= (byte)(1 << (id % 8));

            return new CpuAffinityMask(bytes);
        }

        public byte[] ToBytes()
        {
            return (byte[])bytes.Clone();
        }

        public CpuAffinityMask Intersect(CpuAffinityMask other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            var intersection = new byte[bytes.Length];
            int sharedLength = Math.Min(bytes.Length, other.bytes.Length);
            for (int i = 0; i < sharedLength; i++)
                intersection[i] = (byte)(bytes[i] & other.bytes[i]);

            return new CpuAffinityMask(intersection);
        }

        public bool Equals(CpuAffinityMask? other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null)
                return false;

            int significantLength = SignificantLength(bytes);
            if (significantLength != SignificantLength(other.bytes))
                return false;

            for (int i = 0; i < significantLength; i++)
            {
                if (bytes[i] != other.bytes[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as CpuAffinityMask);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            int length = SignificantLength(bytes);
            for (int i = 0; i < length; i++)
                hash.Add(bytes[i]);
            return hash.ToHashCode();
        }

        private static int SignificantLength(byte[] value)
        {
            int length = value.Length;
            while (length > 0 && value[length - 1] == 0)
                length--;
            return length;
        }
    }
}
