using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TsMap.FileSystem.Zip;
using TsMap.Helpers;

namespace TsMap.FileSystem
{
    public class LocalFileEntry : Entry
    {
        public LocalFileEntry(string path) : base(path)
        {
        }

        public override bool IsCompressed()
        {
            return false;
        }

        public override bool IsDirectory()
        {
            throw new NotImplementedException();
        }

        public override byte[] Read()
        {
            return File.ReadAllBytes(this._path);
        }

        protected override byte[] Inflate(byte[] buff)
        {
            throw new NotImplementedException();
        }
    }
}
