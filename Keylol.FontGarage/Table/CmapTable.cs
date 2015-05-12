﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keylol.FontGarage.Table
{
    public class CmapTable : IOpenTypeFontTable
    {
        public string Tag
        {
            get { return "cmap"; }
        }

        public ushort Version { get; set; }

        public List<Cmap.CmapSubtable> Subtables;

        public void Serialize(BinaryWriter writer)
        {
            throw new NotImplementedException();
        }

        public static CmapTable Deserialize(BinaryReader reader, long startOffset)
        {
            var table = new CmapTable();
            reader.BaseStream.Position = startOffset;

            table.Version = DataTypeConverter.ReadUShort(reader);

            var numberOfSubtables = DataTypeConverter.ReadUShort(reader);
            var platformIds = new ushort[numberOfSubtables];
            var encodingIds = new ushort[numberOfSubtables];
            var offsets = new uint[numberOfSubtables];
            for (var i = 0; i < numberOfSubtables; i++)
            {
                platformIds[i] = DataTypeConverter.ReadUShort(reader);
                encodingIds[i] = DataTypeConverter.ReadUShort(reader);
                offsets[i] = DataTypeConverter.ReadULong(reader);
            }
            var offsetSubtableMap = new Dictionary<uint, Cmap.CmapSubtable>();
            for (var i = 0; i < numberOfSubtables; i++)
            {
                if (offsetSubtableMap.ContainsKey(offsets[i])) // Multiple map
                {
                    offsetSubtableMap[offsets[i]].Environments.Add(new Cmap.Environment
                    {
                        EncodingId = encodingIds[i],
                        PlatformId = platformIds[i]
                    });
                    continue;
                }

                var subtableStartOffset = reader.BaseStream.Position = startOffset + offsets[i];
                var format = DataTypeConverter.ReadUShort(reader);
                Cmap.CmapSubtable subtable;
                switch (format)
                {
                    case 0:
                        subtable = Cmap.Format0Subtable.Deserialize(reader, subtableStartOffset, platformIds[i],
                            encodingIds[i]);
                        break;

                    case 4:
                        subtable = Cmap.Format4Subtable.Deserialize(reader, subtableStartOffset, platformIds[i],
                            encodingIds[i]);
                        break;

                    case 6:
                        subtable = Cmap.Format6Subtable.Deserialize(reader, subtableStartOffset, platformIds[i],
                            encodingIds[i]);
                        break;

                    default:
                        throw new NotImplementedException();
                }
                offsetSubtableMap[offsets[i]] = subtable;
                table.Subtables.Add(subtable);
            }
            return table;
        }

        public CmapTable()
        {
            Version = 0;
            Subtables = new List<Cmap.CmapSubtable>();
        }
    }
}