using OpenNest.IO;
using System;
using System.IO;

namespace OpenNest
{
    public class Document
    {
        public Nest Nest { get; set; }

        public DateTime LastSaveDate { get; private set; }

        public string LastSavePath { get; private set; }

        public Units Units { get; set; }

        public void SaveAs(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            Nest.Name = name;
            LastSaveDate = DateTime.Now;
            LastSavePath = path;

            var writer = new NestWriter(Nest);
            writer.Write(path);
        }
    }
}
