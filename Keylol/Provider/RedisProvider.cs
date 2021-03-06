﻿using System;
using System.Configuration;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using StackExchange.Redis;

namespace Keylol.Provider
{
    /// <summary>
    ///     提供 Redis 服务
    /// </summary>
    public class RedisProvider : IDisposable
    {
        private bool _disposed;

        /// <summary>
        ///     Redis <see cref="ConnectionMultiplexer" /> 对象
        /// </summary>
        public ConnectionMultiplexer Connection { get; } =
            ConnectionMultiplexer.Connect(ConfigurationManager.AppSettings["redisConnection"] ??
                                          "localhost,abortConnect=false,allowAdmin=true");

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     获取指定 Redis Database
        /// </summary>
        /// <param name="db">Database 编号</param>
        /// <returns>IDatabase 对象</returns>
        public IDatabase GetDatabase(int db = -1) => Connection.GetDatabase(db);

        /// <summary>
        ///     将对象序列化成 JSON
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="object">要序列化的对象</param>
        /// <returns>序列化结果 BSON</returns>
        public static byte[] Serialize<T>(T @object)
        {
            var ms = new MemoryStream();
            using (var writer = new JsonTextWriter(new StreamWriter(ms)))
            {
                var serializer = new JsonSerializer {Converters = {new StringEnumConverter()}};
                serializer.Serialize(writer, @object);
            }
            return ms.ToArray();
        }

        /// <summary>
        ///     将 JSON 反序列化为对象
        /// </summary>
        /// <param name="data">要反序列化的 BSON</param>
        /// <returns>反序列化后的对象</returns>
        public static T Deserialize<T>(byte[] data)
        {
            var ms = new MemoryStream(data);
            using (var reader = new JsonTextReader(new StreamReader(ms)))
            {
                var serializer = new JsonSerializer();
                return serializer.Deserialize<T>(reader);
            }
        }

        /// <summary>
        ///     资源清理
        /// </summary>
        /// <param name="disposing">是否清理托管对象</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                Connection.Dispose();
            }
            _disposed = true;
        }
    }
}