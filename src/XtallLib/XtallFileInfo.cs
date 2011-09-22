using System;

namespace XtallLib
{
    public class XtallFileInfo
    {
        public string Filename { get; private set; }
        public string Md5Hash { get; private set; }
        public long ByteCount { get; private set; }

        public XtallFileInfo(string filename, string md5Hash, long byteCount)
        {
            if (filename == null)
                throw new ArgumentNullException("filename");
            if (md5Hash == null)
                throw new ArgumentNullException("md5Hash");
            if (byteCount < 0)
                throw new ArgumentException("byteCount must be greater than or equal to zero");

            Filename = filename;
            Md5Hash = md5Hash;
            ByteCount = byteCount;
        }
    }
}