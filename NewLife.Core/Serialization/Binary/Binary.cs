﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NewLife.Log;
using NewLife.Reflection;

namespace NewLife.Serialization
{
    /// <summary>二进制序列化</summary>
    public class Binary : FormatterBase, IBinary
    {
        #region 属性
        private Boolean _EncodeInt;
        /// <summary>使用7位编码整数。默认false不使用</summary>
        public Boolean EncodeInt { get { return _EncodeInt; } set { _EncodeInt = value; } }

        private Boolean _IsLittleEndian;
        /// <summary>小端字节序。默认false，是大端而不是小端</summary>
        public Boolean IsLittleEndian { get { return _IsLittleEndian; } set { _IsLittleEndian = value; } }

        private Boolean _UseFieldSize;
        /// <summary>使用指定大小的FieldSizeAttribute特性，默认false</summary>
        public Boolean UseFieldSize { get { return _UseFieldSize; } set { _UseFieldSize = value; } }

        private List<IBinaryHandler> _Handlers = new List<IBinaryHandler>();
        /// <summary>处理器列表</summary>
        public List<IBinaryHandler> Handlers { get { return _Handlers; } }
        #endregion

        #region 构造
        /// <summary>实例化</summary>
        public Binary()
        {
            // 遍历所有处理器实现
            var list = new List<IBinaryHandler>();
            //foreach (var item in typeof(IBinaryHandler).GetAllSubclasses(true))
            //{
            //    var handler = item.CreateInstance() as IBinaryHandler;
            //    handler.Host = this;
            //    list.Add(handler);
            //}
            list.Add(new BinaryGeneral { Host = this });
            list.Add(new BinaryComposite { Host = this });
            list.Add(new BinaryList { Host = this });
            //list.Add(new BinaryDictionary { Host = this });
            // 根据优先级排序
            list.Sort();

            _Handlers = list;
        }
        #endregion

        #region 处理器
        /// <summary>添加处理器</summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        public Binary AddHandler(IBinaryHandler handler)
        {
            if (handler != null)
            {
                handler.Host = this;
                _Handlers.Add(handler);
                // 根据优先级排序
                _Handlers.Sort();
            }

            return this;
        }

        /// <summary>添加处理器</summary>
        /// <typeparam name="THandler"></typeparam>
        /// <param name="priority"></param>
        /// <returns></returns>
        public Binary AddHandler<THandler>(Int32 priority = 0) where THandler : IBinaryHandler, new()
        {
            var handler = new THandler();
            handler.Host = this;
            if (priority != 0) handler.Priority = priority;

            return AddHandler(handler);
        }
        #endregion

        #region 写入
        /// <summary>写入一个对象</summary>
        /// <param name="value">目标对象</param>
        /// <param name="type">类型</param>
        /// <returns></returns>
        public virtual Boolean Write(Object value, Type type = null)
        {
            if (type == null)
            {
                if (value == null) return true;

                type = value.GetType();

                // 一般类型为空是顶级调用
                if (Hosts.Count == 0) WriteLog("BinaryWrite {0} {1}", type.Name, value);
            }

            foreach (var item in Handlers)
            {
                if (item.Write(value, type)) return true;
            }
            return false;
        }

        /// <summary>写入字节</summary>
        /// <param name="value"></param>
        public virtual void Write(Byte value) { Stream.WriteByte(value); }

        /// <summary>将字节数组部分写入当前流，不写入数组长度。</summary>
        /// <param name="buffer">包含要写入的数据的字节数组。</param>
        /// <param name="offset">buffer 中开始写入的起始点。</param>
        /// <param name="count">要写入的字节数。</param>
        public virtual void Write(Byte[] buffer, Int32 offset = 0, Int32 count = -1)
        {
            if (count < 0) count = buffer.Length - offset;
            Stream.Write(buffer, offset, count);
        }

        /// <summary>写入大小，如果有FieldSize则返回，否则写入编码的大小</summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public virtual Int32 WriteSize(Int32 size)
        {
            if (UseFieldSize)
            {
                var fieldsize = GetFieldSize();
                if (fieldsize >= 0) return fieldsize;
            }

            if (EncodeInt)
                WriteEncoded(size);
            else
            {
                Write(size);
            }
            return -1;
        }

        /// <summary>
        /// 以7位压缩格式写入32位整数，小于7位用1个字节，小于14位用2个字节。
        /// 由每次写入的一个字节的第一位标记后面的字节是否还是当前数据，所以每个字节实际可利用存储空间只有后7位。
        /// </summary>
        /// <param name="value">数值</param>
        /// <returns>实际写入字节数</returns>
        Int32 WriteEncoded(Int32 value)
        {
            var list = new List<Byte>();

            Int32 count = 1;
            UInt32 num = (UInt32)value;
            while (num >= 0x80)
            {
                list.Add((byte)(num | 0x80));
                num = num >> 7;

                count++;
            }
            list.Add((byte)num);

            Write(list.ToArray(), 0, list.Count);

            return count;
        }
        #endregion

        #region 读取
        /// <summary>读取指定类型对象</summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public virtual Object Read(Type type)
        {
            var value = type.CreateInstance();
            if (!TryRead(type, ref value)) throw new Exception("读取失败！");

            return value;
        }

        /// <summary>读取指定类型对象</summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Read<T>()
        {
            return (T)(Object)Read(typeof(T));
        }

        /// <summary>尝试读取指定类型对象</summary>
        /// <param name="type"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public virtual Boolean TryRead(Type type, ref Object value)
        {
            if (Hosts.Count == 0) WriteLog("BinaryRead {0} {1}", type.Name, value);
            
            foreach (var item in Handlers)
            {
                if (item.TryRead(type, ref value)) return true;
            }
            return false;
        }

        /// <summary>读取字节</summary>
        /// <returns></returns>
        public virtual Byte ReadByte()
        {
            var b = Stream.ReadByte();
            if (b < 0) throw new Exception("数据流超出范围！");
            return (Byte)b;
        }

        /// <summary>从当前流中将 count 个字节读入字节数组</summary>
        /// <param name="count">要读取的字节数。</param>
        /// <returns></returns>
        public virtual Byte[] ReadBytes(Int32 count)
        {
            var buffer = new Byte[count];
            Stream.Read(buffer, 0, count);

            return buffer;
        }

        /// <summary>读取大小</summary>
        /// <returns></returns>
        public virtual Int32 ReadSize()
        {
            if (UseFieldSize)
            {
                var size = GetFieldSize();
                if (size >= 0) return size;
            }

            if (EncodeInt)
                return ReadEncodedInt32();
            else
                //return ReadInt32();
                return (Int32)Read(typeof(Int32));
            //var sizeFormat = TypeCode.Int32;
            //switch (sizeFormat)
            //{
            //    case TypeCode.Int16:
            //        return ReadInt16();
            //    case TypeCode.UInt16:
            //        return ReadEncodedInt16();
            //    case TypeCode.Int32:
            //    case TypeCode.Int64:
            //    default:
            //        return ReadInt32();
            //    case TypeCode.UInt32:
            //    case TypeCode.UInt64:
            //        return ReadEncodedInt32();
            //}
        }

        Int32 GetFieldSize()
        {
            var member = Member as MemberInfo;
            if (member != null)
            {
                // 获取FieldSizeAttribute特性
                var att = member.GetCustomAttribute<FieldSizeAttribute>();
                if (att != null)
                {
                    // 如果指定了固定大小，直接返回
                    if (att.Size > 0 && String.IsNullOrEmpty(att.ReferenceName)) return att.Size;

                    // 如果指定了引用字段，则找引用字段所表示的长度
                    var size = att.GetReferenceSize(Hosts.Peek(), member);
                    if (size >= 0) return size;
                }
            }

            return -1;
        }
        #endregion

        #region 有符号整数
        /// <summary>读取整数的字节数组，某些写入器（如二进制写入器）可能需要改变字节顺序</summary>
        /// <param name="count">数量</param>
        /// <returns></returns>
        Byte[] ReadIntBytes(Int32 count) { return Stream.ReadBytes(count); }

        /// <summary>从当前流中读取 2 字节有符号整数，并使流的当前位置提升 2 个字节。</summary>
        /// <returns></returns>
        short ReadInt16() { return BitConverter.ToInt16(ReadIntBytes(2), 0); }

        /// <summary>从当前流中读取 4 字节有符号整数，并使流的当前位置提升 4 个字节。</summary>
        /// <returns></returns>
        Int32 ReadInt32() { return BitConverter.ToInt32(ReadIntBytes(4), 0); }
        #endregion

        #region 7位压缩编码整数
        /// <summary>以压缩格式读取16位整数</summary>
        /// <returns></returns>
        public Int16 ReadEncodedInt16()
        {
            Byte b;
            Int16 rs = 0;
            Byte n = 0;
            while (true)
            {
                b = ReadByte();
                // 必须转为Int16，否则可能溢出
                rs += (Int16)((b & 0x7f) << n);
                if ((b & 0x80) == 0) break;

                n += 7;
                if (n >= 16) throw new FormatException("数字值过大，无法使用压缩格式读取！");
            }
            return rs;
        }

        /// <summary>以压缩格式读取32位整数</summary>
        /// <returns></returns>
        public Int32 ReadEncodedInt32()
        {
            Byte b;
            Int32 rs = 0;
            Byte n = 0;
            while (true)
            {
                b = ReadByte();
                // 必须转为Int32，否则可能溢出
                rs += (Int32)((b & 0x7f) << n);
                if ((b & 0x80) == 0) break;

                n += 7;
                if (n >= 32) throw new FormatException("数字值过大，无法使用压缩格式读取！");
            }
            return rs;
        }

        /// <summary>以压缩格式读取64位整数</summary>
        /// <returns></returns>
        public Int64 ReadEncodedInt64()
        {
            Byte b;
            Int64 rs = 0;
            Byte n = 0;
            while (true)
            {
                b = ReadByte();
                // 必须转为Int64，否则可能溢出
                rs += (Int64)(b & 0x7f) << n;
                if ((b & 0x80) == 0) break;

                n += 7;
                if (n >= 64) throw new FormatException("数字值过大，无法使用压缩格式读取！");
            }
            return rs;
        }
        #endregion

        #region 辅助函数

        #endregion

        #region 跟踪日志
        /// <summary>使用跟踪流。实际上是重新包装一次Stream，必须在设置Stream，使用之前</summary>
        public virtual void EnableTrace()
        {
            var stream = Stream;
            if (stream == null || stream is TraceStream) return;

            Stream = new TraceStream(stream) { Encoding = this.Encoding, IsLittleEndian = this.IsLittleEndian };
        }

        private Boolean _Debug;
        /// <summary>是否调试</summary>
        public Boolean Debug { get { return _Debug; } set { _Debug = value; } }

        /// <summary>输出日志</summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        public void WriteLog(String format, params Object[] args)
        {
            if (Debug) XTrace.WriteLine(format, args);
        }
        #endregion
    }
}