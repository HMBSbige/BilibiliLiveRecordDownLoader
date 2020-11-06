using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagPackets
{
    public class AMFMetadata : IBytesStruct
    {
        #region Data

        private Dictionary<string, object> _data;
        public IDictionary<string, object> Data => _data;

        #endregion

        public bool UseArray { get; set; }

        public AMFMetadata()
        {
            _data = new Dictionary<string, object>
            {
                [@"duration"] = 0.0
            };
        }

        public int Size => Count(@"onMetaData") + Count(_data);

        public Memory<byte> ToMemory(Memory<byte> array)
        {
            var res = array.Slice(0, Size);

            var offset = Encode(res.Span, @"onMetaData");

            Encode(res.Span.Slice(offset), _data);

            return res;
        }

        public void Read(Span<byte> buffer)
        {
            buffer = Decode(buffer, out var value);
            if (value is string name && name == @"onMetaData")
            {
                Decode(buffer, out value);
                if (value is Dictionary<string, object> d)
                {
                    _data = d;
                    if (!_data.ContainsKey(@"duration"))
                    {
                        _data[@"duration"] = 0.0;
                    }

                    _data.Remove(string.Empty);
                    foreach (var (key, o) in _data.ToArray())
                    {
                        if (o is string str && str.Contains('\0'))
                        {
                            _data[key] = str.Replace("\0", string.Empty);
                        }
                    }
                    return;
                }
            }
            throw new NotSupportedException($@"MetaData parse error: {value}");
        }

        private static Span<byte> DecodeString(Span<byte> buffer, out object value)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(buffer);
            value = Encoding.UTF8.GetString(buffer.Slice(sizeof(ushort), length));
            buffer = buffer.Slice(length + sizeof(ushort));
            return buffer;
        }

        private static Span<byte> Decode(Span<byte> buffer, out object value)
        {
            var type = (AMF0)buffer[0];
            value = null;
            buffer = buffer.Slice(1);

            switch (type)
            {
                case AMF0.Number:
                {
                    // TODO:.NET 5.0 value = BinaryPrimitives.ReadDoubleBigEndian(buffer);;
                    value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(buffer));
                    buffer = buffer.Slice(sizeof(double));
                    break;
                }
                case AMF0.Boolean:
                {
                    value = Convert.ToBoolean(buffer[0]);
                    buffer = buffer.Slice(1);
                    break;
                }
                case AMF0.String:
                {
                    buffer = DecodeString(buffer, out value);
                    break;
                }
                case AMF0.Object:
                {
                    var o = new Dictionary<string, object>();
                    while (buffer[0] != 0 || buffer[1] != 0 || buffer[2] != 9)
                    {
                        buffer = DecodeString(buffer, out var key);
                        buffer = Decode(buffer, out var v);
                        o[(string)key] = v;
                    }
                    value = o;
                    buffer = buffer.Slice(3); // AMF0.ObjectEnd 00 00 09
                    break;
                }
                case AMF0.Null:
                case AMF0.Undefined:
                {
                    break;
                }
                case AMF0.ECMAArray:
                {
                    buffer = buffer.Slice(sizeof(uint)); // length
                    goto case AMF0.Object;
                }
                case AMF0.StrictArray:
                {
                    var length = BinaryPrimitives.ReadUInt32BigEndian(buffer);
                    var list = new List<object>(Math.Max(0, (int)length));
                    for (var i = 0u; i < length; ++i)
                    {
                        buffer = Decode(buffer, out var v);
                        list.Add(v);
                    }
                    value = list;
                    break;
                }
                case AMF0.Date:
                {
                    // TODO:.NET 5.0 value = BinaryPrimitives.ReadDoubleBigEndian(buffer);
                    var datetime = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(buffer));
                    var localDateTimeOffset = BinaryPrimitives.ReadInt16BigEndian(buffer);

                    value = DateTime.UnixEpoch.AddMilliseconds(datetime).AddMinutes(-localDateTimeOffset);
                    buffer = buffer.Slice(sizeof(double) + sizeof(short));
                    break;
                }
                case AMF0.LongString:
                {
                    var length = BinaryPrimitives.ReadUInt32BigEndian(buffer);
                    if (length > int.MaxValue)
                    {
                        throw new OutOfMemoryException($@"String is too long: {length} > {int.MaxValue}");
                    }
                    value = Encoding.UTF8.GetString(buffer.Slice(sizeof(uint), (int)length));
                    buffer = buffer.Slice((int)length + sizeof(uint));
                    break;
                }
                default:
                {
                    throw new NotSupportedException($@"Unsupported AMF type: {type}");
                }
            }

            return buffer;
        }

        private int Encode(Span<byte> array, object value)
        {
            switch (value)
            {
                case double number:
                {
                    array[0] = (byte)AMF0.Number;
                    // TODO:.NET 5.0 BinaryPrimitives.WriteDoubleBigEndian(array.Slice(1), number);
                    BinaryPrimitives.WriteInt64BigEndian(array.Slice(1), BitConverter.DoubleToInt64Bits(number));
                    return Count(number);
                }
                case bool b:
                {
                    array[0] = (byte)AMF0.Boolean;
                    array[1] = Convert.ToByte(b);
                    return Count(b);
                }
                case string str:
                {
                    var strCount = Encoding.UTF8.GetByteCount(str);
                    if (strCount > ushort.MaxValue)
                    {
                        array[0] = (byte)AMF0.LongString;
                        BinaryPrimitives.WriteUInt32BigEndian(array.Slice(1), (uint)strCount);
                        Encoding.UTF8.GetBytes(str, array.Slice(1 + sizeof(uint)));
                        return 1 + strCount + sizeof(uint);
                    }

                    array[0] = (byte)AMF0.String;
                    BinaryPrimitives.WriteUInt16BigEndian(array.Slice(1), (ushort)strCount);
                    Encoding.UTF8.GetBytes(str, array.Slice(1 + sizeof(ushort)));
                    return 1 + strCount + sizeof(ushort);
                }
                case Dictionary<string, object> o:
                {
                    int current;
                    if (UseArray)
                    {
                        array[0] = (byte)AMF0.ECMAArray;
                        BinaryPrimitives.WriteUInt32BigEndian(array.Slice(1), (uint)o.Count);
                        current = 5;
                    }
                    else
                    {
                        array[0] = (byte)AMF0.Object;
                        current = 1;
                    }
                    foreach (var (key, v) in o)
                    {
                        var length = Math.Min(Encoding.UTF8.GetByteCount(key), ushort.MaxValue);
                        BinaryPrimitives.WriteUInt16BigEndian(array.Slice(current), (ushort)length);
                        current += 2;

                        Encoding.UTF8.GetBytes(key, array.Slice(current));
                        current += length;

                        current += Encode(array.Slice(current), v);
                    }

                    var span = array.Slice(current);
                    span[0] = 0;
                    span[1] = 0;
                    span[2] = 9;
                    return current + 3;
                }
                case List<object> list:
                {
                    array[0] = (byte)AMF0.StrictArray;
                    BinaryPrimitives.WriteUInt32BigEndian(array.Slice(1), (uint)list.Count);
                    var current = 5;
                    foreach (var o in list)
                    {
                        current += Encode(array.Slice(current), o);
                    }
                    return current;
                }
                case DateTime dataTime:
                {
                    array[0] = (byte)AMF0.Date;

                    var time = dataTime.ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalMilliseconds;
                    // TODO:.NET 5.0 BinaryPrimitives.WriteDoubleBigEndian(array.Slice(1), time);
                    BinaryPrimitives.WriteInt64BigEndian(array.Slice(1), BitConverter.DoubleToInt64Bits(time));

                    BinaryPrimitives.WriteInt16BigEndian(array.Slice(1 + sizeof(double)), 0);

                    return Count(dataTime);
                }
                default:
                {
                    throw new NotSupportedException($@"{value.GetType().FullName} is not supported");
                }
            }
        }

        private int Count(object value)
        {
            switch (value)
            {
                case double _:
                {
                    return 1 + sizeof(double);
                }
                case bool _:
                {
                    return 1 + sizeof(bool);
                }
                case string str:
                {
                    var strCount = Encoding.UTF8.GetByteCount(str);
                    if (strCount > ushort.MaxValue)
                    {
                        return 1 + sizeof(int) + strCount;
                    }
                    return 1 + sizeof(ushort) + strCount;
                }
                case Dictionary<string, object> o:
                {
                    var count = UseArray ? 1 + 4 + 3 : 1 + 3;
                    foreach (var (key, v) in o)
                    {
                        count += sizeof(ushort);
                        count += Math.Min(Encoding.UTF8.GetByteCount(key), ushort.MaxValue);
                        count += Count(v);
                    }
                    return count;
                }
                case List<object> list:
                {
                    return 1 + sizeof(uint) + list.Sum(Count);
                }
                case DateTime _:
                {
                    return 1 + sizeof(double) + sizeof(short);
                }
                default:
                {
                    throw new NotSupportedException($@"{value.GetType().FullName} is not supported");
                }
            }
        }
    }
}
