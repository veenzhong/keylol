﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keylol.FontGarage.Table
{
    public class BinaryDataTable : IOpenTypeFontTable
    {
        public string Tag { get; set; }
        public byte[] Data { get; set; }

        public void Serialize(BinaryWriter writer, long startOffset, OpenTypeFont font)
        {
            writer.BaseStream.Position = startOffset;
            writer.Write(Data);
        }

        public static BinaryDataTable Deserialize(BinaryReader reader, long startOffset, uint length, string tag)
        {
            reader.BaseStream.Position = startOffset;
            return new BinaryDataTable
            {
                Tag = tag,
                Data = reader.ReadBytes((int) length)
            };
        }
    }
}
