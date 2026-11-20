using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PWManager.Domain.Models;

namespace PWManager.Infrastructure.Save
{
    internal static class SaveJson
    {
        private static readonly IContractResolver Fields = new SaveFields();

        private static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = Fields,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            DateParseHandling = DateParseHandling.None,
            TypeNameHandling = TypeNameHandling.None,
            CheckAdditionalContent = true
        });

        internal static void Write(TextWriter output, GameSave save)
        {
            using var json = new JsonTextWriter(output) { CloseOutput = false };
            Serializer().Serialize(json, save);
        }

        internal static GameSave Read(TextReader input)
        {
            using var json = new JsonTextReader(input) { CloseInput = false };
            return Serializer().Deserialize<GameSave>(json);
        }

        private sealed class SaveFields : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization serialization) =>
                // Match the existing Unity save schema, excluding computed properties and runtime delegates.
                type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                    .Where(field => !field.IsNotSerialized && !field.IsInitOnly)
                    .Select(field => base.CreateProperty(field, MemberSerialization.Fields)).ToList();
        }
    }

    internal sealed class SavePayloadStream : Stream
    {
        private readonly Stream inner;
        private readonly long limit;
        private readonly bool exactLength;
        internal long BytesProcessed { get; private set; }

        internal SavePayloadStream(Stream inner, long limit, bool exactLength = false)
        {
            this.inner = inner;
            this.limit = limit;
            this.exactLength = exactLength;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesProcessed = checked(BytesProcessed + read);
            if (BytesProcessed > limit) throw new InvalidDataException("Decompressed save exceeds its declared length.");
            if (read == 0 && exactLength && BytesProcessed != limit) throw new InvalidDataException("Incomplete compressed save.");
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (BytesProcessed + count > limit) throw new InvalidDataException("Save exceeds the supported payload length.");
            inner.Write(buffer, offset, count);
            BytesProcessed += count;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesProcessed; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
